using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Nami.Collector.Storage;
using Nami.Retention;

namespace Nami.Health;

internal sealed class DuckDbHealthCheck(INamiStore store, IOptions<RetentionOptions> retentionOptions) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // The read below runs on the reader pool and still succeeds when the writer has stopped; every write
            // is then refused. The writer's state is asked for itself.
            if (store.WriterFailure is { } writerFailure)
            {
                return HealthCheckResult.Unhealthy(
                    "DuckDB writer stopped after a failure; the store accepts no writes",
                    writerFailure);
            }

            var stats = await store.GetStorageStatsAsync(ProjectScope.DefaultProjectId, cancellationToken)
                .ConfigureAwait(false);
            var fileMetrics = store.GetStorageFileMetrics();
            var data = new Dictionary<string, object>
            {
                ["span_count"] = stats.SpanCount,
                ["session_count"] = stats.SessionCount,
                ["database_file_size_bytes"] = fileMetrics.DatabaseFileSizeBytes,
                ["wal_file_size_bytes"] = fileMetrics.WalFileSizeBytes,
                ["managed_storage_bytes"] = fileMetrics.ManagedStorageBytes,
                ["storage_free_bytes"] = fileMetrics.StorageFreeBytes
            };

            if (fileMetrics.StorageFreeBytes < retentionOptions.Value.StorageMinimumFreeBytes)
            {
                return HealthCheckResult.Degraded(
                    $"DuckDB storage free space is below the configured minimum; " +
                    $"managed_storage_bytes={fileMetrics.ManagedStorageBytes}; " +
                    $"storage_free_bytes={fileMetrics.StorageFreeBytes}",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                $"DuckDB connection is healthy; managed_storage_bytes={fileMetrics.ManagedStorageBytes}; " +
                $"storage_free_bytes={fileMetrics.StorageFreeBytes}",
                data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "DuckDB connection failed",
                ex);
        }
    }
}
