import "./storage-rows.tsp";
import "./ingestion-records.tsp";
using Nami.Api.Schema.Emit.CSharp;
namespace Nami.Models.Storage.Persistence;
@csharpForm("internal sealed partial positional record")
model TelemetrySnapshot {
  id: string;
  projectId: string;
  capturedAt: utcDateTime;
  spans: Nami.Models.Storage.Rows.SpanStorageRow[];
  logs: Nami.Models.Storage.Rows.LogStorageRow[];
  metrics: Nami.Models.Storage.Rows.MetricSeriesRow[];
  signals: Nami.Models.Storage.Rows.SignalWatermarkRow[];
  resources: Nami.Models.Storage.Rows.ResourceObservationRow[];
  attributeStabilities: Nami.Models.Storage.Rows.AttributeStabilityRow[];
  attributeDiscoveries: Nami.Models.Storage.Rows.AttributeDiscoveryRow[];
}
@csharpForm("internal sealed record")
model SpanBatch { spans: Nami.Models.Storage.Rows.SpanStorageRow[]; }
model StorageStats { spanCount: int64 = 0; sessionCount: int64 = 0; }
model SpanEventJson {
  name: string = "";
  timeUnixNano: uint64 = 0;
  attributesJson?: string;
  droppedAttributesCount: uint32 = 0;
}
model SpanLinkJson {
  traceId: string = "";
  spanId: string = "";
  attributesJson?: string;
  droppedAttributesCount: uint32 = 0;
  flags: uint32 = 0;
  traceState?: string;
}
model StoredResourceEntityRef {
  schemaUrl?: string;
  type: string;
  idKeys: string[];
  descriptionKeys: string[];
}
