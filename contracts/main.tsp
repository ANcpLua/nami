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
import "./models/engine.tsp";
import "./api/routes.tsp";
import "./emit-config.tsp";

import "./models/storage-rows.tsp";

import "./models/storage-json.tsp";

import "./models/ingestion.tsp";

import "./models/ingestion-metrics.tsp";

import "./models/configuration.tsp";

import "./models/ingestion-records.tsp";

import "./models/storage-persistence.tsp";
import "./models/storage-numeric-json.tsp";
import "./models/configuration-options.tsp";
import "./models/storage-runtime.tsp";
import "./models/credential-json.tsp";
import "./models/tool-runtime.tsp";
import "./models/contract-runtime.tsp";
