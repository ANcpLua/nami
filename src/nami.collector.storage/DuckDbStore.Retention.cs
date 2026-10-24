using DuckDB.NET.Data;

namespace Nami.Collector.Storage;

internal sealed partial class DuckDbStore
{
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
                                  SELECT candidate.project_id, candidate.trace_id, candidate.span_id
                                  FROM spans AS candidate
                                  WHERE candidate.end_time_unix_nano < $1
                                    AND NOT EXISTS (
                                        SELECT 1
                                        FROM spans AS child
                                        WHERE child.project_id = candidate.project_id
                                          AND child.trace_id = candidate.trace_id
                                          AND child.parent_span_id = candidate.span_id
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
