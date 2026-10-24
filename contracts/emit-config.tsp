// Build-only mapping; wire definitions remain language independent.
import "./emitters/csharp/lib/main.tsp";
using Nami.Api.Schema.Emit.CSharp;
@@csharpNamespace(Nami.Api.Contracts, "Nami.Api.Contracts");
@@csharpNamespace(Nami.Api.Contracts.Common, "Nami.Api.Contracts.Common");
@@csharpNamespace(Nami.Api.Contracts.Health, "Nami.Api.Contracts.Health");
@@csharpNamespace(Nami.Api.Contracts.Tools, "Nami.Api.Contracts.Tools");
@@csharpNamespace(Nami.Api.Contracts.OTel, "Nami.Api.Contracts.OTel");
@@csharpNamespace(Nami.Api.Contracts.OTel.Enums, "Nami.Api.Contracts.OTel.Enums");
@@csharpNamespace(Nami.Api.Contracts.OTel.Logs, "Nami.Api.Contracts.OTel.Logs");
@@csharpNamespace(Nami.Api.Contracts.OTel.Resource, "Nami.Api.Contracts.OTel.Resource");
@@csharpNamespace(Nami.Api.Contracts.OTel.Traces, "Nami.Api.Contracts.OTel.Traces");
@@csharpNamespace(Nami.Api.Contracts.OTel.Metrics, "Nami.Api.Contracts.OTel.Metrics");
