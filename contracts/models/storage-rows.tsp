import "../common/values.tsp";
import "../emitters/csharp/lib/main.tsp";
using Nami.Api.Schema.Emit.CSharp;
namespace Nami.Models.Storage.Rows;

@duckDbTable(#{ name: "spans", indexes: "ProjectId;ProjectId,TraceId;ProjectId,SessionId;ProjectId,StartTimeUnixNano;ProjectId,TraceId,ParentSpanId;ProjectId,ServiceName;ProjectId,GenAiProviderName;ProjectId,GenAiRequestModel;ProjectId,GenAiResponseModel;ProjectId,GenAiOperationName;TraceId;SessionId;StartTimeUnixNano;EndTimeUnixNano;ServiceName;GenAiProviderName;GenAiRequestModel;GenAiResponseModel;GenAiOperationName", onConflict: "ON CONFLICT (project_id, trace_id, span_id) DO UPDATE SET\n    end_time_unix_nano = EXCLUDED.end_time_unix_nano,\n    duration_ns = EXCLUDED.duration_ns,\n    status_code = EXCLUDED.status_code,\n    service_name = COALESCE(EXCLUDED.service_name, service_name),\n    gen_ai_provider_name = EXCLUDED.gen_ai_provider_name,\n    gen_ai_operation_name = EXCLUDED.gen_ai_operation_name,\n    gen_ai_output_type = EXCLUDED.gen_ai_output_type,\n    gen_ai_request_model = EXCLUDED.gen_ai_request_model,\n    gen_ai_response_model = EXCLUDED.gen_ai_response_model,\n    gen_ai_input_tokens = EXCLUDED.gen_ai_input_tokens,\n    gen_ai_output_tokens = EXCLUDED.gen_ai_output_tokens,\n    gen_ai_cache_read_input_tokens = EXCLUDED.gen_ai_cache_read_input_tokens,\n    gen_ai_cache_write_input_tokens = EXCLUDED.gen_ai_cache_write_input_tokens,\n    gen_ai_reasoning_tokens = EXCLUDED.gen_ai_reasoning_tokens,\n    attributes_json = EXCLUDED.attributes_json,\n    resource_json = EXCLUDED.resource_json,\n    resource_entity_refs_json = EXCLUDED.resource_entity_refs_json,\n    schema_url = EXCLUDED.schema_url,\n    status_message = EXCLUDED.status_message,\n    events_json = EXCLUDED.events_json,\n    links_json = EXCLUDED.links_json,\n    scope_name = EXCLUDED.scope_name,\n    scope_version = EXCLUDED.scope_version,\n    scope_schema_url = EXCLUDED.scope_schema_url,\n    scope_attributes_json = EXCLUDED.scope_attributes_json,\n    scope_dropped_attributes_count = EXCLUDED.scope_dropped_attributes_count,\n    resource_schema_url = EXCLUDED.resource_schema_url,\n    resource_dropped_attributes_count = EXCLUDED.resource_dropped_attributes_count,\n    trace_state = EXCLUDED.trace_state,\n    flags = EXCLUDED.flags,\n    dropped_attributes_count = EXCLUDED.dropped_attributes_count,\n    dropped_events_count = EXCLUDED.dropped_events_count,\n    dropped_links_count = EXCLUDED.dropped_links_count" })
model SpanStorageRow {
  @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0, sqlType: "VARCHAR(128)" })
  projectId: Nami.Models.Values.ProjectId;
  @duckDbColumn(#{ order: 1, primaryKeyOrdinal: 2 })
  spanId: string;
  @duckDbColumn(#{ order: 2, primaryKeyOrdinal: 1 })
  traceId: string;
  @duckDbColumn(#{ order: 3 })
  parentSpanId?: string | null;
  @duckDbColumn(#{ order: 4 })
  sessionId?: string | null;
  @duckDbColumn(#{ order: 5 })
  name: string;
  @duckDbColumn(#{ order: 6 })
  kind: uint8;
  @duckDbColumn(#{ order: 7 })
  startTimeUnixNano: uint64;
  @duckDbColumn(#{ order: 8 })
  endTimeUnixNano: uint64;
  @duckDbColumn(#{ order: 9 })
  durationNs: uint64;
  @duckDbColumn(#{ order: 10 })
  statusCode: uint8;
  @duckDbColumn(#{ order: 11 })
  serviceName?: string | null;
  @duckDbColumn(#{ order: 12 })
  genAiProviderName?: string | null;
  @duckDbColumn(#{ order: 13 })
  genAiOperationName?: string | null;
  @duckDbColumn(#{ order: 14 })
  genAiOutputType?: string | null;
  @duckDbColumn(#{ order: 15 })
  genAiRequestModel?: string | null;
  @duckDbColumn(#{ order: 16 })
  genAiResponseModel?: string | null;
  @duckDbColumn(#{ order: 17 })
  genAiInputTokens?: int64 | null;
  @duckDbColumn(#{ order: 18 })
  genAiOutputTokens?: int64 | null;
  @duckDbColumn(#{ order: 19 })
  genAiCacheReadInputTokens?: int64 | null;
  @duckDbColumn(#{ order: 20 })
  genAiCacheWriteInputTokens?: int64 | null;
  @duckDbColumn(#{ order: 21 })
  genAiReasoningTokens?: int64 | null;
  @duckDbColumn(#{ order: 22, sqlType: "JSON" })
  attributesJson?: string | null;
  @duckDbColumn(#{ order: 23, sqlType: "JSON" })
  resourceJson?: string | null;
  @duckDbColumn(#{ order: 24, sqlType: "JSON" })
  resourceEntityRefsJson?: string | null;
  @duckDbColumn(#{ order: 25, sqlType: "VARCHAR(256)" })
  schemaUrl?: string | null;
  @duckDbColumn(#{ order: 26, sqlType: "VARCHAR" })
  statusMessage?: string | null;
  @duckDbColumn(#{ order: 27, sqlType: "JSON" })
  eventsJson?: string | null;
  @duckDbColumn(#{ order: 28, sqlType: "JSON" })
  linksJson?: string | null;
  @duckDbColumn(#{ order: 29, sqlType: "VARCHAR(256)" })
  scopeName?: string | null;
  @duckDbColumn(#{ order: 30, sqlType: "VARCHAR(64)" })
  scopeVersion?: string | null;
  @duckDbColumn(#{ order: 31 })
  scopeSchemaUrl?: string | null;
  @duckDbColumn(#{ order: 32, sqlType: "JSON" })
  scopeAttributesJson?: string | null;
  @duckDbColumn(#{ order: 33 })
  scopeDroppedAttributesCount?: uint32 | null;
  @duckDbColumn(#{ order: 34 })
  resourceSchemaUrl?: string | null;
  @duckDbColumn(#{ order: 35 })
  resourceDroppedAttributesCount?: uint32 | null;
  @duckDbColumn(#{ order: 36 })
  traceState?: string | null;
  @duckDbColumn(#{ order: 37 })
  flags?: uint32 | null;
  @duckDbColumn(#{ order: 38 })
  droppedAttributesCount?: uint32 | null;
  @duckDbColumn(#{ order: 39 })
  droppedEventsCount?: uint32 | null;
  @duckDbColumn(#{ order: 40 })
  droppedLinksCount?: uint32 | null;
}

@duckDbTable(#{ name: "logs", indexes: "ProjectId,TimeUnixNano;ProjectId,IngestSequence;ProjectId,TraceId;ProjectId,SessionId;TimeUnixNano", onConflict: "ON CONFLICT (project_id, log_id) DO UPDATE SET\n    trace_id = EXCLUDED.trace_id,\n    span_id = EXCLUDED.span_id,\n    event_name = EXCLUDED.event_name,\n    session_id = EXCLUDED.session_id,\n    time_unix_nano = EXCLUDED.time_unix_nano,\n    observed_time_unix_nano = least(observed_time_unix_nano, EXCLUDED.observed_time_unix_nano),\n    severity_number = EXCLUDED.severity_number,\n    severity_text = EXCLUDED.severity_text,\n    body = EXCLUDED.body,\n    service_name = EXCLUDED.service_name,\n    attributes_json = EXCLUDED.attributes_json,\n    resource_json = EXCLUDED.resource_json,\n    resource_entity_refs_json = EXCLUDED.resource_entity_refs_json,\n    scope_name = EXCLUDED.scope_name,\n    scope_version = EXCLUDED.scope_version,\n    scope_schema_url = EXCLUDED.scope_schema_url,\n    scope_attributes_json = EXCLUDED.scope_attributes_json,\n    scope_dropped_attributes_count = EXCLUDED.scope_dropped_attributes_count,\n    resource_schema_url = EXCLUDED.resource_schema_url,\n    resource_dropped_attributes_count = EXCLUDED.resource_dropped_attributes_count,\n    flags = EXCLUDED.flags,\n    dropped_attributes_count = EXCLUDED.dropped_attributes_count,\n    received_time_unix_nano = coalesce(least(received_time_unix_nano, EXCLUDED.received_time_unix_nano), received_time_unix_nano, EXCLUDED.received_time_unix_nano)" })
model LogStorageRow {
  @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0, sqlType: "VARCHAR(128)" })
  projectId: Nami.Models.Values.ProjectId;
  @duckDbColumn(#{ order: 1, primaryKeyOrdinal: 1 })
  logId: string;
  @duckDbColumn(#{ order: 2 })
  traceId?: string | null;
  @duckDbColumn(#{ order: 3 })
  spanId?: string | null;
  @duckDbColumn(#{ order: 4 })
  eventName?: string | null;
  @duckDbColumn(#{ order: 5 })
  sessionId?: string | null;
  @duckDbColumn(#{ order: 6 })
  timeUnixNano?: uint64 | null;
  @duckDbColumn(#{ order: 7 })
  observedTimeUnixNano?: uint64 | null;
  @duckDbColumn(#{ order: 8 })
  severityNumber: uint8;
  @duckDbColumn(#{ order: 9 })
  severityText?: string | null;
  @duckDbColumn(#{ order: 10 })
  body?: string | null;
  @duckDbColumn(#{ order: 11 })
  serviceName?: string | null;
  @duckDbColumn(#{ order: 12, sqlType: "JSON" })
  attributesJson?: string | null;
  @duckDbColumn(#{ order: 13, sqlType: "JSON" })
  resourceJson?: string | null;
  @duckDbColumn(#{ order: 14, sqlType: "JSON" })
  resourceEntityRefsJson?: string | null;
  @duckDbColumn(#{ order: 15, excludeFromInsert: true, defaultSql: "nextval('logs_ingest_sequence')", privateInit: true })
  ingestSequence?: int64;
  @duckDbColumn(#{ order: 16 })
  scopeName?: string | null;
  @duckDbColumn(#{ order: 17 })
  scopeVersion?: string | null;
  @duckDbColumn(#{ order: 18 })
  scopeSchemaUrl?: string | null;
  @duckDbColumn(#{ order: 19, sqlType: "JSON" })
  scopeAttributesJson?: string | null;
  @duckDbColumn(#{ order: 20 })
  scopeDroppedAttributesCount?: uint32 | null;
  @duckDbColumn(#{ order: 21 })
  resourceSchemaUrl?: string | null;
  @duckDbColumn(#{ order: 22 })
  resourceDroppedAttributesCount?: uint32 | null;
  @duckDbColumn(#{ order: 23 })
  flags?: uint32 | null;
  @duckDbColumn(#{ order: 24 })
  droppedAttributesCount?: uint32 | null;
  @duckDbColumn(#{ order: 25 })
  receivedTimeUnixNano?: uint64 | null;
}

@duckDbTable(#{ name: "signal_watermarks", onConflict: "ON CONFLICT (project_id, signal, service_name) DO UPDATE SET\n  last_seen_unix_nano = greatest(last_seen_unix_nano, EXCLUDED.last_seen_unix_nano),\n  watermark_unix_nano = greatest(watermark_unix_nano, EXCLUDED.watermark_unix_nano)" })
model SignalWatermarkRow {
  @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0 })
  projectId: Nami.Models.Values.ProjectId;
  @duckDbColumn(#{ order: 1, primaryKeyOrdinal: 1 })
  signal: string;
  @duckDbColumn(#{ order: 2, primaryKeyOrdinal: 2 })
  serviceName: string;
  @duckDbColumn(#{ order: 3 })
  lastSeenUnixNano: uint64;
  @duckDbColumn(#{ order: 4 })
  watermarkUnixNano: uint64;
}

@duckDbTable(#{ name: "resource_observations", indexes: "ProjectId,ServiceName,ObservedUnixNano" })
model ResourceObservationRow {
  @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0 })
  projectId: Nami.Models.Values.ProjectId;
  @duckDbColumn(#{ order: 1, primaryKeyOrdinal: 1 })
  observationId: Nami.Models.Values.ObservationId;
  @duckDbColumn(#{ order: 2 })
  serviceName: string;
  @duckDbColumn(#{ order: 3 })
  observedUnixNano: uint64;
  @duckDbColumn(#{ order: 4 })
  sequenceNumber: int64;
  @duckDbColumn(#{ order: 5, sqlType: "JSON" })
  attributesJson: string;
}

@duckDbTable(#{ name: "read_snapshots", indexes: "ExpiresUnixNano" })
model ReadSnapshotRow {
  @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0 })
  projectId: Nami.Models.Values.ProjectId;
  @duckDbColumn(#{ order: 1, primaryKeyOrdinal: 1 })
  snapshotId: string;
  @duckDbColumn(#{ order: 2 })
  expiresUnixNano: uint64;
  @duckDbColumn(#{ order: 3, sqlType: "BLOB" })
  @csharpType("byte[]")
  payload: bytes;
}

@duckDbTable(#{ name: "attribute_stability", onConflict: "ON CONFLICT (project_id, attribute_key) DO UPDATE SET stability = EXCLUDED.stability" })
model AttributeStabilityRow {
  @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0 })
  projectId: Nami.Models.Values.ProjectId;
  @duckDbColumn(#{ order: 1, primaryKeyOrdinal: 1 })
  attributeKey: string;
  @duckDbColumn(#{ order: 2 })
  stability: string;
}

@duckDbTable(#{ name: "attribute_discovery", onConflict: "ON CONFLICT (project_id, original_key) DO UPDATE SET\n  canonical_key = EXCLUDED.canonical_key,\n  last_seen_unix_nano = greatest(last_seen_unix_nano, EXCLUDED.last_seen_unix_nano)" })
model AttributeDiscoveryRow {
  @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0 })
  projectId: Nami.Models.Values.ProjectId;
  @duckDbColumn(#{ order: 1, primaryKeyOrdinal: 1 })
  originalKey: string;
  @duckDbColumn(#{ order: 2 })
  canonicalKey?: string | null;
  @duckDbColumn(#{ order: 3 })
  `namespace`: string;
  @duckDbColumn(#{ order: 4 })
  firstSeenUnixNano: uint64;
  @duckDbColumn(#{ order: 5 })
  lastSeenUnixNano: uint64;
}

@duckDbTable(#{ name: "metric_series", indexes: "ProjectId,MetricName", onConflict: "ON CONFLICT (project_id, series_id) DO UPDATE SET\n    metric_kind = EXCLUDED.metric_kind,\n    temporality = EXCLUDED.temporality,\n    is_monotonic = EXCLUDED.is_monotonic,\n    unit = EXCLUDED.unit,\n    description = CASE\n        WHEN length(coalesce(EXCLUDED.description, '')) > length(coalesce(description, '')) THEN EXCLUDED.description\n        WHEN length(coalesce(EXCLUDED.description, '')) < length(coalesce(description, '')) THEN description\n        ELSE greatest(description, EXCLUDED.description) END,\n    schema_url = EXCLUDED.schema_url,\n    attributes_json = EXCLUDED.attributes_json,\n    resource_json = EXCLUDED.resource_json,\n    resource_entity_refs_json = EXCLUDED.resource_entity_refs_json,\n    first_seen_unix_nano = least(first_seen_unix_nano, EXCLUDED.first_seen_unix_nano),\n    last_seen_unix_nano = greatest(last_seen_unix_nano, EXCLUDED.last_seen_unix_nano),\n    scope_name = EXCLUDED.scope_name,\n    scope_version = EXCLUDED.scope_version,\n    scope_schema_url = EXCLUDED.scope_schema_url,\n    scope_attributes_json = EXCLUDED.scope_attributes_json,\n    scope_dropped_attributes_count = EXCLUDED.scope_dropped_attributes_count,\n    resource_schema_url = EXCLUDED.resource_schema_url,\n    resource_dropped_attributes_count = EXCLUDED.resource_dropped_attributes_count,\n    instrument_id = EXCLUDED.instrument_id" })
model MetricSeriesRow {
  @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0, sqlType: "VARCHAR(128)" })
  projectId: Nami.Models.Values.ProjectId;
  @duckDbColumn(#{ order: 1, primaryKeyOrdinal: 1 })
  seriesId: Nami.Models.Values.SeriesId;
  @duckDbColumn(#{ order: 2 })
  metricName: string;
  @duckDbColumn(#{ order: 3 })
  metricKind: uint8;
  @duckDbColumn(#{ order: 4 })
  temporality: uint8;
  @duckDbColumn(#{ order: 5 })
  isMonotonic: uint8;
  @duckDbColumn(#{ order: 6 })
  unit?: string | null;
  @duckDbColumn(#{ order: 7 })
  description?: string | null;
  @duckDbColumn(#{ order: 8 })
  serviceName?: string | null;
  @duckDbColumn(#{ order: 9, sqlType: "VARCHAR(256)" })
  schemaUrl?: string | null;
  @duckDbColumn(#{ order: 10, sqlType: "JSON" })
  attributesJson?: string | null;
  @duckDbColumn(#{ order: 11, sqlType: "JSON" })
  resourceJson?: string | null;
  @duckDbColumn(#{ order: 12, sqlType: "JSON" })
  resourceEntityRefsJson?: string | null;
  @duckDbColumn(#{ order: 13 })
  firstSeenUnixNano: uint64;
  @duckDbColumn(#{ order: 14 })
  lastSeenUnixNano: uint64;
  @duckDbColumn(#{ order: 15 })
  scopeName?: string | null;
  @duckDbColumn(#{ order: 16 })
  scopeVersion?: string | null;
  @duckDbColumn(#{ order: 17 })
  scopeSchemaUrl?: string | null;
  @duckDbColumn(#{ order: 18, sqlType: "JSON" })
  scopeAttributesJson?: string | null;
  @duckDbColumn(#{ order: 19 })
  scopeDroppedAttributesCount?: uint32 | null;
  @duckDbColumn(#{ order: 20 })
  resourceSchemaUrl?: string | null;
  @duckDbColumn(#{ order: 21 })
  resourceDroppedAttributesCount?: uint32 | null;
  @duckDbColumn(#{ order: 22 })
  instrumentId?: string | null;
}

@duckDbTable(#{ name: "metric_points", derived: true, indexes: "ProjectId,TimeUnixNano", onConflict: "ON CONFLICT (project_id, series_id, time_unix_nano) DO UPDATE SET\n    start_time_unix_nano = EXCLUDED.start_time_unix_nano,\n    value = EXCLUDED.value,\n    histogram_count = EXCLUDED.histogram_count,\n    histogram_sum = EXCLUDED.histogram_sum,\n    histogram_min = EXCLUDED.histogram_min,\n    histogram_max = EXCLUDED.histogram_max,\n    bucket_bounds_json = EXCLUDED.bucket_bounds_json,\n    bucket_counts_json = EXCLUDED.bucket_counts_json,\n    has_conflict = EXCLUDED.has_conflict,\n    projection_unavailable_reason = EXCLUDED.projection_unavailable_reason" })
model MetricPointRow {
  @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0, sqlType: "VARCHAR(128)" })
  projectId: Nami.Models.Values.ProjectId;
  @duckDbColumn(#{ order: 1, primaryKeyOrdinal: 1 })
  seriesId: Nami.Models.Values.SeriesId;
  @duckDbColumn(#{ order: 2, primaryKeyOrdinal: 2 })
  timeUnixNano: uint64;
  @duckDbColumn(#{ order: 3 })
  startTimeUnixNano?: uint64 | null;
  @duckDbColumn(#{ order: 4 })
  value?: float64 | null;
  @duckDbColumn(#{ order: 5 })
  histogramCount?: uint64 | null;
  @duckDbColumn(#{ order: 6 })
  histogramSum?: float64 | null;
  @duckDbColumn(#{ order: 7 })
  histogramMin?: float64 | null;
  @duckDbColumn(#{ order: 8 })
  histogramMax?: float64 | null;
  @duckDbColumn(#{ order: 9, sqlType: "JSON" })
  bucketBoundsJson?: string | null;
  @duckDbColumn(#{ order: 10, sqlType: "JSON" })
  bucketCountsJson?: string | null;
  @duckDbColumn(#{ order: 11 })
  hasConflict?: uint8;
  @duckDbColumn(#{ order: 12 })
  projectionUnavailableReason?: string | null;
}

@duckDbTable(#{ name: "metric_observations", indexes: "ProjectId,SeriesId,TimeUnixNano;ProjectId,TimeUnixNano", onConflict: "ON CONFLICT (project_id, series_id, observation_id) DO NOTHING" })
model MetricObservationRow {
  @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0, sqlType: "VARCHAR(128)" })
  projectId: Nami.Models.Values.ProjectId;
  @duckDbColumn(#{ order: 1, primaryKeyOrdinal: 1 })
  seriesId: Nami.Models.Values.SeriesId;
  @duckDbColumn(#{ order: 2, primaryKeyOrdinal: 2 })
  observationId: Nami.Models.Values.ObservationId;
  @duckDbColumn(#{ order: 3 })
  measurementId: Nami.Models.Values.MeasurementId;
  @duckDbColumn(#{ order: 4 })
  timeUnixNano: uint64;
  @duckDbColumn(#{ order: 5 })
  startTimeUnixNano?: uint64 | null;
  @duckDbColumn(#{ order: 6 })
  flags?: uint32 | null;
  @duckDbColumn(#{ order: 7, sqlType: "JSON" })
  dataJson?: string | null;
  @duckDbColumn(#{ order: 8, sqlType: "JSON" })
  exemplarsJson?: string | null;
  @duckDbColumn(#{ order: 9, sqlType: "JSON" })
  instrumentMetadataJson?: string | null;
  @duckDbColumn(#{ order: 10, sqlType: "JSON" })
  historicalProjectionJson?: string | null;
  @duckDbColumn(#{ order: 11, sqlType: "JSON" })
  historicalExemplarTraceIdsJson?: string | null;
  @duckDbColumn(#{ order: 12 })
  resourceDroppedAttributesCount?: uint32 | null;
  @duckDbColumn(#{ order: 13 })
  scopeDroppedAttributesCount?: uint32 | null;
}

@duckDbTable(#{ name: "metric_exemplars", derived: true, indexes: "ProjectId,TimeUnixNano", onConflict: "ON CONFLICT DO NOTHING" })
model MetricExemplarRow {
  @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0, sqlType: "VARCHAR(128)" })
  projectId: Nami.Models.Values.ProjectId;
  @duckDbColumn(#{ order: 1, primaryKeyOrdinal: 1 })
  seriesId: Nami.Models.Values.SeriesId;
  @duckDbColumn(#{ order: 2, primaryKeyOrdinal: 2 })
  timeUnixNano: uint64;
  @duckDbColumn(#{ order: 3, primaryKeyOrdinal: 3 })
  traceId: string;
}
