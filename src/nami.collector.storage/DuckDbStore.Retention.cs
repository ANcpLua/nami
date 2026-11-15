using DuckDB.NET.Data;

namespace Nami.Collector.Storage;

internal sealed partial class DuckDbStore
{
    private const int RetentionBatchSize = 1000;
    private static readonly TimeSpan s_shutdownCheckpointTimeout = TimeSpan.FromSeconds(30);

    public async Task<(long Logs, long Spans, long MetricPoints, long MetricSeries)> DeleteExpiredAsync(
        ulong cutoffUnixNano, CancellationToken ct = default)
    {
        long logs = 0, spans = 0, metricPoints = 0, metricSeries = 0, metadata = 0;
        try
        {
            // Logs first: a span stays while a log of its trace remains.
            while (true)
            {
                var deleted = await DeleteExpiredLogsBatchAsync(cutoffUnixNano, RetentionBatchSize, ct)
                    .ConfigureAwait(false);
                logs += deleted;
                if (deleted < RetentionBatchSize)
                    break;
            }

            // Which expired spans go does not depend on the ones already gone, so a short pass is the last one.
            while (true)
            {
                var deleted = await DeleteExpiredSpansBatchAsync(cutoffUnixNano, RetentionBatchSize, ct)
                    .ConfigureAwait(false);
                spans += deleted;
                if (deleted < RetentionBatchSize)
                    break;
            }

            while (true)
            {
                var deleted = await DeleteExpiredMetricsBatchAsync(cutoffUnixNano, RetentionBatchSize, ct)
                    .ConfigureAwait(false);
                metricPoints += deleted.Points;
                metricSeries += deleted.Series;
                if (deleted.Points < RetentionBatchSize)
                    break;
            }

            metadata = await DeleteExpiredMetadataAsync(cutoffUnixNano, ct).ConfigureAwait(false);
        }
        finally
        {
            if (logs + spans + metricPoints + metricSeries + metadata > 0)
                await CheckpointAfterDeletionAsync(ct).ConfigureAwait(false);
        }

        return (logs, spans, metricPoints, metricSeries);
    }

    private async Task CheckpointAfterDeletionAsync(CancellationToken ct)
    {
        if (!ct.IsCancellationRequested)
        {
            await CheckpointAsync(ct).ConfigureAwait(false);
            return;
        }

        // Shutdown cancelled the sweep after it removed rows; reclaim their space within a bounded time.
        using var timeout = new CancellationTokenSource(s_shutdownCheckpointTimeout, _timeProvider);
        await CheckpointAsync(timeout.Token).ConfigureAwait(false);
    }

    public Task<int> DeleteExpiredLogsBatchAsync(
        ulong cutoffUnixNano,
        int batchSize,
        CancellationToken ct = default)
    {
        if (batchSize < 1)
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be positive.");
        return ExecuteMaintenanceWriteAsync(
            (con, token) => DeleteExpiredLogsBatchInternalAsync(con, cutoffUnixNano, batchSize, token),
            ct);
    }

    public Task<int> DeleteExpiredSpansBatchAsync(
        ulong cutoffUnixNano,
        int batchSize,
        CancellationToken ct = default)
    {
        if (batchSize < 1)
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be positive.");
        return ExecuteMaintenanceWriteAsync(
            (con, token) => DeleteExpiredSpansBatchInternalAsync(con, cutoffUnixNano, batchSize, token),
            ct);
    }

    public Task<long> DeleteExpiredMetadataAsync(ulong cutoffUnixNano, CancellationToken ct = default) =>
        ExecuteMaintenanceWriteAsync(async (con, token) =>
        {
            await using var transaction = await con.BeginTransactionAsync(token).ConfigureAwait(false);
            await using var command = con.CreateCommand();
            command.Transaction = transaction;
            // Keep one pre-window resource per service: changes in the retained window need their predecessor.
            command.CommandText = """
                                  DELETE FROM resource_observations
                                  WHERE observed_unix_nano < $1 AND sequence_number < (
                                    SELECT max(previous.sequence_number) FROM resource_observations AS previous
                                    WHERE previous.project_id = resource_observations.project_id
                                      AND previous.service_name = resource_observations.service_name
                                      AND previous.observed_unix_nano < $1)
                                  """;
            AddParameters(command, (decimal)cutoffUnixNano);
            long deleted = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            command.Parameters.Clear();
            command.CommandText = "DELETE FROM read_snapshots WHERE expires_unix_nano <= $1";
            AddParameters(command, (decimal)NamiTimeConversions.ToUnixNanoUnsigned(_timeProvider.GetUtcNow()));
            deleted += await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return deleted;
        }, ct);

    public Task CheckpointAsync(CancellationToken ct = default) =>
        ExecuteMaintenanceWriteAsync(static async (con, token) =>
        {
            await using var command = con.CreateCommand();
            command.CommandText = "CHECKPOINT";
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return 0;
        }, ct);

