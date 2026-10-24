using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DuckDB.NET.Data;

namespace Nami.Collector.Storage;

internal sealed partial class DuckDbStore
{
    private const int MaxMetricSeriesPerBatch = 120;
    private const int MaxMetricPointsPerBatch = 150;

    public async Task InsertMetricsAsync(
        IReadOnlyList<MetricSeriesRow> series,
        IReadOnlyList<MetricObservationRow> observations,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (observations.Count is 0)
            return;

        var indexedSeries = series.Any(static row => row.InstrumentId is null)
            ? series.Select(static row => row.InstrumentId is null
                ? row with { InstrumentId = MetricStorageMapper.InstrumentId(row) }
                : row).ToArray()
            : series;

        await ExecuteWriteAsync(async (con, token) =>
        {
            await using var tx = await con.BeginTransactionAsync(token).ConfigureAwait(false);
            // Series first: a point is only reachable through its series row, so writing the
            // point first would expose an unjoinable sample to a concurrent reader.
            await InsertRowsBatchedAsync(con, tx, indexedSeries, MetricSeriesRow.AddParameters,
                MetricSeriesRow.BuildMultiRowInsertSql, MaxMetricSeriesPerBatch, token);
            await InsertRowsBatchedAsync(con, tx, observations, MetricObservationRow.AddParameters,
                MetricObservationRow.BuildMultiRowInsertSql, MaxMetricPointsPerBatch, token);
            // An export's first observation of a timestamp is the one that arrived first.
            foreach (var arriving in observations.GroupBy(static row =>
                         new MetricObservationKey(row.ProjectId, row.SeriesId, row.TimeUnixNano)))
            {
                token.ThrowIfCancellationRequested();
                RefreshMetricProjection(con, tx, arriving.Key, arriving.First());
            }

            RecordIngestion(con, tx, "metrics",
                indexedSeries.Select(static row => new ArrivingResource(row.ProjectId, row.ServiceName,
                    row.ResourceJson, row.AttributesJson, row.ScopeAttributesJson, row.AttributeRenames)), token);
            await tx.CommitAsync(token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public Task<MetricRetentionResult> DeleteExpiredMetricsBatchAsync(
        ulong cutoffUnixNano,
        int batchSize,
        CancellationToken ct = default)
    {
        if (batchSize < 1)
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be positive.");

        return ExecuteMaintenanceWriteAsync(async (con, token) =>
        {
            await using var transaction = await con.BeginTransactionAsync(token).ConfigureAwait(false);

            var deletedPoints = 0;
            var affected = new HashSet<MetricObservationKey>();
            await using (var command = con.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                                      DELETE FROM metric_observations
                                      WHERE (project_id, series_id, observation_id) IN (
                                          SELECT project_id, series_id, observation_id
                                          FROM metric_observations
                                          WHERE time_unix_nano < $1
                                          ORDER BY time_unix_nano, project_id, series_id, observation_id
                                          LIMIT $2
                                      )
                                      RETURNING project_id, series_id, time_unix_nano
                                      """;
                command.Parameters.Add(new DuckDBParameter { Value = (decimal)cutoffUnixNano });
                command.Parameters.Add(new DuckDBParameter { Value = batchSize });
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    deletedPoints++;
                    affected.Add(new MetricObservationKey(reader.GetString(0), reader.GetString(1),
                        DuckDbValueReader.Read<ulong>(reader, 2, 0)));
                }
            }

            foreach (var key in affected) RefreshMetricProjection(con, transaction, key);

            await using (var command = con.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    "DELETE FROM metric_exemplars e WHERE NOT EXISTS (SELECT 1 FROM metric_points p WHERE p.project_id = e.project_id AND p.series_id = e.series_id AND p.time_unix_nano = e.time_unix_nano)";
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            // A series with no points left is unreachable: nothing can query it and it would
            // otherwise keep advertising a metric name that has no data behind it.
            var deletedSeries = 0;
            await using (var command = con.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                                      DELETE FROM metric_series
                                      WHERE NOT EXISTS (
                                          SELECT 1 FROM metric_observations AS p
                                          WHERE p.project_id = metric_series.project_id
                                            AND p.series_id = metric_series.series_id
                                      )
                                      RETURNING series_id
                                      """;
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                    deletedSeries++;
            }

            await transaction.CommitAsync(token).ConfigureAwait(false);
            return new MetricRetentionResult(deletedPoints, deletedSeries);
        }, ct);
    }

    private static bool BackfillMetricInstrumentIds(DuckDBConnection connection)
    {
        var changed = false;
        while (true)
        {
            using var transaction = connection.BeginTransaction();
            var rows = new List<MetricSeriesRow>();
            using (var read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText = "SELECT " + MetricSeriesRow.SelectColumnList +
                                   " FROM metric_series WHERE instrument_id IS NULL LIMIT 1000";
                using var reader = read.ExecuteReader();
                while (reader.Read()) rows.Add(MetricSeriesRow.MapFromReader(reader));
            }

            foreach (var previous in rows)
            {
                changed = true;
                var indexed = previous with { InstrumentId = MetricStorageMapper.InstrumentId(previous) };
                var migrated = indexed with { SeriesId = MetricStorageMapper.SeriesId(indexed) };
                if (migrated.SeriesId == previous.SeriesId)
                {
                    using var update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText =
                        "UPDATE metric_series SET instrument_id = $1 WHERE project_id = $2 AND series_id = $3";
                    AddParameters(update, migrated.InstrumentId, previous.ProjectId, previous.SeriesId);
                    update.ExecuteNonQuery();
                    continue;
                }

                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "SELECT " + MetricSeriesRow.SelectColumnList +
                                      " FROM metric_series WHERE project_id = $1 AND series_id = $2";
                AddParameters(command, migrated.ProjectId, migrated.SeriesId);
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        var existing = MetricSeriesRow.MapFromReader(reader);
                        existing = existing with
                        {
                            InstrumentId = existing.InstrumentId ?? MetricStorageMapper.InstrumentId(existing)
                        };
                        // Only description, observation ranges and dropped counts are non-identifying.
                        var comparable = migrated with
                        {
                            Description = existing.Description,
                            FirstSeenUnixNano = existing.FirstSeenUnixNano,
                            LastSeenUnixNano = existing.LastSeenUnixNano,
                            ScopeDroppedAttributesCount = existing.ScopeDroppedAttributesCount,
                            ResourceDroppedAttributesCount = existing.ResourceDroppedAttributesCount
                        };
                        if (comparable != existing)
                            throw new InvalidDataException(
                                $"Metric identity collision while migrating '{previous.SeriesId}'.");
                    }
                }

                command.Parameters.Clear();
                command.CommandText = MetricSeriesRow.BuildMultiRowInsertSql(1);
                MetricSeriesRow.AddParameters(command, migrated);
                command.ExecuteNonQuery();

                // Canonical observations survive ID changes, including contradictory historical
                // measurements. The derived query tables are rebuilt after this migration.
                MoveMetricRows("metric_observations", MetricObservationRow.SelectColumnList);
                command.Parameters.Clear();
                command.CommandText = "DELETE FROM metric_series WHERE project_id = $1 AND series_id = $2";
                AddParameters(command, previous.ProjectId, previous.SeriesId);
                command.ExecuteNonQuery();

                void MoveMetricRows(string table, string columns)
                {
                    var projection = string.Join(", ",
                        columns.Split(',').Select(static column => column.Trim().Trim('"'))
                            .Select(static column => column == "series_id" ? "$1" : column));
                    command.Parameters.Clear();
                    command.CommandText = "INSERT INTO " + table + " (" + columns + ") SELECT " + projection +
                                          " FROM " + table + " " +
                                          "WHERE project_id = $2 AND series_id = $3 ON CONFLICT DO NOTHING";
                    AddParameters(command, migrated.SeriesId, previous.ProjectId, previous.SeriesId);
                    command.ExecuteNonQuery();
                    command.Parameters.Clear();
                    command.CommandText = "DELETE FROM " + table + " WHERE project_id = $1 AND series_id = $2";
                    AddParameters(command, previous.ProjectId, previous.SeriesId);
                    command.ExecuteNonQuery();
                }
            }

            transaction.Commit();
            if (rows.Count is 0) return changed;
        }
    }
}
