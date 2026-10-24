import "@typespec/http";
import "@typespec/openapi";
import "../models/health.tsp";
using TypeSpec.Http;
using TypeSpec.OpenAPI;
@service(#{ title: "Nami telemetry and tools" })
namespace Nami.Api.Contracts;
@doc("Reports this process's health and contract revision.")
@route("/health") @get
op health(): Health.HealthReport;
