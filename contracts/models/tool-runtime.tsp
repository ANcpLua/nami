import "../otel/span.tsp";
import "./storage-persistence.tsp";
using Nami.Api.Schema.Emit.CSharp;
namespace Nami.Models.Tools.Runtime;

@csharpType("System.Text.Json.Nodes.JsonObject") model ProjectedJsonObject {}
@csharpForm("internal readonly record struct")
model ToolRunResult { content: unknown; isError: boolean; }
@csharpForm("internal sealed record")
model ToolRows {
  member: string;
  items: ProjectedJsonObject[];
  examined: int64;
  signals: string[];
  allowedFields: string[];
  single: boolean = false;
}
@csharpForm("internal readonly partial record struct")
model ToolWindow { start: utcDateTime; end: utcDateTime; service: string | null; watermarks?: Record<uint64>; }

model SpanApiValues {
  kind: Nami.Api.Contracts.OTel.Traces.Span.kind;
  attributes: Nami.Api.Contracts.OTel.Traces.Span.attributes;
  status: Nami.Api.Contracts.OTel.Traces.Span.status;
  resource: Nami.Api.Contracts.OTel.Traces.Span.resource;
  instrumentationScope: Nami.Api.Contracts.OTel.Traces.Span.instrumentationScope | null;
  events: Nami.Api.Contracts.OTel.Traces.Span.events | null;
  links: Nami.Api.Contracts.OTel.Traces.Span.links | null;
}
model ResourceApiValues {
  serviceName: Nami.Api.Contracts.OTel.Resource.Resource.serviceName;
  attributes: Nami.Api.Contracts.OTel.Resource.Resource.attributes;
  entityRefs: Nami.Api.Contracts.OTel.Resource.Resource.entityRefs | null;
}
model ScopeApiValues { scopeAttributes: Nami.Api.Contracts.Common.InstrumentationScope.scopeAttributes; }
model EventApiValues { attributes: Nami.Api.Contracts.OTel.Traces.SpanEvent.attributes; }
model LinkApiValues { attributes: Nami.Api.Contracts.OTel.Traces.SpanLink.attributes; }
