using DuckDB.NET.Data;

namespace Nami.Collector.Storage;

internal sealed partial class DuckDbStore
{
    public Task<IReadOnlyList<MetricObservationRow>> ListMetricPointsAsync(
        string projectId, string metricName, string seriesId, int limit, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return ExecuteReadAsync<IReadOnlyList<MetricObservationRow>>(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT " + MetricObservationRow.SelectColumnList +
                                  " FROM metric_observations WHERE project_id = $1 AND series_id = $2 " +
                                  "AND EXISTS (SELECT 1 FROM metric_series s WHERE s.project_id = $1 " +
                                  "AND s.series_id = $2 AND s.metric_name = $3) " +
                                  "ORDER BY time_unix_nano DESC, observation_id LIMIT $4";
            AddParameters(command, projectId, seriesId, metricName, limit);
            var rows = new List<MetricObservationRow>();
            using var reader = command.ExecuteReader();
            while (reader.Read()) rows.Add(MetricObservationRow.MapFromReader(reader));
            return rows;
        }, ct);
    }

    private static bool MetricProjectionsNeedRebuild(DuckDBConnection connection)
    {
        using var command = connection.CreateCommand();
        // A process can stop after the schema transaction commits but before projection
        // rebuilding starts. Compare keys on every open so that state recovers deterministically.
        command.CommandText = """
                              SELECT EXISTS (
                                  (SELECT project_id, series_id, time_unix_nano FROM metric_observations
                                   EXCEPT SELECT project_id, series_id, time_unix_nano FROM metric_points)
                                  UNION ALL
                                  (SELECT project_id, series_id, time_unix_nano FROM metric_points
                                   EXCEPT SELECT project_id, series_id, time_unix_nano FROM metric_observations)
                              )
                              """;
        return Convert.ToBoolean(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static readonly StagedInsert<MetricPointRow> s_metricPointStage = new(MetricPointRow.StageTableName,
        MetricPointRow.CreateStageSql, MetricPointRow.StageKeyOccurrencesSql, MetricPointRow.MergeStageSql,
        MetricPointRow.MergeStageOccurrenceSql, MetricPointRow.DropStageSql, MetricPointRow.AppendStageRow,
        MetricPointRow.InsertSql, MetricPointRow.AddParameters);

    /// <summary>The temporary table of the writer's connection that names the measurements a set of statements projects.</summary>
    private const string ProjectionKeys = "temp.main.metric_projection_keys";

    /// <summary>
    ///     Projects the measurements at <paramref name="keys" /> from their observations, in the caller's transaction
    ///     and in a number of statements that does not grow with the keys: one statement a key took 26 to 31 s for a
    ///     request of 3,000 data points and more memory than a limit of 512 MiB allows (ledger N108). The keys of a
    ///     batch go through the appender into a temporary table that every statement joins; one key, what a sender
    ///     without batching exports, is bound as parameters instead.
    ///     <para>
    ///         Observations of one measurement project it. A different measurement at a recorded timestamp never
    ///         replaces the first: the point keeps the measurement recorded first (the stored point, or else the
    ///         first of the arriving export, <paramref name="arriving" />) and is marked conflicting, while the journal
    ///         keeps every observation. A rebuild cannot tell which arrived first, so its conflicting points keep no
    ///         measurement. A key without observations loses its point; exemplars follow the observations.
    ///     </para>
    /// </summary>
    private static void RefreshMetricProjections(DuckDBConnection connection, DbTransaction transaction,
        IReadOnlyList<MetricObservationKey> keys,
        IReadOnlyDictionary<MetricObservationKey, MetricObservationRow>? arriving, CancellationToken ct)
    {
        if (keys.Count is 0) return;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var batch = keys.Count > 1;
        if (batch)
        {
            command.CommandText = "CREATE OR REPLACE TEMP TABLE metric_projection_keys " +
                                  "(project_id VARCHAR, series_id VARCHAR, time_unix_nano UBIGINT)";
            command.ExecuteNonQuery();
            using var appender = connection.CreateAppender("temp", "main", "metric_projection_keys");
            for (var i = 0; i < keys.Count; i++)
            {
                if ((i & 0xFF) is 0) ct.ThrowIfCancellationRequested();
                appender.CreateRow().AppendValue(keys[i].ProjectId).AppendValue(keys[i].SeriesId)
                    .AppendValue((ulong?)keys[i].TimeUnixNano).EndRow();
            }
        }
        else
        {
            AddParameters(command, keys[0].ProjectId, keys[0].SeriesId, (decimal)keys[0].TimeUnixNano);
        }

        // The rows of a table (or of its alias) that are at one of the keys. Every statement below names the keys
        // through this and nothing else, so the one key's three parameters fit each of them.
        string At(string table) => batch
            ? $"EXISTS (SELECT 1 FROM {ProjectionKeys} k WHERE k.project_id = {table}.project_id " +
              $"AND k.series_id = {table}.series_id AND k.time_unix_nano = {table}.time_unix_nano)"
            : $"{table}.project_id = $1 AND {table}.series_id = $2 AND {table}.time_unix_nano = $3";

        // CA2100: the statements are written here from constants; the keys are bound parameters or went through the
        // appender.
        var projections = new List<MetricPointRow>(keys.Count);
        List<MetricObservationRow>? conflicting = null;
        command.CommandText =
            $"SELECT {MetricObservationRow.SelectColumnList}, measurements FROM (SELECT *, " +
            "count(DISTINCT measurement_id) OVER (PARTITION BY project_id, series_id, time_unix_nano) AS measurements, " +
            "row_number() OVER (PARTITION BY project_id, series_id, time_unix_nano ORDER BY observation_id) AS occurrence " +
            $"FROM metric_observations WHERE {At("metric_observations")}) WHERE occurrence = 1";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                if ((projections.Count & 0xFF) is 0) ct.ThrowIfCancellationRequested();
                var first = MetricObservationRow.MapFromReader(reader);
                if (reader.GetInt64(MetricObservationRow.ColumnCount) <= 1)
                    projections.Add(MetricProjection.Project(first, false));
                else
                    (conflicting ??= []).Add(first);
            }
        }

        if (conflicting is not null)
        {
            var stored = new Dictionary<MetricObservationKey, MetricPointRow>();
            command.CommandText = $"SELECT {MetricPointRow.SelectColumnList} FROM metric_points WHERE {At("metric_points")}";
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var point = MetricPointRow.MapFromReader(reader);
                    stored[new MetricObservationKey(point.ProjectId, point.SeriesId, point.TimeUnixNano)] = point;
                }
            }

            foreach (var first in conflicting)
            {
                var key = new MetricObservationKey(first.ProjectId, first.SeriesId, first.TimeUnixNano);
                projections.Add(stored.TryGetValue(key, out var kept)
                    ? kept with { HasConflict = 1 }
                    : arriving is not null && arriving.TryGetValue(key, out var arrived)
                        ? MetricProjection.Project(arrived, false) with { HasConflict = 1 }
                        : MetricProjection.Project(first, true));
            }
        }

        if (projections.Count < keys.Count)
        {
            command.CommandText =
                $"DELETE FROM metric_points WHERE {At("metric_points")} AND NOT EXISTS (SELECT 1 FROM metric_observations o " +
                "WHERE o.project_id = metric_points.project_id AND o.series_id = metric_points.series_id " +
                "AND o.time_unix_nano = metric_points.time_unix_nano)";
            command.ExecuteNonQuery();
        }

        InsertRowsStaged(connection, transaction, projections, s_metricPointStage, ct);

        // The exemplars of a key are the trace ids of its observations: those that no observation carries any more
        // go, the others are added. No row is deleted and written again.
        var carried = "SELECT o.project_id, o.series_id, o.time_unix_nano, " +
                      "json_extract_string(e.value, '$.trace_id') AS trace_id " +
                      $"FROM metric_observations o, json_each(o.exemplars_json) e WHERE {At("o")}";
        command.CommandText =
            $"DELETE FROM metric_exemplars WHERE {At("metric_exemplars")} AND NOT EXISTS (SELECT 1 FROM ({carried}) c " +
            "WHERE c.project_id = metric_exemplars.project_id AND c.series_id = metric_exemplars.series_id " +
            "AND c.time_unix_nano = metric_exemplars.time_unix_nano AND c.trace_id = metric_exemplars.trace_id)";
        command.ExecuteNonQuery();
        command.CommandText =
            "INSERT INTO metric_exemplars (project_id, series_id, time_unix_nano, trace_id) " +
            $"SELECT DISTINCT project_id, series_id, time_unix_nano, trace_id FROM ({carried}) " +
            "WHERE trace_id IS NOT NULL ON CONFLICT DO NOTHING";
        command.ExecuteNonQuery();
        if (!batch) return;
        command.CommandText = $"DROP TABLE IF EXISTS {ProjectionKeys}";
        command.ExecuteNonQuery();
    }

    private static void RebuildMetricProjections(DuckDBConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM metric_points; DELETE FROM metric_exemplars";
            clear.ExecuteNonQuery();
        }

        MetricObservationKey? after = null;
        while (true)
        {
            var keys = new List<MetricObservationKey>();
            using (var read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText = "SELECT DISTINCT project_id, series_id, time_unix_nano FROM metric_observations " +
                                   (after is null
                                       ? ""
                                       : "WHERE (project_id, series_id, time_unix_nano) > ($1, $2, $3) ") +
                                   "ORDER BY project_id, series_id, time_unix_nano LIMIT 1000";
                if (after is { } cursor)
                    AddParameters(read, cursor.ProjectId, cursor.SeriesId, (decimal)cursor.TimeUnixNano);
                using var reader = read.ExecuteReader();
                while (reader.Read())
                {
                    keys.Add(new MetricObservationKey(reader.GetString(0), reader.GetString(1),
                        DuckDbValueReader.Read<ulong>(reader, 2, 0)));
                }
            }

            RefreshMetricProjections(connection, transaction, keys, null, CancellationToken.None);
            if (keys.Count < 1000) break;
            after = keys[^1];
        }

        transaction.Commit();
    }

    private readonly record struct MetricObservationKey(string ProjectId, string SeriesId, ulong TimeUnixNano);
}
