import "@typespec/events";
import "@typespec/http";
import "@typespec/sse";

import "./routes.tsp";
import "../common/errors.tsp";
import "../otel/logs.tsp";

using TypeSpec.Events;
using TypeSpec.Http;
using TypeSpec.SSE;

using Nami.Api.Contracts.Common.Errors;
using Nami.Api.Contracts.OTel.Logs;

namespace Nami.Api.Contracts.Streaming;

@doc("One log delivered by the collector's live stream.")
model LogStreamEvent {
  type: "log";
  data: LogRecord;
  @doc("Event timestamp, falling back to the observed timestamp. Absent when both are historically unknown; this is not the stream emission time.")
  timestamp?: Nami.Api.Contracts.Common.Timestamp;
}

@doc("Keep-alive event emitted while no new log is available.")
model HeartbeatEvent {
  type: "heartbeat";
  timestamp: Nami.Api.Contracts.Common.Timestamp;
}

@events
union LogEvents {
  log: LogStreamEvent,
  heartbeat: HeartbeatEvent,
}

@route("/api/v1/stream")
@tag("Streaming")
interface StreamingApi {
  @get
  @route("/logs")
  @useAuth(Nami.Api.Contracts.NamiApiKeyHeaderAuth)
  streamLogs(
    ...Nami.Api.Contracts.ProjectScopeHeader,
    @header("Last-Event-ID") lastEventId?: string,
    @query("service_name") serviceName?: string,
    @query("min_severity") @minValue(1) @maxValue(24) minSeverity?: int32,
    @query query?: string,
  ):
    | SSEStream<LogEvents>
    | ValidationError
    | UnauthorizedError
    | ServiceUnavailableError
    | Nami.Api.Contracts.CollectorServerErrorResponse;
}
