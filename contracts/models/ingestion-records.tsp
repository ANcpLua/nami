import "./ingestion-context.tsp";
import "./ingestion.tsp";
import "./ingestion-metrics.tsp";
import "../common/values.tsp";
using Nami.Api.Schema.Emit.CSharp;
namespace Nami.Models.Ingestion.Records;
@csharpForm("internal sealed record")
model TraceIngestionBatch { spans: SpanIngestionRecord[]; rejectedSpans: int64 = 0; rejectionMessage?: string; }
@csharpForm("internal sealed record")
model LogIngestionBatch { logs: LogIngestionRecord[]; rejectedLogRecords: int64 = 0; rejectionMessage?: string; }
@csharpForm("internal sealed record")
model MetricIngestionBatch { points: MetricPointIngestionRecord[]; rejectedDataPoints: int64; rejectionMessage: string | null; }
@csharpForm("internal sealed record")
model ResourceEntityRefIngestionRecord { schemaUrl: string | null; type: string; idKeys: string[]; descriptionKeys: string[]; }
@csharpForm("internal sealed record")
model SpanEventIngest { name: string; timeUnixNano: Nami.Models.Values.UnixNanos; attributes: Nami.Models.Ingestion.NormalizedAttributes; droppedAttributesCount: uint32 = 0; }
@csharpForm("internal sealed record")
model SpanLinkIngest { traceId: string; spanId: string; attributes: Nami.Models.Ingestion.NormalizedAttributes; traceState?: string; flags: uint32 = 0; droppedAttributesCount: uint32 = 0; }
model SpanIngestionRecord {
  ...Nami.Shapes.Ingestion.ResourceAndScope;
  spanId: string;
  traceId: string;
  parentSpanId?: string;
  name: string;
  kind?: int32;
  startTimeUnixNano: Nami.Models.Values.UnixNanos;
  endTimeUnixNano: Nami.Models.Values.UnixNanos;
  statusCode?: int32;
  attributes: Nami.Models.Ingestion.NormalizedAttributes;
  schemaUrl?: string;
  traceState?: string;
  flags: uint32 = 0;
  droppedAttributesCount: uint32 = 0;
  droppedEventsCount: uint32 = 0;
  droppedLinksCount: uint32 = 0;
  statusMessage?: string;
  events: SpanEventIngest[] = #[];
  links: SpanLinkIngest[] = #[];
}

model LogIngestionRecord {
  ...Nami.Shapes.Ingestion.ResourceAndScope;
  flags: uint32 = 0;
  droppedAttributesCount: uint32 = 0;
  traceId?: string;
  spanId?: string;
  eventName?: string;
  timeOrigin: Nami.Models.Ingestion.LogTimeOrigin = #{};
  severityNumber: int32;
  severityText?: string;
  body: OtlpAttributeValue;
  attributes: Nami.Models.Ingestion.NormalizedAttributes;
}

model MetricPointIngestionRecord {
  ...Nami.Shapes.Ingestion.ResourceAndScope;
  resourceIdentity?: string;
  scopeIdentity?: string;
  attributesIdentity?: string;
  metricName: string;
  shape: Nami.Models.Ingestion.Metrics.MetricShape;
  unit?: string;
  description?: string;
  schemaUrl?: string;
  attributes: Nami.Models.Ingestion.NormalizedAttributes;
  timeUnixNano: Nami.Models.Values.UnixNanos;
  startTimeUnixNano?: Nami.Models.Values.UnixNanos;
  flags: uint32 = 0;
  exemplars: Nami.Models.Ingestion.NormalizedMetricExemplars = #{ values: #[], renames: #[] };
  instrumentMetadata: Nami.Models.Ingestion.NormalizedContractAttributes = #{ values: #[], renames: #[] };
}
