namespace Nami.Collector.Storage;

internal sealed class NamiStoreUnavailableException(string message) : InvalidOperationException(message);

internal sealed class NamiSchemaMismatchException(string message) : InvalidOperationException(message);

internal readonly record struct TracePageCursor(ulong ActivityUnixNano, string TraceId);

/// <summary>
/// One page of traces, newest activity first. Every filter is a trace-level predicate: a trace is
/// listed whole or not at all, so a filter never returns a partial trace.
/// </summary>
internal sealed record TracePageQuery
{
    public required string ProjectId { get; init; }
    public required int Limit { get; init; }
    public TracePageCursor? Cursor { get; init; }

    /// <summary>Earliest trace start (the minimum span start), inclusive.</summary>
    public ulong? StartUnixNano { get; init; }

    /// <summary>Trace start bound, exclusive: only traces that started before it.</summary>
    public ulong? EndUnixNano { get; init; }

    /// <summary>Only traces with at least one span from this service.</summary>
    public string? ServiceName { get; init; }

    /// <summary>Only traces with at least one span whose status is Error.</summary>
    public bool ErrorsOnly { get; init; }

    /// <summary>Only the traces a session touched, under the session identity COALESCE(session_id, trace_id).</summary>
    public string? SessionId { get; init; }
}

internal readonly record struct SpanPageCursor(ulong StartTimeUnixNano, string SpanId);

internal readonly record struct SessionPageCursor(ulong LastActivityUnixNano, string SessionId);

internal readonly record struct LogPageCursor(ulong? TimeUnixNano, string LogId);

internal readonly record struct MetricSeriesPageCursor(ulong LastSeenUnixNano, string SeriesId);
internal readonly record struct MetricCatalogCursor(string MetricName, string InstrumentId);

internal sealed record TraceStoragePage(
    IReadOnlyList<TraceStoragePageItem> Items,
    bool HasMore);

internal sealed record TraceStoragePageItem(
    string TraceId,
    ulong ActivityUnixNano,
    IReadOnlyList<SpanStorageRow> Spans);

internal readonly record struct StorageFileMetrics(
    long DatabaseFileSizeBytes,
    long WalFileSizeBytes,
    long ManagedStorageBytes,
    long StorageFreeBytes);

internal interface INamiStore : IAsyncDisposable
{
    Task<AnalysisSnapshot> ReadAnalysisSnapshotAsync(string projectId, AnalysisReadQuery? query = null, CancellationToken ct = default);
    Task<IReadOnlyList<AnalysisStateRow>> ReadAnalysisStateAsync(string projectId, IReadOnlyList<string> keys, CancellationToken ct = default);
    Task<ResourceWriteOutcome<AnalysisStateRow>> WriteAnalysisStateAsync(AnalysisStateRow row, string? expectedEtag, CancellationToken ct = default);

    ValueTask EnqueueAsync(SpanBatch batch, CancellationToken ct = default);

    Task InsertLogsAsync(IReadOnlyList<LogStorageRow> logs, CancellationToken ct = default);

    Task<int> DeleteExpiredLogsBatchAsync(
        ulong cutoffUnixNano,
        int batchSize,
        CancellationToken ct = default);

    Task InsertMetricsAsync(
        IReadOnlyList<MetricSeriesRow> series,
        IReadOnlyList<MetricObservationRow> observations,
        CancellationToken ct = default);

    /// <summary>Metric names recorded for a project, with the shape shared by their series.</summary>
    Task<IReadOnlyList<MetricCatalogEntry>> ListMetricsAsync(
        string projectId,
        string? namePrefix = null,
        int limit = 200,
        MetricCatalogCursor? cursor = null,
        IReadOnlyList<MetricAttributeMatcher>? matchers = null,
        string? seriesId = null,
        CancellationToken ct = default);

    /// <summary>The distinct attribute streams recorded under one metric name.</summary>
    Task<IReadOnlyList<MetricSeriesRow>> ListMetricSeriesAsync(
        string projectId,
        string metricName,
        IReadOnlyList<MetricAttributeMatcher> matchers,
        int limit = 200,
        MetricSeriesPageCursor? cursor = null,
        string? seriesId = null,
        CancellationToken ct = default);

    /// <summary>Aggregates one metric into time buckets server-side; never returns raw points.</summary>
    Task<IReadOnlyList<MetricQuerySeries>> QueryMetricAsync(
        MetricRangeQuery request,
        CancellationToken ct = default);

    Task<IReadOnlyList<MetricObservationPageItem>> ListMetricPointsAsync(
        MetricPointQuery query, CancellationToken ct = default);

    Task<MetricRetentionResult> DeleteExpiredMetricsBatchAsync(
        ulong cutoffUnixNano,
        int batchSize,
        CancellationToken ct = default);

    Task<int> DeleteExpiredSpansBatchAsync(
        ulong cutoffUnixNano,
        int batchSize,
        CancellationToken ct = default);

    Task CheckpointAsync(CancellationToken ct = default);

    StorageFileMetrics GetStorageFileMetrics();

