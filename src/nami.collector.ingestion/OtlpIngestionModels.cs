using Nami.Collector.Ingestion.Primitives;

namespace Nami.Collector.Ingestion;

internal sealed record TraceIngestionBatch(IReadOnlyList<SpanIngestionRecord> Spans)
{
    public long RejectedSpans { get; init; }
    public string? RejectionMessage { get; init; }
}

internal sealed record LogIngestionBatch(IReadOnlyList<LogIngestionRecord> Logs)
{
    public long RejectedLogRecords { get; init; }
    public string? RejectionMessage { get; init; }
}

/// <summary>
/// The converted points plus the OTLP partial-success accounting for what was dropped.
/// A rejected point must not fail the whole export: valid points in the same request
/// remain available and the sender receives an explicit rejection count and reason.
/// </summary>
internal sealed record MetricIngestionBatch(
    IReadOnlyList<MetricPointIngestionRecord> Points,
    long RejectedDataPoints,
    string? RejectionMessage);

internal sealed record ResourceEntityRefIngestionRecord(
    string? SchemaUrl,
    string Type,
    IReadOnlyList<string> IdKeys,
    IReadOnlyList<string> DescriptionKeys);

internal sealed record SpanIngestionRecord
{
    public string? ProjectIdHint { get; init; }
    public required string SpanId { get; init; }
    public required string TraceId { get; init; }
    public string? ParentSpanId { get; init; }
    public required string Name { get; init; }
    public int? Kind { get; init; }
    public required ulong StartTimeUnixNano { get; init; }
    public required ulong EndTimeUnixNano { get; init; }
    public int? StatusCode { get; init; }
    public required string ServiceName { get; init; }
    public required IReadOnlyDictionary<string, OtlpAttributeValue> Attributes { get; init; }
    public required IReadOnlyDictionary<string, OtlpAttributeValue> ResourceAttributes { get; init; }
    public IReadOnlyList<ResourceEntityRefIngestionRecord> ResourceEntityRefs { get; init; } = [];
    /// <summary>The scope's schema URL, or the resource's when the scope carries none; null when neither does.</summary>
    public string? SchemaUrl { get; init; }
    public string? ScopeName { get; init; }
    public string? ScopeVersion { get; init; }
    public string? ScopeSchemaUrl { get; init; }
    public IReadOnlyDictionary<string, OtlpAttributeValue> ScopeAttributes { get; init; } = FrozenDictionary<string, OtlpAttributeValue>.Empty;
    public uint ScopeDroppedAttributesCount { get; init; }
    public string? ResourceSchemaUrl { get; init; }
    public uint ResourceDroppedAttributesCount { get; init; }
    public string? TraceState { get; init; }
    public uint Flags { get; init; }
    public uint DroppedAttributesCount { get; init; }
    public uint DroppedEventsCount { get; init; }
    public uint DroppedLinksCount { get; init; }
    public string? StatusMessage { get; init; }
    public IReadOnlyList<SpanEventIngest> Events { get; init; } = [];
    public IReadOnlyList<SpanLinkIngest> Links { get; init; } = [];
}

internal sealed record SpanEventIngest(
    string Name,
    ulong TimeUnixNano,
    IReadOnlyDictionary<string, OtlpAttributeValue> Attributes,
    uint DroppedAttributesCount = 0);

internal sealed record SpanLinkIngest(
    string TraceId,
    string SpanId,
    IReadOnlyDictionary<string, OtlpAttributeValue> Attributes,
    string? TraceState = null,
    uint Flags = 0,
    uint DroppedAttributesCount = 0);

internal sealed record LogIngestionRecord
{
    public string? ScopeName { get; init; }
    public string? ScopeVersion { get; init; }
    public string? ScopeSchemaUrl { get; init; }
    public IReadOnlyDictionary<string, OtlpAttributeValue> ScopeAttributes { get; init; } = FrozenDictionary<string, OtlpAttributeValue>.Empty;
    public uint ScopeDroppedAttributesCount { get; init; }
    public string? ResourceSchemaUrl { get; init; }
    public uint ResourceDroppedAttributesCount { get; init; }
    public uint Flags { get; init; }
    public uint DroppedAttributesCount { get; init; }
    public string? ProjectIdHint { get; init; }
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
    public string? EventName { get; init; }
    public ulong? TimeUnixNano { get; init; }
    public ulong? ObservedTimeUnixNano { get; init; }
    public int? UntimedRequestOrdinal { get; init; }
    public bool ObservedTimeProvided { get; init; } = true;
    /// <summary>When the Collector received the record.</summary>
    public ulong? ReceivedTimeUnixNano { get; init; }
    public required int SeverityNumber { get; init; }
    public string? SeverityText { get; init; }
    public required OtlpAttributeValue Body { get; init; }
    public required string ServiceName { get; init; }
    public required IReadOnlyDictionary<string, OtlpAttributeValue> Attributes { get; init; }
    public required IReadOnlyDictionary<string, OtlpAttributeValue> ResourceAttributes { get; init; }
    public IReadOnlyList<ResourceEntityRefIngestionRecord> ResourceEntityRefs { get; init; } = [];
}

/// <summary>
/// One OTLP data point carrying the stream metadata it was nested under. Flattening here
/// keeps the converter a pure projection of the wire shape; the storage mapper folds the
/// repeated metadata back into one series row per distinct stream.
/// </summary>
internal sealed record MetricPointIngestionRecord
{
    public string? ResourceIdentity { get; init; }
    public string? ScopeIdentity { get; init; }
    public string? AttributesIdentity { get; init; }
    public string? ScopeName { get; init; }
    public string? ScopeVersion { get; init; }
    public string? ScopeSchemaUrl { get; init; }
    public IReadOnlyDictionary<string, OtlpAttributeValue> ScopeAttributes { get; init; } = FrozenDictionary<string, OtlpAttributeValue>.Empty;
    public uint ScopeDroppedAttributesCount { get; init; }
    public string? ResourceSchemaUrl { get; init; }
    public uint ResourceDroppedAttributesCount { get; init; }
    public string? ProjectIdHint { get; init; }
    public required string MetricName { get; init; }
    public required MetricKind Kind { get; init; }
    public required MetricTemporality Temporality { get; init; }
    public required bool IsMonotonic { get; init; }
    public string? Unit { get; init; }
    public string? Description { get; init; }
    public required string ServiceName { get; init; }
    public string? SchemaUrl { get; init; }
    public required IReadOnlyDictionary<string, OtlpAttributeValue> Attributes { get; init; }
    public required IReadOnlyDictionary<string, OtlpAttributeValue> ResourceAttributes { get; init; }
    public IReadOnlyList<ResourceEntityRefIngestionRecord> ResourceEntityRefs { get; init; } = [];

    public required ulong TimeUnixNano { get; init; }
    public ulong? StartTimeUnixNano { get; init; }

    public required Api.Contracts.OTel.Metrics.MetricPointData Data { get; init; }
    public uint Flags { get; init; }
    public IReadOnlyList<Api.Contracts.OTel.Metrics.MetricExemplar> Exemplars { get; init; } = [];
    public IReadOnlyList<Nami.Api.Contracts.Common.Attribute> InstrumentMetadata { get; init; } = [];
}
