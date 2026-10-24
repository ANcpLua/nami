namespace Nami.Collector.Storage;

internal sealed record SpanBatch(IReadOnlyList<SpanStorageRow> Spans);

[DuckDbTable("spans",
    Indexes =
        "ProjectId;ProjectId,TraceId;ProjectId,SessionId;ProjectId,StartTimeUnixNano;ProjectId,TraceId,ParentSpanId;ProjectId,ServiceName;ProjectId,GenAiProviderName;ProjectId,GenAiRequestModel;ProjectId,GenAiResponseModel;ProjectId,GenAiOperationName;TraceId;SessionId;StartTimeUnixNano;EndTimeUnixNano;ServiceName;GenAiProviderName;GenAiRequestModel;GenAiResponseModel;GenAiOperationName",
    OnConflict = """
                 ON CONFLICT (project_id, trace_id, span_id) DO UPDATE SET
                     end_time_unix_nano = EXCLUDED.end_time_unix_nano,
                     duration_ns = EXCLUDED.duration_ns,
                     status_code = EXCLUDED.status_code,
                     service_name = COALESCE(EXCLUDED.service_name, service_name),
                     gen_ai_provider_name = EXCLUDED.gen_ai_provider_name,
                     gen_ai_operation_name = EXCLUDED.gen_ai_operation_name,
                     gen_ai_output_type = EXCLUDED.gen_ai_output_type,
                     gen_ai_request_model = EXCLUDED.gen_ai_request_model,
                     gen_ai_response_model = EXCLUDED.gen_ai_response_model,
                     gen_ai_input_tokens = EXCLUDED.gen_ai_input_tokens,
                     gen_ai_output_tokens = EXCLUDED.gen_ai_output_tokens,
                     gen_ai_cache_read_input_tokens = EXCLUDED.gen_ai_cache_read_input_tokens,
                     gen_ai_cache_write_input_tokens = EXCLUDED.gen_ai_cache_write_input_tokens,
                     gen_ai_reasoning_tokens = EXCLUDED.gen_ai_reasoning_tokens,
                     attributes_json = EXCLUDED.attributes_json,
                     resource_json = EXCLUDED.resource_json,
                     resource_entity_refs_json = EXCLUDED.resource_entity_refs_json,
                     schema_url = EXCLUDED.schema_url,
                     status_message = EXCLUDED.status_message,
                     events_json = EXCLUDED.events_json,
                     links_json = EXCLUDED.links_json,
                     scope_name = EXCLUDED.scope_name,
                     scope_version = EXCLUDED.scope_version,
                     scope_schema_url = EXCLUDED.scope_schema_url,
                     scope_attributes_json = EXCLUDED.scope_attributes_json,
                     scope_dropped_attributes_count = EXCLUDED.scope_dropped_attributes_count,
                     resource_schema_url = EXCLUDED.resource_schema_url,
                     resource_dropped_attributes_count = EXCLUDED.resource_dropped_attributes_count,
                     trace_state = EXCLUDED.trace_state,
                     flags = EXCLUDED.flags,
                     dropped_attributes_count = EXCLUDED.dropped_attributes_count,
                     dropped_events_count = EXCLUDED.dropped_events_count,
                     dropped_links_count = EXCLUDED.dropped_links_count
                 """)]
internal sealed partial record SpanStorageRow
{
    [JsonIgnore] internal IReadOnlyList<AttributeRename> AttributeRenames { get; init; } = [];

    [DuckDbColumn(PrimaryKeyOrdinal = 0, SqlType = "VARCHAR(128)")]
    public required string ProjectId { get; init; }

    [DuckDbColumn(PrimaryKeyOrdinal = 2)] public required string SpanId { get; init; }

    [DuckDbColumn(PrimaryKeyOrdinal = 1)] public required string TraceId { get; init; }

    public string? ParentSpanId { get; init; }
    public string? SessionId { get; init; }

    public required string Name { get; init; }
    public required byte Kind { get; init; }
    public required ulong StartTimeUnixNano { get; init; }
    public required ulong EndTimeUnixNano { get; init; }
    public required ulong DurationNs { get; init; }
    public required byte StatusCode { get; init; }

    public string? ServiceName { get; init; }

    public string? GenAiProviderName { get; init; }
    public string? GenAiOperationName { get; init; }
    public string? GenAiOutputType { get; init; }
    public string? GenAiRequestModel { get; init; }
    public string? GenAiResponseModel { get; init; }
    public long? GenAiInputTokens { get; init; }
    public long? GenAiOutputTokens { get; init; }
    public long? GenAiCacheReadInputTokens { get; init; }
    public long? GenAiCacheWriteInputTokens { get; init; }
    public long? GenAiReasoningTokens { get; init; }

