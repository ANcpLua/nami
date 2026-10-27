import "./storage-rows.tsp";
using Nami.Api.Schema.Emit.CSharp;
namespace Nami.Models.Storage.Runtime;

@csharpForm("internal readonly record struct")
model ArrivingResource {
  projectId: string;
  serviceName: string | null;
  attributesJson: string | null;
  recordAttributesJson?: string;
  scopeAttributesJson?: string;
  renames?: Nami.Models.Ingestion.AttributeRename[];
}

@csharpForm("internal readonly record struct")
model StorageFileMetrics {
  databaseFileSizeBytes: int64;
  walFileSizeBytes: int64;
  managedStorageBytes: int64;
  storageFreeBytes: int64;
}
@csharpForm("internal readonly record struct")
model MetricRetentionResult { points: int32; series: int32; }
@csharpForm("internal readonly record struct")
model MetricWriteSet {
  series: Nami.Models.Storage.Rows.MetricSeriesRow[];
  observations: Nami.Models.Storage.Rows.MetricObservationRow[];
}

model SpanStorageValues {
  attributeRenames: Nami.Models.Storage.Rows.SpanStorageRow.attributeRenames;
  projectId: Nami.Models.Storage.Rows.SpanStorageRow.projectId;
  parentSpanId: Nami.Models.Storage.Rows.SpanStorageRow.parentSpanId;
  sessionId: Nami.Models.Storage.Rows.SpanStorageRow.sessionId;
  kind: Nami.Models.Storage.Rows.SpanStorageRow.kind;
  durationNs: Nami.Models.Storage.Rows.SpanStorageRow.durationNs;
  statusCode: Nami.Models.Storage.Rows.SpanStorageRow.statusCode;
  attributesJson: Nami.Models.Storage.Rows.SpanStorageRow.attributesJson;
  resourceJson: Nami.Models.Storage.Rows.SpanStorageRow.resourceJson;
  resourceEntityRefsJson: Nami.Models.Storage.Rows.SpanStorageRow.resourceEntityRefsJson;
  eventsJson: Nami.Models.Storage.Rows.SpanStorageRow.eventsJson;
  linksJson: Nami.Models.Storage.Rows.SpanStorageRow.linksJson;
  scopeAttributesJson: Nami.Models.Storage.Rows.SpanStorageRow.scopeAttributesJson;
}
model LogStorageValues {
  attributeRenames: Nami.Models.Storage.Rows.LogStorageRow.attributeRenames;
  projectId: Nami.Models.Storage.Rows.LogStorageRow.projectId;
  logId: Nami.Models.Storage.Rows.LogStorageRow.logId;
  sessionId: Nami.Models.Storage.Rows.LogStorageRow.sessionId;
  timeUnixNano: Nami.Models.Storage.Rows.LogStorageRow.timeUnixNano;
  observedTimeUnixNano: Nami.Models.Storage.Rows.LogStorageRow.observedTimeUnixNano;
  severityNumber: Nami.Models.Storage.Rows.LogStorageRow.severityNumber;
  severityText: Nami.Models.Storage.Rows.LogStorageRow.severityText;
  body: Nami.Models.Storage.Rows.LogStorageRow.body;
  attributesJson: Nami.Models.Storage.Rows.LogStorageRow.attributesJson;
  resourceJson: Nami.Models.Storage.Rows.LogStorageRow.resourceJson;
  resourceEntityRefsJson: Nami.Models.Storage.Rows.LogStorageRow.resourceEntityRefsJson;
  scopeAttributesJson: Nami.Models.Storage.Rows.LogStorageRow.scopeAttributesJson;
  receivedTimeUnixNano: Nami.Models.Storage.Rows.LogStorageRow.receivedTimeUnixNano;
}
model MetricSeriesValues {
  attributeRenames: Nami.Models.Storage.Rows.MetricSeriesRow.attributeRenames;
  projectId: Nami.Models.Storage.Rows.MetricSeriesRow.projectId;
  seriesId: Nami.Models.Storage.Rows.MetricSeriesRow.seriesId;
  instrumentId: Nami.Models.Storage.Rows.MetricSeriesRow.instrumentId;
  metricKind: Nami.Models.Storage.Rows.MetricSeriesRow.metricKind;
  temporality: Nami.Models.Storage.Rows.MetricSeriesRow.temporality;
  isMonotonic: Nami.Models.Storage.Rows.MetricSeriesRow.isMonotonic;
  attributesJson: Nami.Models.Storage.Rows.MetricSeriesRow.attributesJson;
  resourceJson: Nami.Models.Storage.Rows.MetricSeriesRow.resourceJson;
  resourceEntityRefsJson: Nami.Models.Storage.Rows.MetricSeriesRow.resourceEntityRefsJson;
  scopeAttributesJson: Nami.Models.Storage.Rows.MetricSeriesRow.scopeAttributesJson;
  firstSeenUnixNano: Nami.Models.Storage.Rows.MetricSeriesRow.firstSeenUnixNano;
  lastSeenUnixNano: Nami.Models.Storage.Rows.MetricSeriesRow.lastSeenUnixNano;
}

@catalogProjection(Nami.Models.Ingestion.OtlpAttributeValue)
model SpanHotAttributeProjection {
  @catalogValue("GenAiProviderName")
  genAiProviderName: Nami.Models.Storage.Rows.SpanStorageRow.genAiProviderName;
  @catalogValue("GenAiOperationName")
  genAiOperationName: Nami.Models.Storage.Rows.SpanStorageRow.genAiOperationName;
  @catalogValue("GenAiOutputType")
  genAiOutputType: Nami.Models.Storage.Rows.SpanStorageRow.genAiOutputType;
  @catalogValue("GenAiRequestModel")
  genAiRequestModel: Nami.Models.Storage.Rows.SpanStorageRow.genAiRequestModel;
  @catalogValue("GenAiResponseModel")
  genAiResponseModel: Nami.Models.Storage.Rows.SpanStorageRow.genAiResponseModel;
  @catalogValue("GenAiInputTokens")
  genAiInputTokens: Nami.Models.Storage.Rows.SpanStorageRow.genAiInputTokens;
  @catalogValue("GenAiOutputTokens")
  genAiOutputTokens: Nami.Models.Storage.Rows.SpanStorageRow.genAiOutputTokens;
  @catalogValue("GenAiCacheReadInputTokens")
  genAiCacheReadInputTokens: Nami.Models.Storage.Rows.SpanStorageRow.genAiCacheReadInputTokens;
  @catalogValue("GenAiCacheWriteInputTokens")
  genAiCacheWriteInputTokens: Nami.Models.Storage.Rows.SpanStorageRow.genAiCacheWriteInputTokens;
  @catalogValue("GenAiReasoningTokens")
  genAiReasoningTokens: Nami.Models.Storage.Rows.SpanStorageRow.genAiReasoningTokens;
}
