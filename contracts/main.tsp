// Nami's typed MCP contract and standard telemetry projections.
import "@typespec/http";
import "@typespec/openapi";
import "@typespec/json-schema";
import "./common/types.tsp";
import "./common/errors.tsp";
import "./common/telemetry-query.tsp";
import "./otel/enums.tsp";
import "./otel/resource.tsp";
import "./otel/span.tsp";
import "./otel/logs.tsp";
import "./models/metric-points.tsp";
import "./models/health.tsp";
import "./models/tools.tsp";
import "./api/routes.tsp";
import "./emit-config.tsp";