    [DuckDbColumn(SqlType = "JSON")] public string? AttributesJson { get; init; }

    [DuckDbColumn(SqlType = "JSON")] public string? ResourceJson { get; init; }

    [DuckDbColumn(SqlType = "JSON")] public string? ResourceEntityRefsJson { get; init; }

    [DuckDbColumn(SqlType = "VARCHAR(256)")]
    public string? SchemaUrl { get; init; }

    [DuckDbColumn(SqlType = "VARCHAR")] public string? StatusMessage { get; init; }

    [DuckDbColumn(SqlType = "JSON")] public string? EventsJson { get; init; }

    [DuckDbColumn(SqlType = "JSON")] public string? LinksJson { get; init; }

    // Columns a release appends stay last: DuckDbStore adds them to an existing table with
    // ALTER TABLE ADD COLUMN, which only works while the stored columns are a prefix of these.
    [DuckDbColumn(SqlType = "VARCHAR(256)")]
    public string? ScopeName { get; init; }

    [DuckDbColumn(SqlType = "VARCHAR(64)")]
    public string? ScopeVersion { get; init; }

    public string? ScopeSchemaUrl { get; init; }

    [DuckDbColumn(SqlType = "JSON")] public string? ScopeAttributesJson { get; init; }

    public uint? ScopeDroppedAttributesCount { get; init; }
    public string? ResourceSchemaUrl { get; init; }
    public uint? ResourceDroppedAttributesCount { get; init; }
    public string? TraceState { get; init; }
    public uint? Flags { get; init; }
    public uint? DroppedAttributesCount { get; init; }
    public uint? DroppedEventsCount { get; init; }
    public uint? DroppedLinksCount { get; init; }
}

internal sealed record StorageStats
{
    public long SpanCount { get; init; }
    public long SessionCount { get; init; }
}

[DuckDbTable("logs",
    Indexes = "ProjectId,TimeUnixNano;ProjectId,IngestSequence;ProjectId,TraceId;ProjectId,SessionId;TimeUnixNano",
    OnConflict = """
                 ON CONFLICT (project_id, log_id) DO UPDATE SET
                     trace_id = EXCLUDED.trace_id,
                     span_id = EXCLUDED.span_id,
                     event_name = EXCLUDED.event_name,
                     session_id = EXCLUDED.session_id,
                     time_unix_nano = EXCLUDED.time_unix_nano,
                     observed_time_unix_nano = least(observed_time_unix_nano, EXCLUDED.observed_time_unix_nano),
                     severity_number = EXCLUDED.severity_number,
                     severity_text = EXCLUDED.severity_text,
                     body = EXCLUDED.body,
                     service_name = EXCLUDED.service_name,
                     attributes_json = EXCLUDED.attributes_json,
                     resource_json = EXCLUDED.resource_json,
                     resource_entity_refs_json = EXCLUDED.resource_entity_refs_json,
                     scope_name = EXCLUDED.scope_name,
                     scope_version = EXCLUDED.scope_version,
                     scope_schema_url = EXCLUDED.scope_schema_url,
                     scope_attributes_json = EXCLUDED.scope_attributes_json,
                     scope_dropped_attributes_count = EXCLUDED.scope_dropped_attributes_count,
                     resource_schema_url = EXCLUDED.resource_schema_url,
                     resource_dropped_attributes_count = EXCLUDED.resource_dropped_attributes_count,
                     flags = EXCLUDED.flags,
                     dropped_attributes_count = EXCLUDED.dropped_attributes_count,
                     received_time_unix_nano = coalesce(least(received_time_unix_nano, EXCLUDED.received_time_unix_nano), received_time_unix_nano, EXCLUDED.received_time_unix_nano)
                 """)]
internal sealed partial record LogStorageRow
{
    // Preserve raw event time while ordering absent event times by observation.
    // https://github.com/open-telemetry/opentelemetry-specification/blob/v1.61.0/specification/logs/data-model.md#field-observedtimestamp

    [JsonIgnore] internal IReadOnlyList<AttributeRename> AttributeRenames { get; init; } = [];

    [DuckDbColumn(PrimaryKeyOrdinal = 0, SqlType = "VARCHAR(128)")]
    public required string ProjectId { get; init; }

    [DuckDbColumn(PrimaryKeyOrdinal = 1)] public required string LogId { get; init; }

    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
    public string? EventName { get; init; }
    public string? SessionId { get; init; }

    public ulong? TimeUnixNano { get; init; }
    public ulong? ObservedTimeUnixNano { get; init; }

    public required byte SeverityNumber { get; init; }
    public string? SeverityText { get; init; }
    public string? Body { get; init; }

    public string? ServiceName { get; init; }

