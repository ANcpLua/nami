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

        await ExecuteWriteAsync(async (con, token) =>
        {
            await using var tx = await con.BeginTransactionAsync(token).ConfigureAwait(false);
            // Series first: a point is only reachable through its series row, so writing the
            // point first would expose an unjoinable sample to a concurrent reader.
            await InsertRowsBatchedAsync(con, tx, series, MetricSeriesRow.AddParameters,
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
                series.Select(static row => new ArrivingResource(row.ProjectId, row.ServiceName,
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

}
