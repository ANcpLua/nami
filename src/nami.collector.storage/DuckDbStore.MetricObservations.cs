using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

    private static void RefreshMetricProjection(DuckDBConnection connection, DbTransaction transaction,
        MetricObservationKey key,
        MetricObservationRow? arriving = null) =>
        WriteMetricProjection(connection, transaction, key,
            ReadMetricProjection(connection, transaction, key, arriving));

    /// <summary>
    ///     Observations of one measurement project it. A different measurement at a recorded timestamp never
    ///     replaces the first: the point keeps the measurement recorded first (the stored point, or else the
    ///     first of the arriving export) and is marked conflicting, while the journal keeps every observation.
    ///     A rebuild cannot tell which arrived first, so its conflicting points keep no measurement.
    /// </summary>
    private static MetricPointRow? ReadMetricProjection(DuckDBConnection connection, DbTransaction transaction,
        MetricObservationKey key,
        MetricObservationRow? arriving)
    {
        MetricObservationRow first;
        long measurements;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT " + MetricObservationRow.SelectColumnList +
                                  ", count(DISTINCT measurement_id) OVER () FROM metric_observations " +
                                  "WHERE project_id = $1 AND series_id = $2 AND time_unix_nano = $3 ORDER BY observation_id LIMIT 1";
            AddParameters(command, key.ProjectId, key.SeriesId, (decimal)key.TimeUnixNano);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            first = MetricObservationRow.MapFromReader(reader);
            measurements = reader.GetInt64(MetricObservationRow.ColumnCount);
        }

        if (measurements <= 1) return MetricProjection.Project(first, false);
        var kept = ReadMetricPoint(connection, transaction, key) ??
                   (arriving is null ? null : MetricProjection.Project(arriving, false));
        return kept is null ? MetricProjection.Project(first, true) : kept with { HasConflict = 1 };
    }

    private static MetricPointRow? ReadMetricPoint(DuckDBConnection connection, DbTransaction transaction,
        MetricObservationKey key)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT " + MetricPointRow.SelectColumnList +
                              " FROM metric_points WHERE project_id = $1 AND series_id = $2 AND time_unix_nano = $3";
        AddParameters(command, key.ProjectId, key.SeriesId, (decimal)key.TimeUnixNano);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MetricPointRow.MapFromReader(reader) : null;
    }

    private static void WriteMetricProjection(DuckDBConnection connection, DbTransaction transaction,
        MetricObservationKey key, MetricPointRow? projection)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (projection is null)
        {
            command.CommandText =
                "DELETE FROM metric_points WHERE project_id = $1 AND series_id = $2 AND time_unix_nano = $3";
            AddParameters(command, key.ProjectId, key.SeriesId, (decimal)key.TimeUnixNano);
            command.ExecuteNonQuery();
        }
        else
        {
            command.CommandText = MetricPointRow.BuildMultiRowInsertSql(1);
            MetricPointRow.AddParameters(command, projection);
            command.ExecuteNonQuery();
        }

        command.Parameters.Clear();
        command.CommandText =
            "DELETE FROM metric_exemplars WHERE project_id = $1 AND series_id = $2 AND time_unix_nano = $3";
        AddParameters(command, key.ProjectId, key.SeriesId, (decimal)key.TimeUnixNano);
        command.ExecuteNonQuery();
        if (projection is null) return;
        command.Parameters.Clear();
        command.CommandText = """
                              INSERT INTO metric_exemplars (project_id, series_id, time_unix_nano, trace_id)
                              SELECT $1, $2, $3, trace_id FROM (
                                  SELECT json_extract_string(e.value, '$.trace_id') AS trace_id
                                  FROM metric_observations o, json_each(o.exemplars_json) e
                                  WHERE o.project_id = $1 AND o.series_id = $2 AND o.time_unix_nano = $3
                                  UNION
                                  SELECT json_extract_string(e.value, '$') AS trace_id
                                  FROM metric_observations o, json_each(o.historical_exemplar_trace_ids_json) e
                                  WHERE o.project_id = $1 AND o.series_id = $2 AND o.time_unix_nano = $3
                              ) WHERE trace_id IS NOT NULL ON CONFLICT DO NOTHING
                              """;
        AddParameters(command, key.ProjectId, key.SeriesId, (decimal)key.TimeUnixNano);
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

            foreach (var key in keys) RefreshMetricProjection(connection, transaction, key);
            if (keys.Count < 1000) break;
            after = keys[^1];
        }

        transaction.Commit();
    }

    private readonly record struct MetricObservationKey(string ProjectId, string SeriesId, ulong TimeUnixNano);
}
