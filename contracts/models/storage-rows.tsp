import "../common/values.tsp";
import "./ingestion.tsp";
import "../emitters/csharp/lib/main.tsp";
using Nami.Api.Schema.Emit.CSharp;
namespace Nami.Models.Storage.Rows;

@duckDbTable(#{ name: "nami_schema_meta", bootstrap: true, conflict: "update" })
model SchemaMetadataRow {
  @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0 })
  singleton: uint8 = 0;
  @duckDbColumn(#{ order: 1, merge: "replace" })
  authoritativeSchemaHash: string;
  @duckDbColumn(#{ order: 2, merge: "replace" })
  derivedSchemaHash: string;
}

@duckDbTable(#{ name: "spans", indexes: "ProjectId;ProjectId,TraceId;ProjectId,SessionId;ProjectId,StartTimeUnixNano;ProjectId,TraceId,ParentSpanId;ProjectId,ServiceName;ProjectId,GenAiProviderName;ProjectId,GenAiRequestModel;ProjectId,GenAiResponseModel;ProjectId,GenAiOperationName;TraceId;SessionId;StartTimeUnixNano;EndTimeUnixNano;ServiceName;GenAiProviderName;GenAiRequestModel;GenAiResponseModel;GenAiOperationName", conflict: "update" })
model SpanStorageRow {
  @csharpInternal attributeRenames: Nami.Models.Ingestion.AttributeRename[] = #[];
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
  @duckDbColumn(#{ order: 8, merge: "replace" })
  endTimeUnixNano: uint64;
  @duckDbColumn(#{ order: 9, merge: "replace" })
  durationNs: uint64;
  @duckDbColumn(#{ order: 10, merge: "replace" })
  statusCode: uint8;
  @duckDbColumn(#{ order: 11, merge: "coalesce" })
  serviceName?: string | null;
  @duckDbColumn(#{ order: 12, merge: "replace" })
  genAiProviderName?: string | null;
  @duckDbColumn(#{ order: 13, merge: "replace" })
  genAiOperationName?: string | null;
  @duckDbColumn(#{ order: 14, merge: "replace" })
  genAiOutputType?: string | null;
  @duckDbColumn(#{ order: 15, merge: "replace" })
  genAiRequestModel?: string | null;
  @duckDbColumn(#{ order: 16, merge: "replace" })
  genAiResponseModel?: string | null;
  @duckDbColumn(#{ order: 17, merge: "replace" })
  genAiInputTokens?: int64 | null;
  @duckDbColumn(#{ order: 18, merge: "replace" })
  genAiOutputTokens?: int64 | null;
  @duckDbColumn(#{ order: 19, merge: "replace" })
  genAiCacheReadInputTokens?: int64 | null;
  @duckDbColumn(#{ order: 20, merge: "replace" })
  genAiCacheWriteInputTokens?: int64 | null;
  @duckDbColumn(#{ order: 21, merge: "replace" })
  genAiReasoningTokens?: int64 | null;
  @duckDbColumn(#{ order: 22, sqlType: "JSON", merge: "replace" })
  attributesJson?: string | null;
  @duckDbColumn(#{ order: 23, sqlType: "JSON", merge: "replace" })
  resourceJson?: string | null;
  @duckDbColumn(#{ order: 24, sqlType: "JSON", merge: "replace" })
  resourceEntityRefsJson?: string | null;
  @duckDbColumn(#{ order: 25, sqlType: "VARCHAR(256)", merge: "replace" })
  schemaUrl?: string | null;
  @duckDbColumn(#{ order: 26, sqlType: "VARCHAR", merge: "replace" })
  statusMessage?: string | null;
  @duckDbColumn(#{ order: 27, sqlType: "JSON", merge: "replace" })
  eventsJson?: string | null;
  @duckDbColumn(#{ order: 28, sqlType: "JSON", merge: "replace" })
  linksJson?: string | null;
  @duckDbColumn(#{ order: 29, sqlType: "VARCHAR(256)", merge: "replace" })
  scopeName?: string | null;
  @duckDbColumn(#{ order: 30, sqlType: "VARCHAR(64)", merge: "replace" })
  scopeVersion?: string | null;
  @duckDbColumn(#{ order: 31, merge: "replace" })
  scopeSchemaUrl?: string | null;
  @duckDbColumn(#{ order: 32, sqlType: "JSON", merge: "replace" })
  scopeAttributesJson?: string | null;
  @duckDbColumn(#{ order: 33, merge: "replace" })
  scopeDroppedAttributesCount?: uint32 | null;
  @duckDbColumn(#{ order: 34, merge: "replace" })
  resourceSchemaUrl?: string | null;
  @duckDbColumn(#{ order: 35, merge: "replace" })
  resourceDroppedAttributesCount?: uint32 | null;
  @duckDbColumn(#{ order: 36, merge: "replace" })
  traceState?: string | null;
  @duckDbColumn(#{ order: 37, merge: "replace" })
  flags?: uint32 | null;
  @duckDbColumn(#{ order: 38, merge: "replace" })
  droppedAttributesCount?: uint32 | null;
  @duckDbColumn(#{ order: 39, merge: "replace" })
  droppedEventsCount?: uint32 | null;
  @duckDbColumn(#{ order: 40, merge: "replace" })
  droppedLinksCount?: uint32 | null;
}

@duckDbTable(#{ name: "logs", indexes: "ProjectId,TimeUnixNano;ProjectId,IngestSequence;ProjectId,TraceId;ProjectId,SessionId;TimeUnixNano", conflict: "update" })
model LogStorageRow {
  @csharpInternal attributeRenames: Nami.Models.Ingestion.AttributeRename[] = #[];
  @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0, sqlType: "VARCHAR(128)" })
  projectId: Nami.Models.Values.ProjectId;
  @duckDbColumn(#{ order: 1, primaryKeyOrdinal: 1 })
  logId: string;
  @duckDbColumn(#{ order: 2, merge: "replace" })
  traceId?: string | null;
  @duckDbColumn(#{ order: 3, merge: "replace" })
  spanId?: string | null;
  @duckDbColumn(#{ order: 4, merge: "replace" })
  eventName?: string | null;
  @duckDbColumn(#{ order: 5, merge: "replace" })
  sessionId?: string | null;
  @duckDbColumn(#{ order: 6, merge: "replace" })
  timeUnixNano?: uint64 | null;
  @duckDbColumn(#{ order: 7, merge: "min" })
  observedTimeUnixNano?: uint64 | null;
  @duckDbColumn(#{ order: 8, merge: "replace" })
  severityNumber: uint8;
  @duckDbColumn(#{ order: 9, merge: "replace" })
  severityText?: string | null;
  @duckDbColumn(#{ order: 10, merge: "replace" })
  body?: string | null;
  @duckDbColumn(#{ order: 11, merge: "replace" })
  serviceName?: string | null;
  @duckDbColumn(#{ order: 12, sqlType: "JSON", merge: "replace" })
  attributesJson?: string | null;
  @duckDbColumn(#{ order: 13, sqlType: "JSON", merge: "replace" })
  resourceJson?: string | null;
  @duckDbColumn(#{ order: 14, sqlType: "JSON", merge: "replace" })
  resourceEntityRefsJson?: string | null;
  @duckDbColumn(#{ order: 15, excludeFromInsert: true, sequenceName: "logs_ingest_sequence", privateInit: true })
  ingestSequence?: int64;
  @duckDbColumn(#{ order: 16, merge: "replace" })
  scopeName?: string | null;
  @duckDbColumn(#{ order: 17, merge: "replace" })
  scopeVersion?: string | null;
  @duckDbColumn(#{ order: 18, merge: "replace" })
  scopeSchemaUrl?: string | null;
  @duckDbColumn(#{ order: 19, sqlType: "JSON", merge: "replace" })
  scopeAttributesJson?: string | null;
  @duckDbColumn(#{ order: 20, merge: "replace" })
  scopeDroppedAttributesCount?: uint32 | null;
  @duckDbColumn(#{ order: 21, merge: "replace" })
  resourceSchemaUrl?: string | null;
  @duckDbColumn(#{ order: 22, merge: "replace" })
  resourceDroppedAttributesCount?: uint32 | null;
  @duckDbColumn(#{ order: 23, merge: "replace" })
  flags?: uint32 | null;
  @duckDbColumn(#{ order: 24, merge: "replace" })
  droppedAttributesCount?: uint32 | null;
  @duckDbColumn(#{ order: 25, merge: "minPresent" })
  receivedTimeUnixNano?: uint64 | null;
}

@duckDbTable(#{ name: "signal_watermarks", conflict: "update" })
model SignalWatermarkRow {
  @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0 })
  projectId: Nami.Models.Values.ProjectId;
  @duckDbColumn(#{ order: 1, primaryKeyOrdinal: 1 })
  signal: string;
  @duckDbColumn(#{ order: 2, primaryKeyOrdinal: 2 })
  serviceName: string;
  @duckDbColumn(#{ order: 3, merge: "max" })
  lastSeenUnixNano: uint64;
  @duckDbColumn(#{ order: 4, merge: "max" })
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

@duckDbTable(#{ name: "attribute_stability", conflict: "update" })
model AttributeStabilityRow {
  @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0 })
  projectId: Nami.Models.Values.ProjectId;
  @duckDbColumn(#{ order: 1, primaryKeyOrdinal: 1 })
  attributeKey: string;
  @duckDbColumn(#{ order: 2, merge: "replace" })
  stability: string;
}

@duckDbTable(#{ name: "attribute_discovery", conflict: "update" })
model AttributeDiscoveryRow {
  @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0 })
  projectId: Nami.Models.Values.ProjectId;
  @duckDbColumn(#{ order: 1, primaryKeyOrdinal: 1 })
  originalKey: string;
  @duckDbColumn(#{ order: 2, merge: "replace" })
  canonicalKey?: string | null;
  @duckDbColumn(#{ order: 3 })
  `namespace`: string;
  @duckDbColumn(#{ order: 4 })
  firstSeenUnixNano: uint64;
  @duckDbColumn(#{ order: 5, merge: "max" })
  lastSeenUnixNano: uint64;
}

@duckDbTable(#{ name: "metric_series", indexes: "ProjectId,MetricName", conflict: "update" })
model MetricSeriesRow {
  @csharpInternal attributeRenames: Nami.Models.Ingestion.AttributeRename[] = #[];
  @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0, sqlType: "VARCHAR(128)" })
  projectId: Nami.Models.Values.ProjectId;
  @duckDbColumn(#{ order: 1, primaryKeyOrdinal: 1 })
  seriesId: Nami.Models.Values.SeriesId;
  @duckDbColumn(#{ order: 2 })
  metricName: string;
  @duckDbColumn(#{ order: 3, merge: "replace" })
  metricKind: uint8;
  @duckDbColumn(#{ order: 4, merge: "replace" })
  temporality: uint8;
  @duckDbColumn(#{ order: 5, merge: "replace" })
  isMonotonic: uint8;
  @duckDbColumn(#{ order: 6, merge: "replace" })
  unit?: string | null;
  @duckDbColumn(#{ order: 7, merge: "longest" })
  description?: string | null;
  @duckDbColumn(#{ order: 8 })
  serviceName?: string | null;
  @duckDbColumn(#{ order: 9, sqlType: "VARCHAR(256)", merge: "replace" })
  schemaUrl?: string | null;
  @duckDbColumn(#{ order: 10, sqlType: "JSON", merge: "replace" })
  attributesJson?: string | null;
  @duckDbColumn(#{ order: 11, sqlType: "JSON", merge: "replace" })
  resourceJson?: string | null;
  @duckDbColumn(#{ order: 12, sqlType: "JSON", merge: "replace" })
  resourceEntityRefsJson?: string | null;
  @duckDbColumn(#{ order: 13, merge: "min" })
  firstSeenUnixNano: uint64;
  @duckDbColumn(#{ order: 14, merge: "max" })
  lastSeenUnixNano: uint64;
  @duckDbColumn(#{ order: 15, merge: "replace" })
  scopeName?: string | null;
  @duckDbColumn(#{ order: 16, merge: "replace" })
  scopeVersion?: string | null;
  @duckDbColumn(#{ order: 17, merge: "replace" })
  scopeSchemaUrl?: string | null;
  @duckDbColumn(#{ order: 18, sqlType: "JSON", merge: "replace" })
  scopeAttributesJson?: string | null;
  @duckDbColumn(#{ order: 19, merge: "replace" })
  scopeDroppedAttributesCount?: uint32 | null;
  @duckDbColumn(#{ order: 20, merge: "replace" })
  resourceSchemaUrl?: string | null;
  @duckDbColumn(#{ order: 21, merge: "replace" })
  resourceDroppedAttributesCount?: uint32 | null;
  @duckDbColumn(#{ order: 22, merge: "replace" })
  instrumentId: Nami.Models.Values.InstrumentId;
}

@duckDbTable(#{ name: "metric_points", derived: true, indexes: "ProjectId,TimeUnixNano", conflict: "update" })
model MetricPointRow {
  @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0, sqlType: "VARCHAR(128)" })
  projectId: Nami.Models.Values.ProjectId;
  @duckDbColumn(#{ order: 1, primaryKeyOrdinal: 1 })
  seriesId: Nami.Models.Values.SeriesId;
  @duckDbColumn(#{ order: 2, primaryKeyOrdinal: 2 })
  timeUnixNano: uint64;
  @duckDbColumn(#{ order: 3, merge: "replace" })
  startTimeUnixNano?: uint64 | null;
  @duckDbColumn(#{ order: 4, merge: "replace" })
  value?: float64 | null;
  @duckDbColumn(#{ order: 5, merge: "replace" })
  histogramCount?: uint64 | null;
  @duckDbColumn(#{ order: 6, merge: "replace" })
  histogramSum?: float64 | null;
  @duckDbColumn(#{ order: 7, merge: "replace" })
  histogramMin?: float64 | null;
  @duckDbColumn(#{ order: 8, merge: "replace" })
  histogramMax?: float64 | null;
  @duckDbColumn(#{ order: 9, sqlType: "JSON", merge: "replace" })
  bucketBoundsJson?: string | null;
  @duckDbColumn(#{ order: 10, sqlType: "JSON", merge: "replace" })
  bucketCountsJson?: string | null;
  @duckDbColumn(#{ order: 11, merge: "replace" })
  hasConflict?: uint8;
  @duckDbColumn(#{ order: 12, merge: "replace" })
  projectionUnavailableReason?: string | null;
}

@duckDbTable(#{ name: "metric_observations", indexes: "ProjectId,SeriesId,TimeUnixNano;ProjectId,TimeUnixNano", conflict: "ignore" })
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
  dataJson: string;
  @duckDbColumn(#{ order: 8, sqlType: "JSON" })
  exemplarsJson?: string | null;
  @duckDbColumn(#{ order: 9, sqlType: "JSON" })
  instrumentMetadataJson?: string | null;
  @duckDbColumn(#{ order: 12 })
  resourceDroppedAttributesCount?: uint32 | null;
  @duckDbColumn(#{ order: 13 })
  scopeDroppedAttributesCount?: uint32 | null;
}

@duckDbTable(#{ name: "metric_exemplars", derived: true, indexes: "ProjectId,TimeUnixNano", conflict: "ignore" })
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
