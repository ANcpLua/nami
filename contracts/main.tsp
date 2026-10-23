// The nami API contract: every model and route, plus the emitter routing in emit-config.tsp.

import "@typespec/events";
import "@typespec/http";
import "@typespec/openapi";
import "@typespec/sse";

import "./common/types.tsp";
import "./common/errors.tsp";
import "./common/pagination.tsp";

import "./otel/enums.tsp";
import "./otel/resource.tsp";
import "./otel/span.tsp";
import "./otel/logs.tsp";

import "./models/session.tsp";
import "./models/health.tsp";
import "./models/metrics.tsp";
import "./models/runner.tsp";
import "./models/mcp-tools.tsp";

import "./api/routes.tsp";
import "./api/streaming.tsp";
import "./api/runner.tsp";

import "./emit-config.tsp";

import "./api/analysis.tsp";