    Task<IReadOnlyList<SessionQueryRow>> GetSessionsAsync(
        string projectId,
        ulong activeCutoffUnixNano,
        int limit = 100,
        SessionPageCursor? cursor = null,
        bool? isActive = null,
        DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null,
        CancellationToken ct = default);

    Task<SessionQueryRow?> GetSessionAsync(
        string sessionId,
        string projectId,
        CancellationToken ct = default);

    Task<SessionStatsRow> GetSessionStatsAsync(
        string projectId,
        ulong activeCutoffUnixNano,
        DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<SpanStorageRow>> GetTraceAsync(
        string traceId,
        string projectId,
        CancellationToken ct = default);

    Task<TraceStoragePage> GetTracePageAsync(
        TracePageQuery query,
        CancellationToken ct = default);

    /// <summary>
    /// One trace's spans in start order after the cursor; ask for one extra row to learn whether
    /// more follow. A null limit returns every remaining span.
    /// </summary>
    Task<IReadOnlyList<SpanStorageRow>> GetTraceSpanPageAsync(
        string traceId,
        string projectId,
        SpanPageCursor? cursor,
        int? limit,
        CancellationToken ct = default);

    Task<StorageStats> GetStorageStatsAsync(string projectId, CancellationToken ct = default);

    Task<IReadOnlyList<LogStorageRow>> GetLogsAsync(
        string projectId,
        string? sessionId = null,
        string? traceId = null,
        string? severityText = null,
        int? minSeverity = null,
        string? search = null,
        ulong? start = null,
        ulong? before = null,
        string? serviceName = null,
        int limit = 500,
        LogPageCursor? cursor = null,
        CancellationToken ct = default);

    Task<bool> TraceExistsAsync(string projectId, string traceId, CancellationToken ct = default);

    Task<bool> SpanExistsAsync(string projectId, string traceId, string spanId, CancellationToken ct = default);

    /// <summary>The unexpired retry key of a creating POST, naming the record it created, if any.</summary>
    Task<IdempotencyKeyRow?> GetIdempotencyKeyAsync(
        string projectId,
        string operation,
        string idempotencyKey,
        ulong notBeforeUnixNano,
        CancellationToken ct = default);

    Task<AnnotationRow?> GetAnnotationAsync(string projectId, string annotationId, CancellationToken ct = default);

    Task<IReadOnlyList<AnnotationRow>> ListAnnotationsAsync(AnnotationListQuery query, CancellationToken ct = default);

    Task<ResourceWriteOutcome<AnnotationRow>> CreateAnnotationAsync(
        AnnotationRow row,
        IdempotencyKeyRow? idempotency,
        ulong idempotencyNotBeforeUnixNano,
        AnalysisStateRow? metadata = null, CancellationToken ct = default);

    Task<ResourceWriteOutcome<AnnotationRow>> UpdateAnnotationAsync(
        string projectId,
        string annotationId,
        EntityTagCondition ifMatch,
        Func<AnnotationRow, AnnotationRow> update,
        AnalysisStateRow? metadata = null, CancellationToken ct = default);

    Task<ResourceWriteOutcome<AnnotationRow>> DeleteAnnotationAsync(
        string projectId,
        string annotationId,
        EntityTagCondition ifMatch,
        CancellationToken ct = default);

    Task<SavedQueryRow?> GetSavedQueryAsync(string projectId, string savedQueryId, CancellationToken ct = default);

    /// <summary>Saved queries by name; ask for one extra row to learn whether more follow.</summary>
    Task<IReadOnlyList<SavedQueryRow>> ListSavedQueriesAsync(
        string projectId,
        string? signal,
        string? afterName,
        int limit,
        CancellationToken ct = default);

    Task<ResourceWriteOutcome<SavedQueryRow>> CreateSavedQueryAsync(
        SavedQueryRow row,
        IdempotencyKeyRow? idempotency,
        ulong idempotencyNotBeforeUnixNano,
        CancellationToken ct = default);

    Task<ResourceWriteOutcome<SavedQueryRow>> UpdateSavedQueryAsync(
        string projectId,
        string savedQueryId,
        EntityTagCondition ifMatch,
        Func<SavedQueryRow, SavedQueryRow> update,
        CancellationToken ct = default);

    Task<ResourceWriteOutcome<SavedQueryRow>> DeleteSavedQueryAsync(
        string projectId,
        string savedQueryId,
        EntityTagCondition ifMatch,
        CancellationToken ct = default);

    /// <summary>Annotations last changed before the cutoff expire with the telemetry they point at.</summary>
    Task<int> DeleteExpiredAnnotationsBatchAsync(
        ulong cutoffUnixNano,
        int batchSize,
        CancellationToken ct = default);

    Task<int> DeleteExpiredIdempotencyKeysAsync(
        ulong cutoffUnixNano,
        CancellationToken ct = default);

    Task<IReadOnlyList<LogStorageRow>> GetLogStreamPageAsync(
        string projectId,
        string? serviceName = null,
        int? minSeverity = null,
        string? search = null,
        long? afterIngestSequence = null,
        int limit = 250,
        CancellationToken ct = default);
}