    public StorageFileMetrics GetStorageFileMetrics()
    {
        ThrowIfDisposed();
        if (_isInMemory)
            return new StorageFileMetrics(0, 0, 0, long.MaxValue);

        var databaseBytes = File.Exists(_databasePath) ? new FileInfo(_databasePath).Length : 0;
        var walPath = $"{_databasePath}.wal";
        var walBytes = File.Exists(walPath) ? new FileInfo(walPath).Length : 0;
        var managedBytes = checked(databaseBytes + walBytes);
        var databaseDirectory = Path.GetDirectoryName(_databasePath)!;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var drive = DriveInfo.GetDrives()
            .Where(static candidate => candidate.IsReady)
            .Where(candidate => IsWithinDrive(databaseDirectory, candidate.RootDirectory.FullName, comparison))
            .OrderByDescending(static candidate => candidate.RootDirectory.FullName.Length)
            .First();

        return new StorageFileMetrics(
            databaseBytes,
            walBytes,
            managedBytes,
            drive.AvailableFreeSpace);
    }

    private static async ValueTask<int> DeleteExpiredLogsBatchInternalAsync(
        DuckDBConnection con,
        ulong cutoffUnixNano,
        int batchSize,
        CancellationToken ct)
    {
        await using var transaction = await con.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var command = con.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                              DELETE FROM logs
                              WHERE (project_id, log_id) IN (
                                  SELECT project_id, log_id
                                  FROM logs
                                  WHERE COALESCE(NULLIF(time_unix_nano, 0), NULLIF(observed_time_unix_nano, 0)) < $1
                                  ORDER BY COALESCE(NULLIF(time_unix_nano, 0), NULLIF(observed_time_unix_nano, 0)), project_id, log_id
                                  LIMIT $2
                              )
                              RETURNING log_id
                              """;
        command.Parameters.Add(new DuckDBParameter { Value = (decimal)cutoffUnixNano });
        command.Parameters.Add(new DuckDBParameter { Value = batchSize });

        var deleted = 0;
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                deleted++;
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return deleted;
    }

    /// <summary>
    ///     An expired span stays only while something holds it: a log of its trace, a log without a trace id that
    ///     names it, or a span below it that stays, because it has not expired or a log names it. The spans held
    ///     from below are collected by walking up from those spans, parent by parent; UNION ends the walk at a
    ///     span it has already seen. A span that names itself as its parent, and spans that name each other in a
    ///     circle, hold nothing by that alone and go with the rest of their expired trace.
    /// </summary>
    private static async ValueTask<int> DeleteExpiredSpansBatchInternalAsync(
        DuckDBConnection con,
        ulong cutoffUnixNano,
        int batchSize,
        CancellationToken ct)
    {
        await using var transaction = await con.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var command = con.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                              DELETE FROM spans
                              WHERE (project_id, trace_id, span_id) IN (
                                  WITH RECURSIVE held(project_id, trace_id, span_id) AS (
                                      SELECT parent.project_id, parent.trace_id, parent.span_id
                                      FROM spans AS parent
                                      JOIN spans AS child
                                        ON child.project_id = parent.project_id
                                       AND child.trace_id = parent.trace_id
                                       AND child.parent_span_id = parent.span_id
                                      WHERE parent.end_time_unix_nano < $1
                                        AND (
                                            child.end_time_unix_nano >= $1
                                            OR EXISTS (
                                                SELECT 1
                                                FROM logs AS child_log
                                                WHERE child_log.project_id = child.project_id
                                                  AND child_log.trace_id IS NULL
                                                  AND child_log.span_id = child.span_id
                                            )
                                        )
                                      UNION
                                      SELECT parent.project_id, parent.trace_id, parent.span_id
                                      FROM held
                                      JOIN spans AS child
                                        ON child.project_id = held.project_id
                                       AND child.trace_id = held.trace_id
                                       AND child.span_id = held.span_id
                                      JOIN spans AS parent
                                        ON parent.project_id = child.project_id
                                       AND parent.trace_id = child.trace_id
                                       AND parent.span_id = child.parent_span_id
                                      WHERE parent.end_time_unix_nano < $1
                                  )
                                  SELECT candidate.project_id, candidate.trace_id, candidate.span_id
                                  FROM spans AS candidate
                                  WHERE candidate.end_time_unix_nano < $1
                                    AND NOT EXISTS (
                                        SELECT 1
                                        FROM held
                                        WHERE held.project_id = candidate.project_id
                                          AND held.trace_id = candidate.trace_id
                                          AND held.span_id = candidate.span_id
                                    )
                                    AND NOT EXISTS (
                                        SELECT 1
                                        FROM logs AS child_log
                                        WHERE child_log.project_id = candidate.project_id
                                          AND (
                                              child_log.trace_id = candidate.trace_id
                                              OR (child_log.trace_id IS NULL AND child_log.span_id = candidate.span_id)
                                          )
                                    )
                                  ORDER BY candidate.end_time_unix_nano, candidate.project_id,
                                           candidate.trace_id, candidate.span_id
                                  LIMIT $2
                              )
                              RETURNING span_id
                              """;
        command.Parameters.Add(new DuckDBParameter { Value = (decimal)cutoffUnixNano });
        command.Parameters.Add(new DuckDBParameter { Value = batchSize });

        var deleted = 0;
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                deleted++;
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return deleted;
    }

    private static bool IsWithinDrive(string path, string root, StringComparison comparison)
    {
        if (path.Equals(Path.TrimEndingDirectorySeparator(root), comparison))
            return true;

        var rootWithSeparator = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;
        return path.StartsWith(rootWithSeparator, comparison);
    }
}