    [DuckDbColumn(SqlType = "JSON")] public string? AttributesJson { get; init; }

    [DuckDbColumn(SqlType = "JSON")] public string? ResourceJson { get; init; }

    [DuckDbColumn(SqlType = "JSON")] public string? ResourceEntityRefsJson { get; init; }

    // Monotonic collector arrival order. Event timestamps are supplied by producers and may arrive
    // late or out of order, so they cannot be used as a lossless live-stream cursor.
    [DuckDbColumn(ExcludeFromInsert = true, DefaultSql = "nextval('logs_ingest_sequence')")]
    public long IngestSequence { get; private init; }

    public string? ScopeName { get; init; }
    public string? ScopeVersion { get; init; }
    public string? ScopeSchemaUrl { get; init; }

    [DuckDbColumn(SqlType = "JSON")] public string? ScopeAttributesJson { get; init; }

    public uint? ScopeDroppedAttributesCount { get; init; }
    public string? ResourceSchemaUrl { get; init; }
    public uint? ResourceDroppedAttributesCount { get; init; }
    public uint? Flags { get; init; }

    public uint? DroppedAttributesCount { get; init; }

    // Appended after the columns earlier releases stored. When the Collector first received the
    // record; null for records stored before a release recorded it.
    public ulong? ReceivedTimeUnixNano { get; init; }

    internal ulong? EffectiveTimeUnixNano() => TimeUnixNano is > 0 ? TimeUnixNano :
        ObservedTimeUnixNano is > 0 ? ObservedTimeUnixNano : null;
}

/// <summary>A project-scoped, immutable view. Its captured clock and rows survive process restarts.</summary>
internal interface ISnapshot
{
    string Id { get; }
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
    [DuckDbColumn(PrimaryKeyOrdinal = 0)] public required string ProjectId { get; init; }

    [DuckDbColumn(PrimaryKeyOrdinal = 1)] public required string Signal { get; init; }

    [DuckDbColumn(PrimaryKeyOrdinal = 2)] public required string ServiceName { get; init; }

    public required ulong LastSeenUnixNano { get; init; }
    public required ulong WatermarkUnixNano { get; init; }
}

[DuckDbTable("resource_observations", Indexes = "ProjectId,ServiceName,ObservedUnixNano")]
internal sealed partial record ResourceObservationRow
{
    [DuckDbColumn(PrimaryKeyOrdinal = 0)] public required string ProjectId { get; init; }

    [DuckDbColumn(PrimaryKeyOrdinal = 1)] public required string ObservationId { get; init; }

    public required string ServiceName { get; init; }
    public required ulong ObservedUnixNano { get; init; }
    public required long SequenceNumber { get; init; }

    [DuckDbColumn(SqlType = "JSON")] public required string AttributesJson { get; init; }
}

[DuckDbTable("read_snapshots", Indexes = "ExpiresUnixNano")]
internal sealed partial record ReadSnapshotRow
{
    [DuckDbColumn(PrimaryKeyOrdinal = 0)] public required string ProjectId { get; init; }

    [DuckDbColumn(PrimaryKeyOrdinal = 1)] public required string SnapshotId { get; init; }

    public required ulong ExpiresUnixNano { get; init; }

    [DuckDbColumn(SqlType = "BLOB")] public required byte[] Payload { get; init; }
}

internal sealed class SnapshotUnavailableException()
    : InvalidOperationException("Snapshot is unknown, expired, or belongs to another project.");

[DuckDbTable("attribute_stability",
    OnConflict = "ON CONFLICT (project_id, attribute_key) DO UPDATE SET stability = EXCLUDED.stability")]
internal sealed partial record AttributeStabilityRow
{
    [DuckDbColumn(PrimaryKeyOrdinal = 0)] public required string ProjectId { get; init; }

    [DuckDbColumn(PrimaryKeyOrdinal = 1)] public required string AttributeKey { get; init; }

    public required string Stability { get; init; }
}

[DuckDbTable("attribute_discovery", OnConflict = """
                                                 ON CONFLICT (project_id, original_key) DO UPDATE SET
                                                   canonical_key = EXCLUDED.canonical_key,
                                                   last_seen_unix_nano = greatest(last_seen_unix_nano, EXCLUDED.last_seen_unix_nano)
                                                 """)]
internal sealed partial record AttributeDiscoveryRow
{
    [DuckDbColumn(PrimaryKeyOrdinal = 0)] public required string ProjectId { get; init; }

    [DuckDbColumn(PrimaryKeyOrdinal = 1)] public required string OriginalKey { get; init; }

    public string? CanonicalKey { get; init; }
    public required string Namespace { get; init; }
    public required ulong FirstSeenUnixNano { get; init; }
    public required ulong LastSeenUnixNano { get; init; }
}
