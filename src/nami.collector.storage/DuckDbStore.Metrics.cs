using DuckDB.NET.Data;

namespace Nami.Collector.Storage;

internal sealed partial class DuckDbStore
{
    private static readonly StagedInsert<MetricSeriesRow> s_metricSeriesStage = new(MetricSeriesRow.StageTableName,
        MetricSeriesRow.CreateStageSql, MetricSeriesRow.StageKeyOccurrencesSql, MetricSeriesRow.MergeStageSql,
        MetricSeriesRow.MergeStageOccurrenceSql, MetricSeriesRow.DropStageSql, MetricSeriesRow.AppendStageRow,
        MetricSeriesRow.InsertSql, MetricSeriesRow.AddParameters);

    private static readonly StagedInsert<MetricObservationRow> s_metricObservationStage = new(
        MetricObservationRow.StageTableName, MetricObservationRow.CreateStageSql,
        MetricObservationRow.StageKeyOccurrencesSql, MetricObservationRow.MergeStageSql,
        MetricObservationRow.MergeStageOccurrenceSql, MetricObservationRow.DropStageSql,
        MetricObservationRow.AppendStageRow, MetricObservationRow.InsertSql, MetricObservationRow.AddParameters);

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
            using var rollbackUninterrupted = WriterInterrupt.EndBeforeTheTransactionDoes();
            // Series first: a point is only reachable through its series row, so writing the
            // point first would expose an unjoinable sample to a concurrent reader.
            InsertRowsStaged(con, tx, series, s_metricSeriesStage, token);
            InsertRowsStaged(con, tx, observations, s_metricObservationStage, token);
            // An export's first observation of a timestamp is the one that arrived first.
            var arriving = new Dictionary<MetricObservationKey, MetricObservationRow>(observations.Count);
            foreach (var row in observations)
                arriving.TryAdd(new MetricObservationKey(row.ProjectId, row.SeriesId, row.TimeUnixNano), row);
            RefreshMetricProjections(con, tx, [.. arriving.Keys], arriving, token);

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
            using var rollbackUninterrupted = WriterInterrupt.EndBeforeTheTransactionDoes();

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

            RefreshMetricProjections(con, transaction, [.. affected], null, token);

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
