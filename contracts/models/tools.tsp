import "../common/telemetry-query.tsp";
import "../otel/span.tsp";
using Nami.Api.Contracts.Common;
using TypeSpec.JsonSchema;
namespace Nami.Api.Contracts.Tools;

@doc("The health of telemetry sources, including silent signals.")
@jsonSchema
model SourcesStatusInput extends ToolReadInput {}
@doc("Recorded service and signal activity.")
@jsonSchema
model SourcesStatusResult extends ToolResult { services: ProjectedSourceStatus[]; }
@doc("A service's telemetry signals.")
model SourceStatus { service: string; signals: SignalStatus[]; }
@doc("The latest receipt time for one signal, or how long it has been silent.")
model SignalStatus {
  signal: "traces" | "metrics" | "logs";
  kind: "active" | "silent";
  @encodedName("application/json", "last_seen") lastSeen?: Timestamp;
  @encodedName("application/json", "silent_for") silentFor?: IsoDuration;
}

@doc("Recent errors grouped by error type and operation, without stack traces.")
@jsonSchema
model ErrorsRecentInput extends ToolReadInput {}
@doc("Groups of errors from log events, span events and error-status spans.")
@jsonSchema
model ErrorsRecentResult extends ToolResult { groups: ProjectedErrorGroup[]; }
@doc("A deduplicated error group and one correlated example when available.")
model ErrorGroup {
  @encodedName("application/json", "error_type") errorType: string;
  operation: string;
  @encodedName("application/json", "first_seen") firstSeen: Timestamp;
  @encodedName("application/json", "last_seen") lastSeen: Timestamp;
  count: Count;
  @encodedName("application/json", "example_trace_id") exampleTraceId?: TraceId;
  source: "log_event" | "span_event" | "span_status";
}

@doc("Read one trace, projecting only requested fields.")
@jsonSchema
model TracesGetInput extends ToolReadInput {
  @encodedName("application/json", "trace_id") traceId: TraceId;
}
@doc("A projected trace. An omitted field was not requested; it is not a fabricated value.")
model ProjectedTrace { ...OptionalProperties<Nami.Api.Contracts.OTel.Traces.Trace>; }
@doc("The requested trace and evidence completeness.")
@jsonSchema
model TracesGetResult extends ToolResult { trace: ProjectedTrace; }

@doc("The slowest traces in the selected window.")
@jsonSchema
model TracesSlowestInput extends ToolReadInput {
  @minLength(1) @maxLength(1024) operation?: string;
  @minValue(1) @maxValue(100) limit?: int32 = 10;
}
@doc("Traces ranked by descending duration.")
@jsonSchema
model TracesSlowestResult extends ToolResult { traces: ProjectedSlowTrace[]; }
@doc("One trace in a duration ranking.")
model SlowTrace {
  @encodedName("application/json", "trace_id") traceId: TraceId;
  operation: string;
  @encodedName("application/json", "duration_ms") durationMs: DurationMs;
  @encodedName("application/json", "started_at") startedAt: Timestamp;
  status: "unset" | "ok" | "error";
}

@doc("Find log bodies containing literal, case-sensitive text; this is not a regular expression.")
@jsonSchema
model LogsSearchInput extends ToolReadInput {
  @minLength(1) @maxLength(4096) query: string;
}
@doc("Log records containing the literal query.")
@jsonSchema
model LogsSearchResult extends ToolResult { records: ProjectedLogHit[]; }
@doc("A matching log with optional trace correlation.")
model LogHit {
  timestamp: Timestamp;
  severity: string;
  body: string;
  @encodedName("application/json", "trace_id") traceId?: TraceId;
  @encodedName("application/json", "span_id") spanId?: SpanId;
  service: string;
}

@doc("Resource changes recorded in telemetry, without querying a deployment service.")
@jsonSchema
model ChangesRecentInput extends ToolReadInput {}
@doc("Observed revision, version, environment and pipeline changes.")
@jsonSchema
model ChangesRecentResult extends ToolResult { changes: ProjectedChange[]; }
@doc("A resource attribute transition observed on ingestion.")
model Change {
  @encodedName("application/json", "observed_at") observedAt: Timestamp;
  service: string;
  kind: "revision" | "version" | "environment" | "pipeline";
  from?: string;
  to: string;
  @encodedName("application/json", "source_key") sourceKey: string;
}

@doc("A field projection of SourceStatus; omitted members were not requested.")
model ProjectedSourceStatus { ...OptionalProperties<SourceStatus>; }

@doc("A field projection of ErrorGroup; omitted members were not requested.")
model ProjectedErrorGroup { ...OptionalProperties<ErrorGroup>; }

@doc("A field projection of SlowTrace; omitted members were not requested.")
model ProjectedSlowTrace { ...OptionalProperties<SlowTrace>; }

@doc("A field projection of LogHit; omitted members were not requested.")
model ProjectedLogHit { ...OptionalProperties<LogHit>; }

@doc("A field projection of Change; omitted members were not requested.")
model ProjectedChange { ...OptionalProperties<Change>; }
