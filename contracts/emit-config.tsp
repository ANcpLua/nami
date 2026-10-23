// Build-only routing for the C# and TypeScript contract emitters.

import "./emitters/csharp/lib/main.tsp";
import "./emitters/ts-types/lib/main.tsp";
import "./main.tsp";

using Nami.Api.Schema.Emit.CSharp;
using Nami.Api.Schema.Emit.TsTypes;

@@csharpNamespace(Nami.Api.Contracts, "Nami.Api.Contracts");
@@csharpNamespace(Nami.Api.Contracts.Streaming, "Nami.Api.Contracts.Streaming");
@@csharpNamespace(Nami.Api.Contracts.Health, "Nami.Api.Contracts.Health");
@@csharpNamespace(
  Nami.Api.Contracts.OTel.Metrics,
  "Nami.Api.Contracts.OTel.Metrics"
);
@@csharpNamespace(Nami.Api.Contracts.Mcp.Tools, "Nami.Api.Contracts.Mcp.Tools");
@@csharpNamespace(Nami.Api.Contracts.Runner, "Nami.Api.Contracts.Runner");
@@csharpNamespace(
  Nami.Api.Contracts.Annotations,
  "Nami.Api.Contracts.Annotations"
);
@@csharpNamespace(
  Nami.Api.Contracts.SavedQueries,
  "Nami.Api.Contracts.SavedQueries"
);
@@csharpPolymorphic(Nami.Api.Contracts.Annotations.AnnotationTarget, "kind");
@@csharpPolymorphic(Nami.Api.Contracts.SavedQueries.SavedQueryFilter, "signal");
@@csharpPolymorphic(
  Nami.Api.Contracts.SavedQueries.SavedQueryRunResult,
  "signal"
);

@@csharpNamespace(Nami.Api.Contracts.Common, "Nami.Api.Contracts.Common");
@@csharpNamespace(
  Nami.Api.Contracts.Common.Errors,
  "Nami.Api.Contracts.Common.Errors"
);
@@csharpNamespace(
  Nami.Api.Contracts.Common.Pagination,
  "Nami.Api.Contracts.Common.Pagination"
);

@@csharpNamespace(
  Nami.Api.Contracts.Domains.Observe.Session,
  "Nami.Api.Contracts.Domains.Observe.Session"
);

@@csharpNamespace(Nami.Api.Contracts.OTel, "Nami.Api.Contracts.OTel");
@@csharpNamespace(Nami.Api.Contracts.OTel.Enums, "Nami.Api.Contracts.OTel.Enums");
@@csharpNamespace(Nami.Api.Contracts.OTel.Logs, "Nami.Api.Contracts.OTel.Logs");
@@csharpNamespace(
  Nami.Api.Contracts.OTel.Resource,
  "Nami.Api.Contracts.OTel.Resource"
);
@@csharpNamespace(
  Nami.Api.Contracts.OTel.Traces,
  "Nami.Api.Contracts.OTel.Traces"
);

@@tsBrand(Nami.Api.Contracts.Common.TraceId);
@@tsBrand(Nami.Api.Contracts.Common.SpanId);
@@tsBrand(Nami.Api.Contracts.Common.SessionId);
@@tsBrand(Nami.Api.Contracts.Common.UserId);
