namespace Nami.Collector.Storage;

/// <summary>A project-scoped, immutable view. Its captured clock and rows survive process restarts.</summary>
internal interface ISnapshot
{
    string Id { get; }
    string ProjectId { get; }
    DateTimeOffset CapturedAt { get; }
    IReadOnlyList<SpanStorageRow> Spans { get; }
    IReadOnlyList<LogStorageRow> Logs { get; }
    IReadOnlyList<MetricSeriesRow> Metrics { get; }
    IReadOnlyList<SignalWatermarkRow> Signals { get; }
    IReadOnlyList<ResourceObservationRow> Resources { get; }
    IReadOnlyList<AttributeStabilityRow> AttributeStabilities { get; }
    IReadOnlyList<AttributeDiscoveryRow> AttributeDiscoveries { get; }
}

internal sealed record TelemetrySnapshot(
    string Id,
    string ProjectId,
    DateTimeOffset CapturedAt,
    IReadOnlyList<SpanStorageRow> Spans,
    IReadOnlyList<LogStorageRow> Logs,
    IReadOnlyList<MetricSeriesRow> Metrics,
    IReadOnlyList<SignalWatermarkRow> Signals,
    IReadOnlyList<ResourceObservationRow> Resources,
    IReadOnlyList<AttributeStabilityRow> AttributeStabilities,
    IReadOnlyList<AttributeDiscoveryRow> AttributeDiscoveries) : ISnapshot;

[DuckDbTable("signal_watermarks", OnConflict = """
    ON CONFLICT (project_id, signal, service_name) DO UPDATE SET
      last_seen_unix_nano = greatest(last_seen_unix_nano, EXCLUDED.last_seen_unix_nano),
      watermark_unix_nano = greatest(watermark_unix_nano, EXCLUDED.watermark_unix_nano)
    """)]
internal sealed partial record SignalWatermarkRow
{
    [DuckDbColumn(PrimaryKeyOrdinal = 0)]
    public required string ProjectId { get; init; }
    [DuckDbColumn(PrimaryKeyOrdinal = 1)]
    public required string Signal { get; init; }
    [DuckDbColumn(PrimaryKeyOrdinal = 2)]
    public required string ServiceName { get; init; }
    public required ulong LastSeenUnixNano { get; init; }
    public required ulong WatermarkUnixNano { get; init; }
}

[DuckDbTable("resource_observations", Indexes = "ProjectId,ServiceName,ObservedUnixNano")]
internal sealed partial record ResourceObservationRow
{
    [DuckDbColumn(PrimaryKeyOrdinal = 0)]
    public required string ProjectId { get; init; }
    [DuckDbColumn(PrimaryKeyOrdinal = 1)]
    public required string ObservationId { get; init; }
    public required string ServiceName { get; init; }
    public required ulong ObservedUnixNano { get; init; }
    public required long SequenceNumber { get; init; }
    [DuckDbColumn(SqlType = "JSON")]
    public required string AttributesJson { get; init; }
}

[DuckDbTable("read_snapshots", Indexes = "ExpiresUnixNano")]
internal sealed partial record ReadSnapshotRow
{
    [DuckDbColumn(PrimaryKeyOrdinal = 0)]
    public required string ProjectId { get; init; }
    [DuckDbColumn(PrimaryKeyOrdinal = 1)]
    public required string SnapshotId { get; init; }
    public required ulong ExpiresUnixNano { get; init; }
    [DuckDbColumn(SqlType = "BLOB")]
    public required byte[] Payload { get; init; }
}

internal sealed class SnapshotUnavailableException() : InvalidOperationException("Snapshot is unknown, expired, or belongs to another project.");

[DuckDbTable("attribute_stability", OnConflict = "ON CONFLICT (project_id, attribute_key) DO UPDATE SET stability = EXCLUDED.stability")]
internal sealed partial record AttributeStabilityRow
{
    [DuckDbColumn(PrimaryKeyOrdinal = 0)]
    public required string ProjectId { get; init; }
    [DuckDbColumn(PrimaryKeyOrdinal = 1)]
    public required string AttributeKey { get; init; }
    public required string Stability { get; init; }
}

[DuckDbTable("attribute_discovery", OnConflict = """
    ON CONFLICT (project_id, original_key) DO UPDATE SET
      canonical_key = EXCLUDED.canonical_key,
      last_seen_unix_nano = greatest(last_seen_unix_nano, EXCLUDED.last_seen_unix_nano)
    """)]
internal sealed partial record AttributeDiscoveryRow
{
    [DuckDbColumn(PrimaryKeyOrdinal = 0)]
    public required string ProjectId { get; init; }
    [DuckDbColumn(PrimaryKeyOrdinal = 1)]
    public required string OriginalKey { get; init; }
    public string? CanonicalKey { get; init; }
    public required string Namespace { get; init; }
    public required ulong FirstSeenUnixNano { get; init; }
    public required ulong LastSeenUnixNano { get; init; }
}
