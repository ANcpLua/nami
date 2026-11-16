namespace Nami.Collector.Storage;

internal sealed class NamiStoreUnavailableException(string? message, Exception? innerException)
    : InvalidOperationException(message, innerException)
{
    public NamiStoreUnavailableException(string? message) : this(message, null) { }
}

internal sealed class NamiSchemaMismatchException(string? message, Exception? innerException)
    : InvalidOperationException(message, innerException)
{
    public NamiSchemaMismatchException(string? message) : this(message, null) { }
}

internal interface INamiStore : IAsyncDisposable
{
    Task<TelemetrySnapshot> ReadSnapshotAsync(string projectId, string? snapshotId = null, CancellationToken ct = default);
    ValueTask EnqueueAsync(SpanBatch batch, CancellationToken ct = default);
    Task InsertLogsAsync(IReadOnlyList<LogStorageRow> logs, CancellationToken ct = default);

    Task InsertMetricsAsync(IReadOnlyList<MetricSeriesRow> series,
        IReadOnlyList<MetricObservationRow> observations, CancellationToken ct = default);

    /// <summary>
    ///     Removes every record older than the cutoff, in the order and the number of passes the schema needs, and
    ///     reclaims the space when anything was removed, within a bounded time if shutdown cancels the sweep.
    ///     Retention is the only path that deletes recorded telemetry.
    /// </summary>
    Task<(long Logs, long Spans, long MetricPoints, long MetricSeries)> DeleteExpiredAsync(ulong cutoffUnixNano,
        CancellationToken ct = default);

    StorageFileMetrics GetStorageFileMetrics();
    Task<StorageStats> GetStorageStatsAsync(string projectId, CancellationToken ct = default);
}
