namespace Nami.Collector.Storage;

internal sealed class NamiStoreUnavailableException(string? message, Exception? innerException)
    : InvalidOperationException(message, innerException)
{
    public NamiStoreUnavailableException() : this("The Nami store is unavailable.", null) { }
    public NamiStoreUnavailableException(string? message) : this(message, null) { }
}

internal sealed class NamiSchemaMismatchException(string? message, Exception? innerException)
    : InvalidOperationException(message, innerException)
{
    public NamiSchemaMismatchException() : this("The database schema does not match the Nami schema.", null) { }
    public NamiSchemaMismatchException(string? message) : this(message, null) { }
}

internal interface INamiStore : IAsyncDisposable
{
    Task<TelemetrySnapshot> ReadSnapshotAsync(string projectId, string? snapshotId = null, CancellationToken ct = default);
    ValueTask EnqueueAsync(SpanBatch batch, CancellationToken ct = default);
    Task InsertLogsAsync(IReadOnlyList<LogStorageRow> logs, CancellationToken ct = default);

    Task InsertMetricsAsync(IReadOnlyList<MetricSeriesRow> series,
        IReadOnlyList<MetricObservationRow> observations, CancellationToken ct = default);

    Task<int> DeleteExpiredLogsBatchAsync(ulong cutoffUnixNano, int batchSize, CancellationToken ct = default);
    Task<int> DeleteExpiredSpansBatchAsync(ulong cutoffUnixNano, int batchSize, CancellationToken ct = default);

    Task<MetricRetentionResult> DeleteExpiredMetricsBatchAsync(
        ulong cutoffUnixNano, int batchSize, CancellationToken ct = default);

    Task<long> DeleteExpiredMetadataAsync(ulong cutoffUnixNano, CancellationToken ct = default);
    Task CheckpointAsync(CancellationToken ct = default);
    StorageFileMetrics GetStorageFileMetrics();
    Task<StorageStats> GetStorageStatsAsync(string projectId, CancellationToken ct = default);
}
