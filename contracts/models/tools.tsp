import "../common/telemetry-query.tsp";
import "../otel/span.tsp";
using Nami.Api.Contracts.Common;
using TypeSpec.JsonSchema;
namespace Nami.Api.Contracts.Tools;

@doc("List every service that has sent telemetry to nami, and for each of its three signals (traces, metrics, logs) whether it is active or silent. A signal is active when something arrived at or after since (default: the last hour) and silent otherwise; a silent signal carries silent_for and, if it was ever seen, last_seen. Call this first: it tells you which services exist (the values service accepts in every nami tool) and whether missing evidence means nothing happened or nothing was sent. nami stores metrics but has no tool that reads metric values. Rows are ordered by service name.")
@jsonSchema
model SourcesStatusInput extends ToolReadInput {}
@doc("Recorded service and signal activity.")
@jsonSchema
model SourcesStatusResult extends ToolResult {
  services: ProjectedSourceStatus[];
}
@doc("A service's telemetry signals.")
model SourceStatus {
  service: string;
  signals: SignalStatus[];
}
// Spread into both signal states; an alias of a model expression, so nothing is generated for it.
alias SignalStatusFields = {
  signal: "traces" | "metrics" | "logs";
};
model ActiveSignalStatus {
  ...SignalStatusFields;
  kind: "active";
  @encodedName("application/json", "last_seen") lastSeen: Timestamp;
}
model SilentSignalStatus {
  ...SignalStatusFields;
  kind: "silent";
  @encodedName("application/json", "last_seen") lastSeen?: Timestamp;
  @encodedName("application/json", "silent_for") silentFor: IsoDuration;
}
@doc("The latest receipt time for one signal, or how long it has been silent.")
@discriminated(#{ envelope: "none", discriminatorPropertyName: "kind" })
union SignalStatus {
  active: ActiveSignalStatus,
  silent: SilentSignalStatus,
}

@doc("Group the errors recorded in the window (since, default the last hour) by error type and operation, most frequent first, with first and last occurrence, a count and one example trace ID. Three things count as an error: a log record named exception, an exception event on a span, and a span with status error that recorded no exception. A plain log line at ERROR severity is not an error here; find those with logs_search. No stack traces are returned: pass example_trace_id to traces_get to read the exception event and the spans around it. operation is the span name, or log when the log record has no span.")
@jsonSchema
model ErrorsRecentInput extends ToolReadInput {}
@doc("Groups of errors from log events, span events and error-status spans.")
@jsonSchema
model ErrorsRecentResult extends ToolResult {
  groups: ProjectedErrorGroup[];
}
@doc("A deduplicated error group and one correlated example when available.")
model ErrorGroup {
  @encodedName("application/json", "error_type") errorType: string;
  operation: string;
  @encodedName("application/json", "first_seen") firstSeen: Timestamp;
  @encodedName("application/json", "last_seen") lastSeen: Timestamp;
  count: Count;
  @encodedName("application/json", "example_trace_id") exampleTraceId?: TraceId;
  @doc("Where the errors were recorded, the first that applies to any of them: log_event, a log record named exception; span_event, an exception event on a span; span_status, a span with status error and no exception recorded for it.")
  source: "log_event" | "span_event" | "span_status";
}

@doc("Read one trace by its 32-character lowercase hex trace ID: its root span, duration, services, error flag and every span with attributes, events (including exception events) and links. Use it after errors_recent, traces_slowest or logs_search gave you a trace ID. The trace is read whole whatever since says, up to the snapshot; with service, it is found only when one of its spans is from that service. Spans are ordered by start time and paged: when next_cursor is present, call again with it to read the next spans. To keep the answer small, pass fields, for example [\"trace_id\",\"root_span\",\"span_count\",\"duration_ns\",\"has_error\"] without spans.")
@jsonSchema
model TracesGetInput extends ToolReadInput {
  @encodedName("application/json", "trace_id") traceId: TraceId;
}
@doc("A projected trace. An omitted field was not requested; it is not a fabricated value.")
model ProjectedTrace {
  ...OptionalProperties<Nami.Api.Contracts.OTel.Traces.Trace>;
}
@doc("The requested trace and evidence completeness.")
@jsonSchema
model TracesGetResult extends ToolResult {
  trace: ProjectedTrace;
}

@doc("Rank the traces that started in the window (since, default the last hour) by duration, longest first, and return up to limit of them (default 10, at most 100) with trace ID, root operation, duration in milliseconds, start time and status (error if any span failed). Use it for latency questions; for failures start with errors_recent. operation keeps only traces whose root span has exactly this name (case-sensitive; take names from an earlier traces_slowest or traces_get answer). Pass a trace_id from the answer to traces_get for the spans.")
@jsonSchema
model TracesSlowestInput extends ToolReadInput {
  @doc("Exact, case-sensitive name of the trace's root span, for example GET /orders/{id}. Omit for all operations.")
  @minLength(1) @maxLength(1024) operation?: string;

  @doc("How many of the slowest traces to return, 1 to 100; default 10. freshness.matched still counts every matching trace, and truncated is true when the limit left some out.")
  @minValue(1) @maxValue(100) limit?: int32 = 10;
}
@doc("Traces ranked by descending duration.")
@jsonSchema
model TracesSlowestResult extends ToolResult {
  traces: ProjectedSlowTrace[];
}
@doc("One trace in a duration ranking.")
model SlowTrace {
  @encodedName("application/json", "trace_id") traceId: TraceId;
  operation: string;
  @encodedName("application/json", "duration_ms") durationMs: DurationMs;
  @encodedName("application/json", "started_at") startedAt: Timestamp;
  @doc("error when any span of the trace has status error, otherwise ok when any has status ok, otherwise unset.")
  status: "unset" | "ok" | "error";
}

@doc("Find log records whose body contains query as literal, case-sensitive text (not a regular expression, no wildcards), newest first, in the window (since, default the last hour). Only the body is searched, not attributes, severity or the service name; narrow by service with service. Each record has its timestamp, severity, body, service and, when the log was written inside a span, trace_id and span_id for traces_get. Prefer several short, distinctive queries (an error code, an exception class name) over one long sentence.")
@jsonSchema
model LogsSearchInput extends ToolReadInput {
  @doc("Literal, case-sensitive text to find in log bodies, 1 to 4096 characters, for example OutOfMemoryException or order 4711.")
  @minLength(1) @maxLength(4096) query: string;
}
@doc("Log records containing the literal query.")
@jsonSchema
model LogsSearchResult extends ToolResult {
  records: ProjectedLogHit[];
}
@doc("A matching log with optional trace correlation.")
model LogHit {
  timestamp: Timestamp;
  severity: string;
  body: string;
  @encodedName("application/json", "trace_id") traceId?: TraceId;
  @encodedName("application/json", "span_id") spanId?: SpanId;
  service: string;
}

@doc("List the deploy-like changes nami saw in the resource attributes services send, newest first, in the window (since, default the last hour): a new commit (vcs.ref.head.revision), a new service.version, a new deployment.environment.name, or a changed cicd.* attribute. Use it to ask whether something changed shortly before errors or latency began. It reads only what the services reported; it does not ask a deployment system. from is absent when this is the first value nami saw for that service, which is not necessarily a deploy.")
@jsonSchema
model ChangesRecentInput extends ToolReadInput {}
@doc("Observed revision, version, environment and pipeline changes.")
@jsonSchema
model ChangesRecentResult extends ToolResult {
  changes: ProjectedChange[];
}
@doc("A resource attribute transition observed on ingestion.")
model Change {
  @encodedName("application/json", "observed_at") observedAt: Timestamp;
  service: string;
  @doc("The resource attribute that changed, named in source_key: revision is vcs.ref.head.revision, version is service.version, environment is deployment.environment.name, pipeline is any cicd.* attribute.")
  kind: "revision" | "version" | "environment" | "pipeline";
  from?: string;
  to: string;
  @encodedName("application/json", "source_key") sourceKey: string;
}

@doc("A field projection of SourceStatus; omitted members were not requested.")
model ProjectedSourceStatus {
  ...OptionalProperties<SourceStatus>;
}

@doc("A field projection of ErrorGroup; omitted members were not requested.")
model ProjectedErrorGroup {
  ...OptionalProperties<ErrorGroup>;
}

@doc("A field projection of SlowTrace; omitted members were not requested.")
model ProjectedSlowTrace {
  ...OptionalProperties<SlowTrace>;
}

@doc("A field projection of LogHit; omitted members were not requested.")
model ProjectedLogHit {
  ...OptionalProperties<LogHit>;
}

@doc("A field projection of Change; omitted members were not requested.")
model ProjectedChange {
  ...OptionalProperties<Change>;
}
