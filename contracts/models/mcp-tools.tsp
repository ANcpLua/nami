import "./mcp-analysis.tsp";
import "../common/telemetry-query.tsp";
import "../common/errors.tsp";
import "../common/types.tsp";
import "../otel/logs.tsp";
import "../otel/span.tsp";
import "./session.tsp";
import "./metrics.tsp";
import "./metric-points.tsp";
import "./annotations.tsp";
import "./saved-queries.tsp";
import "../common/resource.tsp";

using TypeSpec.OpenAPI;
using Nami.Api.Contracts.Common.Errors;
using Nami.Api.Contracts.Common;
using Nami.Api.Contracts.Common.Pagination;
using Nami.Api.Contracts.Annotations;
using Nami.Api.Contracts.SavedQueries;
using Nami.Api.Contracts.OTel.Metrics;
using Nami.Api.Contracts.Domains.Observe.Session;
using Nami.Api.Contracts.OTel.Logs;
using Nami.Api.Contracts.OTel.Traces;

namespace Nami.Api.Contracts.Mcp.Tools;

@doc("""
  OAuth scopes of the nami MCP server. `nami:read` alone keeps the read-only
  tool surface; `nami:write` adds the create and update tools; `nami:delete`
  adds the one delete tool. No scope deletes telemetry.
  """)
enum McpScope {
  read: "nami:read",
  write: "nami:write",
  delete: "nami:delete",
}

@doc("""
  The `service.name` nami's own components report their telemetry under. The
  nami MCP server exports its spans, metrics and logs as `mcp` into the project
  it reads, beside the caller's telemetry; triage and check_telemetry_health
  leave that service out unless `service_name` names it.
  """)
enum NamiService {
  mcp: "nami.mcp",
}

@doc("Stable failure code of a nami MCP tool result. A model branches on the code, never on the message.")
union ToolErrorCode {
  invalidArgument: "invalid_argument",
  notFound: "not_found",
  noProjectAccess: "no_project_access",
  collectorUnreachable: "collector_unreachable",
  collectorTimeout: "collector_timeout",
  cancelled: "cancelled",
  collectorUnauthorized: "collector_unauthorized",
  collectorRateLimited: "collector_rate_limited",
  collectorRejected: "collector_rejected",
  collectorError: "collector_error",
  contractMismatch: "contract_mismatch",

  @doc("The Collector serves another contract revision than this MCP server was generated from, as during a deploy of one of the two; `collector_contract_revision` and `server_contract_revision` name both. Retry once both serve one revision.")
  collectorContractMismatch: "collector_contract_mismatch",

  @doc("The Collector does not answer its health check, as while it restarts during a deploy, so the call was answered at once instead of waiting out the request timeout. Repeat it after `retry_after_seconds`.")
  collectorUnavailable: "collector_unavailable",

  @doc("One complete original record exceeds the MCP response budget. It remains available through the exact HTTP API; it was not truncated or rewritten.")
  responseTooLarge: "response_too_large",

  @doc("The token lacks the scope the tool needs: nami:write or nami:delete.")
  insufficientScope: "insufficient_scope",

  @doc("HTTP 409: the change conflicts with existing state, e.g. a duplicate saved-query name.")
  conflict: "conflict",

  @doc("HTTP 412: the etag is stale; re-read the resource and retry with its current etag.")
  preconditionFailed: "precondition_failed",

  @doc("HTTP 428: the request carried no etag.")
  preconditionRequired: "precondition_required",

  internal: "internal",
}

@doc("A failed tool call, carried in structuredContent.error.")
model ToolError {
  @doc("Every rule the arguments broke, as the input schema or the Collector reported them.")
  fields?: ValidationErrorDetail[];
  @encodedName("application/json", "problem_type")
  problemType?: ProblemType;

  code: ToolErrorCode;

  @doc("Human-readable description of this failure.")
  message: string;

  @doc("Whether the same call can succeed when repeated unchanged.")
  retryable: boolean;

  @doc("The argument to change, by its path such as `status`, `filter.signal` or `matchers[0].op`: the first one the input schema or the Collector rejected.")
  field?: string;

  @doc("The values `field` accepts, or for an argument the tool does not take, the arguments it does; present when they form a closed set.")
  allowed?: (string | float64 | boolean)[];

  @doc("The next step: what to change, or which tool returns the ids the call needs.")
  hint?: string;

  @doc("With not_found: the kind of record that was not found, such as `trace`, `failure`, `metric` or `saved_query`.")
  @encodedName("application/json", "resource_type")
  resourceType?: string;

  @doc("With not_found: the identifier that was not found.")
  @encodedName("application/json", "resource_id")
  resourceId?: string;

  @doc("With collector_contract_mismatch: the contract revision the Collector serves; null when its health names none.")
  @encodedName("application/json", "collector_contract_revision")
  collectorContractRevision?: string | null;

  @doc("With collector_contract_mismatch: the contract revision this MCP server was generated from.")
  @encodedName("application/json", "server_contract_revision")
  serverContractRevision?: string;

  @doc("With collector_unavailable: the seconds to wait before repeating the same call, longer the longer the Collector has not answered.")
  @minValue(1)
  @encodedName("application/json", "retry_after_seconds")
  retryAfterSeconds?: int32;
}

@doc("Pagination members of a paged tool output.")
model ToolPage {
  @doc("Whether more items exist beyond this page. False only when the result is complete; `next_cursor` is present exactly when this is true.")
  @encodedName("application/json", "has_more")
  hasMore: boolean;

  @doc("Pass as `cursor` to the same tool with the same filters for the next page. Absent on the last page.")
  @encodedName("application/json", "next_cursor")
  nextCursor?: Cursor;
}

@doc("Time-range members of a tool input. Omit all three for the tool's default window ending now; the result's `window` reports the bounds that were applied, so a follow-up call can pass them as `start_time` and `end_time` to keep the same window.")
model ToolTimeRange {
  @doc("Minutes before `end_time` (or now) when `start_time` is omitted, at most 43200 (30 days). Default 60, unless the tool's input description names another default.")
  @minValue(1) @maxValue(43200)
  @encodedName("application/json", "lookback_minutes")
  lookbackMinutes?: int32;

  @doc("Start of the window (inclusive), RFC 3339; takes precedence over `lookback_minutes`.")
  @encodedName("application/json", "start_time")
  startTime?: Nami.Api.Contracts.Common.Timestamp;

  @doc("End of the window (exclusive), RFC 3339; default now.")
  @encodedName("application/json", "end_time")
  endTime?: Nami.Api.Contracts.Common.Timestamp;
}

@doc("Finds executions: one compact row per trace that started in the window, with its root service and span name, start, duration in ms, span and error counts, services, failure ids and session. Use it to get the trace ids that get_trace, compare_traces and get_failure_context take; aggregate_telemetry computes numbers per group and compare_groups explains what differs between two populations. Default: the last 60 minutes, 10 traces per page, newest first.")
model ListTracesInput {
  @doc("Keep traces with an error span of this failure class, the classification get_mcp_stats reports.")
  @encodedName("application/json", "failure_class")
  failureClass?: McpFailureClass;
  @doc("Keep traces with a span whose `mcp.server.name`, or service name, is this value.")
  @encodedName("application/json", "server_name")
  serverName?: string;
  @doc("Keep traces with a span of this tool (`gen_ai.tool.name`).")
  @encodedName("application/json", "tool_name")
  toolName?: string;
  @doc("Keep traces with a span whose `mcp.transport`, or `network.transport`, is this value.")
  transport?: string;

  @doc("Keep traces with a span in this session.")
  @encodedName("application/json", "session_id")
  sessionId?: SessionId;
  @doc("Keep traces whose root span has exactly this name.")
  @encodedName("application/json", "root_span_name")
  rootSpanName?: string;
  @doc("Keep traces lasting at least this many milliseconds, from the earliest span start to the latest span end.")
  @encodedName("application/json", "min_duration_ms")
  minDurationMs?: DurationMs;
  @doc("Keep traces lasting at most this many milliseconds.")
  @encodedName("application/json", "max_duration_ms")
  maxDurationMs?: DurationMs;
  @doc("Keep traces with a span of this release (resource `service.version`). With `service_name` or `matchers`, one span must satisfy all of them.")
  release?: string;
  @doc("Keep traces in which this failure occurred, e.g. a `failure_id` from triage.")
  @encodedName("application/json", "failure_id")
  failureId?: FailureId;
  @doc("Keep traces with an annotation carrying this label, on the trace or one of its spans.")
  @encodedName("application/json", "annotation_label")
  annotationLabel?: string;
  @doc("Order of the traces: `start_time` (default) newest first, `duration` longest first, `errors` most error spans first. Equal values keep the newest trace first.")
  @encodedName("application/json", "order_by")
  orderBy?: "start_time" | "duration" | "errors";

  @doc("Attribute predicates one span of the trace must all satisfy, together with `service_name` and `release`.")
  @maxItems(32)
  matchers?: TelemetryMatcher[];

  @doc("Traces per page, 1 to 100; default 10.")
  @minValue(1)
  @maxValue(100)
  limit?: int32 = 10;

  ...ToolTimeRange;

  @doc("Keep traces with at least one span from this service.")
  @encodedName("application/json", "service_name")
  serviceName?: string;

  @doc("Keep only traces with at least one error span.")
  @encodedName("application/json", "errors_only")
  errorsOnly?: boolean;

  @doc("`next_cursor` of a previous list_traces call with the same filters.")
  cursor?: Cursor;
}

@doc("Structured output for list_traces.")
model ListTracesOutput {
  ...ToolReadContext;

  traces: ToolTraceRow[];
  ...ToolPage;

  @doc("Set when the call failed.")
  error?: ToolError;
}

@doc("Reads one trace: its time range, span and error counts, services, the spans `selection` picks as compact rows (service, name, start, duration in ms, status, attributes), each service's resource once, links, annotations and optionally correlated logs. Use it on a trace id from list_traces, triage or get_failure_context; get_failure_context already explains a failed trace and compare_traces compares two. The whole trace is read whatever the time range; its spans page by `max_spans`, default 100.")
model GetTraceInput {
  @doc("`next_cursor` of a previous get_trace call for the same trace and arguments; continues its spans and its other paged lists.")
  cursor?: Cursor;

  @doc("Which spans to return: `all` (default), `errors` (error status only), `errors_with_ancestors` (error spans plus every span on their path to the root) or `critical_path` (from the root, each span's longest child, each with `self_time_ms`). Start with `errors_with_ancestors` when diagnosing a failure and widen to `all` only when the error path is not enough.")
  selection?: "all" | "errors" | "errors_with_ancestors" | "critical_path";

  @doc("Attribute keys to keep on each returned span; omit for every recorded attribute.")
  attributes?: string[];

  @doc("Most correlated log records to include, 0 to 50. Default 0, no logs; search_logs with the same `trace_id` returns the rest.")
  @minValue(0) @maxValue(50)
  @encodedName("application/json", "max_logs")
  maxLogs?: int32;

  ...ToolTimeRange;

  @doc("The trace to read, 32 lowercase hex characters, e.g. from list_traces.")
  @encodedName("application/json", "trace_id")
  traceId: TraceId;

  @doc("Maximum spans per page after `selection` is applied. When more spans match, `has_more` is true and `next_cursor` continues the same trace; `page_totals` counts every selected span.")
  @encodedName("application/json", "max_spans")
  @minValue(1)
  @maxValue(1000)
  maxSpans?: int32 = 100;

  @doc("Include each span's attributes and each service's resource attributes; default `true`. `false` returns the spans without them.")
  @encodedName("application/json", "include_attributes")
  includeAttributes?: boolean = true;
}

@doc("Structured output for get_trace.")
model GetTraceOutput {
  ...ToolPage;
  @encodedName("application/json", "missing_parent_span_ids")
  missingParentSpanIds?: SpanId[];
  links?: TraceLinkAvailability[];
  @doc("The trace's log records, at most `max_logs` of them, newest first.")
  logs?: ToolLogRow[];
  @doc("True once the trace's last span ended at least five seconds before the read, so late spans have arrived; false while the trace may still be receiving spans, so re-read it before concluding.")
  settled?: boolean;

  ...ToolReadContext;

  trace: ToolTrace;

  @doc("Annotations on the trace and its spans, newest first.")
  annotations?: Annotation[];

  @doc("Set when the call failed.")
  error?: ToolError;
}

@doc("Lists the sessions, spans sharing a session id, that started in the window: span, trace and error counts, duration, services, failure ids, agent name, models and token usage. Use it to find an agent or user session; get_agent_run reconstructs one session's steps and list_traces lists single executions. Default: the last 60 minutes, 20 sessions per page, most recent activity first.")
model ListSessionsInput {
  @doc("Keep only sessions with at least one error span.")
  @encodedName("application/json", "errors_only")
  errorsOnly?: boolean;
  @doc("Keep sessions with a span of this service; with `matchers`, one span must satisfy both.")
  @encodedName("application/json", "service_name")
  serviceName?: string;
  @doc("Keep sessions whose agent (`gen_ai.agent.name`) has this name.")
  @encodedName("application/json", "agent_name")
  agentName?: string;
  @doc("Keep sessions that used this model (`gen_ai.response.model`, else `gen_ai.request.model`).")
  `model`?: string;
  @doc("Keep sessions with at least this many input plus output tokens.")
  @encodedName("application/json", "min_tokens")
  minTokens?: TokenCount;
  @doc("Keep sessions lasting at least this many milliseconds, from the first span start to the last span end.")
  @encodedName("application/json", "min_duration_ms")
  minDurationMs?: DurationMs;
  @doc("Keep sessions with an annotation carrying this label.")
  @encodedName("application/json", "annotation_label")
  annotationLabel?: string;
  @doc("Order of the sessions: `last_activity` (default) most recent first, `errors` most error spans first, `duration` longest first, `tokens` most tokens first.")
  @encodedName("application/json", "order_by")
  orderBy?: "last_activity" | "errors" | "duration" | "tokens";

  @doc("Attribute predicates one span of the session must all satisfy, together with `service_name`.")
  @maxItems(32)
  matchers?: TelemetryMatcher[];

  @doc("Sessions per page, 1 to 100; default 20.")
  @minValue(1)
  @maxValue(100)
  limit?: int32 = 20;

  @doc("Return only active sessions, those whose last span ended less than 5 minutes ago; omitted or false includes ended sessions.")
  @encodedName("application/json", "active_only")
  activeOnly?: boolean;

  ...ToolTimeRange;

  @doc("`next_cursor` of a previous list_sessions call with the same filters.")
  cursor?: Cursor;
}

@doc("Structured output for list_sessions.")
model ListSessionsOutput {
  ...ToolReadContext;

  sessions: ToolSession[];
  ...ToolPage;

  @doc("Set when the call failed.")
  error?: ToolError;
}

@doc("Searches the window's log records, newest first, by trace, span, session, service, minimum severity, body text or matchers. Use it for the logs around one execution (pass a `trace_id` from list_traces or get_trace), or with `group_by: pattern` for the repeating message templates alone; aggregate_telemetry counts logs per attribute. Default: the last 60 minutes, 50 records per page.")
model SearchLogsInput {
  @doc("Return only log records correlated with this span.")
  @encodedName("application/json", "span_id")
  spanId?: SpanId;
  @doc("`[\"pattern\"]` returns message templates (numbers, hex ids, UUIDs and quoted strings replaced by `#`) with their counts, most frequent first, instead of the records.")
  @encodedName("application/json", "group_by")
  @maxItems(1)
  groupBy?: "pattern"[];
  @doc("Also return up to this many records before and after each returned record from the same service and resource, in `context_logs`; 0 to 10, default 0. Not applied with `group_by`.")
  @minValue(0) @maxValue(10)
  context?: int32;

  @doc("Attribute predicates every returned record must satisfy; a matcher tests log attributes unless its `scope` is `resource`.")
  @maxItems(32)
  matchers?: TelemetryMatcher[];

  @doc("Return only log records correlated with this trace.")
  @encodedName("application/json", "trace_id")
  traceId?: TraceId;

  @doc("Return only log records from this service.")
  @encodedName("application/json", "service_name")
  serviceName?: string;

  @doc("Minimum OpenTelemetry severity number: 9 info, 13 warn, 17 error.")
  @encodedName("application/json", "severity_min")
  @minValue(1)
  @maxValue(24)
  severityMin?: int32;

  @doc("Case-insensitive substring of the log body.")
  query?: string;

  @doc("Records per page, or patterns with `group_by`, 1 to 200; default 50.")
  @minValue(1)
  @maxValue(200)
  limit?: int32 = 50;

  @doc("Return only log records of this session.")
  @encodedName("application/json", "session_id")
  sessionId?: SessionId;

  ...ToolTimeRange;

  @doc("`next_cursor` of a previous search_logs call with the same filters.")
  cursor?: Cursor;
}

@doc("Structured output for search_logs.")
model SearchLogsOutput {
  @doc("With `group_by: [\"pattern\"]`, the message templates, most frequent first; `coverage.matched` counts the log records they cover.")
  patterns?: LogPattern[];

  ...ToolReadContext;

  @doc("Matching records, newest first; absent when `group_by` asks for patterns.")
  logs?: ToolLogRow[];
  @doc("With `context`, the records next to each returned one from the same service and resource.")
  @encodedName("application/json", "context_logs")
  contextLogs?: ToolLogRow[];
  ...ToolPage;

  @doc("Set when the call failed.")
  error?: ToolError;
}

@doc("Outcome of one CI phase span, projected from its OpenTelemetry span status.")
enum CiPhaseStatus {
  ok: "ok",
  error: "error",
  unset: "unset",
}

@doc("Lists the CI runs recorded as telemetry by services whose name starts with `service_prefix`, newest first, and breaks one run into its phases per leg with status, duration, message and error logs. `ci_run_id` picks the run to break down (default: the newest matching run), and `baseline` compares it with the last successful run or another run, naming new failures, fixed phases and duration regressions. Use get_failure_context with the same `ci_run_id` to explain a failed run.")
model CiLogInput {
  @doc("`next_cursor` of a previous ci_log call with the same filters; continues the run list.")
  cursor?: Cursor;
  @doc("Keep runs with a span on this branch (`vcs.ref.head.name`).")
  branch?: string;
  @doc("Keep runs with a span of this commit (`vcs.ref.head.revision`).")
  commit?: string;
  @doc("Keep only the phases of this leg (`cicd.pipeline.task.name`, else the service name).")
  leg?: string;
  @doc("Keep only the phases with this span name.")
  phase?: string;
  @doc("`next_phase_cursor` of a previous call for the same run; continues its phases, `phase_limit` per page.")
  @encodedName("application/json", "phase_cursor")
  phaseCursor?: Cursor;
  @doc("Phases of the broken-down run per page, 1 to 50; default 50. A smaller page's `next_phase_cursor` resumes at the first phase it did not return.")
  @encodedName("application/json", "phase_limit")
  @minValue(1)
  @maxValue(50)
  phaseLimit?: int32 = 50;
  @doc("Keep only runs with an error span.")
  @encodedName("application/json", "errors_only")
  errorsOnly?: boolean;
  @doc("The run to compare the selected one with: `last_success`, the latest earlier run without errors, or `{ci_run_id}`, another run.")
  baseline?: CiBaseline;

  @doc("Attribute predicates one span of a run in the window must satisfy.")
  @maxItems(32)
  matchers?: TelemetryMatcher[];

  ...ToolTimeRange;

  @doc("The run to break into phases: a `ci_run_id` of the run list, the run's session id; an unknown run is answered with not_found naming recent runs. Omitted, the newest matching run is broken down.")
  @encodedName("application/json", "ci_run_id")
  ciRunId?: SessionId;

  @doc("Case-sensitive service-name prefix identifying CI telemetry in both run lists and phase breakdowns.")
  @encodedName("application/json", "service_prefix")
  @minLength(1)
  @maxLength(256)
  servicePrefix?: string = "nami-ci";

  @doc("Runs per page, 1 to 50; default 10. The broken-down run's phases page apart from them, by `phase_limit`.")
  @minValue(1)
  @maxValue(50)
  limit?: int32 = 10;
}

@doc("One CI run, summarized from the session its telemetry is grouped under; its `ci_run_id` is that session's id.")
model CiRunSummary {
  @encodedName("application/json", "provider_run_id")
  providerRunId?: string;
  @encodedName("application/json", "provider_run_url")
  providerRunUrl?: UrlString;
  attempt?: int32;
  attempts?: CiAttempt[];

  @encodedName("application/json", "ci_run_id")
  ciRunId: SessionId;

  state: SessionState;

  @encodedName("application/json", "start_time")
  startTime: Nami.Api.Contracts.Common.Timestamp;

  @encodedName("application/json", "duration_ms")
  durationMs?: DurationMs;

  @doc("Spans in the run that ended with an error status.")
  @encodedName("application/json", "error_count")
  @minValue(0)
  errorCount: int32;

  @doc("Service names observed in the run, at least one of which is CI-emitted.")
  services: string[];
}

@doc("One phase span of a CI run, attributed to the leg that executed it.")
model CiPhase {
  @encodedName("application/json", "span_id")
  spanId?: SpanId;
  @encodedName("application/json", "trace_ids")
  traceIds?: TraceId[];
  @encodedName("application/json", "error_log_count")
  errorLogCount?: Nami.Api.Contracts.Common.JsonInteger;
  @encodedName("application/json", "error_logs")
  errorLogs?: CiErrorExcerpt[];

  @doc("The leg that executed the phase: the span's `cicd.pipeline.task.name`, else its service name.")
  leg: string;

  @doc("Span name, which the CI emitter convention sets to the phase name.")
  phase: string;

  status: CiPhaseStatus;

  @encodedName("application/json", "duration_ms")
  durationMs: DurationMs;

  @doc("Span status message, present on a failed phase.")
  message?: string;
}

@doc("Structured output for ci_log: recent runs, or one run's phases.")
model CiLogOutput {
  @encodedName("application/json", "next_phase_cursor")
  nextPhaseCursor?: Cursor;
  ...ToolPage;
  comparison?: CiComparison;

  ...ToolReadContext;

  runs?: CiRunSummary[];

  @doc("The run broken into `phases`.")
  @encodedName("application/json", "ci_run_id")
  ciRunId?: SessionId;

  phases?: CiPhase[];

  @doc("Set when the call failed.")
  error?: ToolError;
}

// -----------------------------------------------------------------------------
// Metric tools: list_metrics, query_metric
// -----------------------------------------------------------------------------

@doc("Lists the recorded metrics, one row per descriptor, its name, kind, unit, temporality and monotonicity, across every resource and scope version that reports it: reporting services, series count, last seen, the aggregations query_metric accepts, whether the name is also recorded with another kind, unit or monotonicity, each attribute key's cardinality and the keys worth grouping by. Start a metric question here, then measure the metric with query_metric, split by its `suggested_group_by`. Default: 50 rows per page, ordered by name.")
model ListMetricsInput {
  @doc("Case-insensitive substring of the metric name or description.")
  query?: string;
  @doc("Keep the series this service reports.")
  @encodedName("application/json", "service_name")
  serviceName?: string;
  @doc("Keep metrics of this kind.")
  kind?: MetricKind;
  @doc("Keep metrics with exactly this unit, e.g. `s` or `By`.")
  unit?: string;

  @doc("Attribute predicates a series must satisfy to be counted; `scope: resource` tests its resource attributes.")
  @maxItems(32)
  matchers?: TelemetryMatcher[];

  ...ToolTimeRange;

  @doc("Return only names starting with this prefix (case-sensitive), e.g. `http.server.`.")
  @encodedName("application/json", "name_prefix")
  namePrefix?: string;

  @doc("Rows per page, 1 to 1000; default 50.")
  @minValue(1)
  @maxValue(1000)
  limit?: int32 = 50;

  @doc("`next_cursor` of a previous list_metrics call with the same filters.")
  cursor?: Cursor;
}

@doc("Structured output for list_metrics.")
model ListMetricsOutput {
  ...ToolReadContext;

  items: MetricCatalogRow[];
  ...ToolPage;

  @doc("Set when the call failed.")
  error?: ToolError;
}

@doc("Aggregates one metric instrument over the window: per series, the buckets that hold points and one value for the whole window, split by `group_by`, transformed by `function`, compared with an earlier window by `baseline` or divided by another metric by `ratio_of`; with `raw` it returns one series' original points instead, with their exact values, histogram buckets and exemplars. Metric values are pre-aggregated in the instrument's unit, often seconds, and histogram percentiles are interpolated; aggregate_telemetry gives exact per-span durations in ms. Without `step_ms` the step is the whole seconds that split the window into at most 60 buckets; the default window is the last 60 minutes.")
model QueryMetricInput {
  @doc("Transform of each series' points before the buckets aggregate them: `none` (default) the recorded values, `delta` the change from the previous point, `increase` the changes summed per bucket, `rate` the change per second of the interval each point recorded. A histogram's points already are the changes of their intervals, so a histogram takes a function only with aggregation `count` or `sum`, and its rate is what each series recorded over the time it recorded it, summed across series. Each series is read by its own temporality, so a delta series beside a cumulative one merges; a sum's recorded values are then totals beside changes, so it takes `delta`, `increase` or `rate`. A count is in unit `1`; a rate appends `/s`.")
  function?: "none" | "rate" | "increase" | "delta";
  @doc("Also aggregate an earlier window into `comparison`: `previous_window`, the window of equal length just before; a `{start_time, end_time}` window; or `{offset_minutes}`, the selected window shifted back.")
  baseline?: AnalysisBaseline;
  @doc("Divide each series, per bucket and for the whole window, by the series with the same `group_by` values of another metric; the result's unit is `1`.")
  @encodedName("application/json", "ratio_of")
  ratioOf?: MetricRatio;

  @doc("The metric to query, e.g. from list_metrics.")
  @encodedName("application/json", "metric_name")
  @minLength(1)
  metricName: string;

  ...ToolTimeRange;

  @doc("Bucket width in integer milliseconds; the window may hold at most 10000 buckets. Omit for whole seconds that split the window into at most 60 buckets.")
  @encodedName("application/json", "step_ms")
  @minValue(1)
  @maxValue(86400000)
  stepMs?: Nami.Api.Contracts.Common.JsonInteger;

  @doc("Reducer of the points in each bucket and in the whole window: `avg` (default), `min`, `max`, `sum`, `count`, `last`, or `p50`, `p90`, `p95` and `p99` for histograms. list_metrics shows each metric's `supported_aggregations`.")
  aggregation?: MetricAggregation = MetricAggregation.avg;

  @doc("Select one exact metric series; disambiguates scopes and conflicting instrument descriptors. A conflict error and a `raw` read that matches several series name their series ids.")
  @encodedName("application/json", "series_id")
  @minLength(1)
  seriesId?: string;

  @doc("Return the original points of one series instead of aggregating: exact values, histogram buckets and exemplars, newest first, `limit` per page. The series is `series_id`, or the one series that `metric_name` and `matchers` select; `function`, `aggregation`, `group_by`, `step_ms`, `baseline` and `ratio_of` do not apply.")
  raw?: boolean;

  @doc("With `raw`, the `next_cursor` of the previous page of the same series.")
  cursor?: Cursor;

  @doc("Attribute keys to split the result by: one series per distinct combination of their values.")
  @encodedName("application/json", "group_by")
  @maxItems(16)
  groupBy?: string[];

  @doc("Attribute matchers a series must all satisfy; serialized to `attr` and `attr_prefix`.")
  @maxItems(32)
  matchers?: MetricAttributeMatcher[];

  @doc("Result series per answer, 1 to 500, default 50, and `coverage.truncated` reports that more existed; with `raw`, original points per page.")
  @minValue(1)
  @maxValue(500)
  limit?: int32 = 50;
}

@doc("One metric instrument's series aggregated over a window: query_metric's answer without `raw`, and a saved metrics query's.")
model MetricAnswer {
  @doc("The instrument queried.")
  name: string;

  kind: MetricKind;

  @doc("Unit of the values: the instrument's, `1` for a count or a ratio, with `/s` appended for a rate.")
  unit?: string;

  @doc("The reducer applied to each bucket and to the whole window.")
  aggregation: MetricAggregation;

  @doc("Bucket width in milliseconds.")
  @encodedName("application/json", "step_ms")
  stepMs: Nami.Api.Contracts.Common.JsonInteger;

  @doc("One stream per `group_by` combination: its value for the whole window and the buckets that hold points.")
  series: MetricSeriesResult[];

  @doc("Aggregates are float64 projections; `raw` reads the exact original values.")
  @encodedName("application/json", "numeric_representation")
  numericRepresentation: "float64";

  @doc("Whether the reducer estimates or interpolates, as histogram percentiles do.")
  approximate: boolean;

  @doc("How the values were computed from the points.")
  method?: string;

  @doc("Traces of exemplars recorded in the window, at most three.")
  @encodedName("application/json", "exemplar_trace_ids")
  exemplarTraceIds?: TraceId[];
}

@doc("Structured output for query_metric: the aggregated series, or with `raw` one series' original points, which keep `name`, `kind` and `unit` and leave the aggregate's other members out.")
model QueryMetricOutput {
  ...ToolReadContext;
  ...PickProperties<MetricAnswer, "name" | "kind" | "unit">;
  ...OptionalProperties<OmitProperties<MetricAnswer, "name" | "kind" | "unit">>;

  comparison?: MetricComparisonResult;

  @doc("With `raw`, the series read.")
  @encodedName("application/json", "series_id")
  seriesId?: string;

  @doc("With `raw`, the attributes that distinguish the series.")
  attributes?: Attribute[];

  @doc("With `raw`, the series' original points in the window, newest first.")
  points?: MetricRawPoint[];

  @doc("With `raw`, whether more points follow; `next_cursor` continues them.")
  @encodedName("application/json", "has_more")
  hasMore?: boolean;

  @doc("With `raw`, pass as `cursor` with the same arguments for the next page of points.")
  @encodedName("application/json", "next_cursor")
  nextCursor?: Cursor;

  @doc("Set when the call failed.")
  error?: ToolError;
}

// -----------------------------------------------------------------------------
// Write tools (scope nami:write; delete needs nami:delete)
// -----------------------------------------------------------------------------

@doc("Attaches a note to a trace, span, session or failure, or with `id` and its current `etag` changes a note's text, labels or classification; the telemetry is not changed. get_trace and list_sessions return the notes on traces, spans and sessions, get_failure_context the notes on a failure and its traces, triage counts them per failure, and `annotation_label` filters list_traces and list_sessions by label. Needs the nami:write scope; a stale etag is refused, so re-read the note and retry.")
model AnnotateInput {
  @doc("The annotation to change, from get_trace, list_sessions or get_failure_context; omit to create one.")
  id?: ResourceId;

  @doc("With `id`: the annotation's current `etag`, sent as If-Match.")
  etag?: ETag;

  @doc("What the note is attached to, required to create: a `trace`, `span`, `session` or `failure` recorded in the project. Fixed at creation.")
  target?: AnnotationTarget;

  @doc("The note, plain text, 1 to 2000 characters; required to create.")
  @minLength(1)
  @maxLength(2000)
  text?: string;

  @doc("Up to 10 labels of 1 to 64 characters each; on a change they replace the old ones. `annotation_label` filters by them.")
  @maxItems(10)
  labels?: AnnotationLabel[];

  @doc("What the note records: `note` (the default on a create), `hypothesis`, `finding` or `rejected`.")
  classification?: AnnotationClassification;

  @doc("Without `id`: a retry key, 1 to 255 characters. Omitted, an identical create within 24 hours returns the annotation the first one created, as it is now, or a new one once that was deleted; send a new key to create another identical one. A key reused with a different body is refused while its annotation exists.")
  @minLength(1) @maxLength(255)
  @encodedName("application/json", "idempotency_key")
  idempotencyKey?: string;
}

@doc("Structured output for annotate.")
model AnnotationOutput {
  annotation?: Annotation;

  @doc("Set when the call failed.")
  error?: ToolError;
}

@doc("Saves a named filter over traces, logs or metrics so a question can be asked again; it stores the filter, never results, and run_saved_query answers it or lists the saved queries. Without `id` it creates a saved query from `name` and `filter`; with `id` and its current `etag` it changes the fields sent, a new `filter` replacing the old one whole, and refuses a stale etag. Names are unique per project; needs the nami:write scope.")
model SaveQueryInput {
  @doc("The saved query to change, from run_saved_query; omit to create one.")
  id?: ResourceId;

  @doc("With `id`: the saved query's current `etag`, sent as If-Match.")
  etag?: ETag;

  @doc("Unique within the project, 1 to 200 characters; required to create. A duplicate is refused with conflict.")
  @minLength(1)
  @maxLength(200)
  name?: string;

  @doc("What the query answers, at most 2000 characters.")
  @maxLength(2000)
  description?: string;

  @doc("The stored filter, required to create; its `signal` (`traces`, `logs` or `metrics`) selects which fields apply.")
  filter?: SavedQueryFilter;

  @doc("Without `id`: a retry key, 1 to 255 characters. Omitted, an identical create within 24 hours returns the saved query the first one created, as it is now, or a new one once that was deleted. A key reused with a different body is refused while its saved query exists.")
  @minLength(1) @maxLength(255)
  @encodedName("application/json", "idempotency_key")
  idempotencyKey?: string;
}

@doc("Structured output for save_query.")
model SavedQueryOutput {
  @encodedName("application/json", "saved_query")
  savedQuery?: SavedQuery;

  @doc("Set when the call failed.")
  error?: ToolError;
}

@doc("Runs a saved query against current telemetry, or without `id` lists the project's saved queries ordered by name with their ids, current etags and filters. A traces query answers like list_traces, a logs query like search_logs and a metrics query like query_metric, over the saved lookback ending now (60 minutes when none was saved) unless `lookback_minutes`, `start_time` or `end_time` override it. save_query stores and changes the saved queries; an unknown id is answered with not_found.")
model RunSavedQueryInput {
  @doc("The saved query to run, from a list without `id`; omit to list the saved queries.")
  id?: ResourceId;

  @doc("Without `id`, list only the saved queries of this signal.")
  signal?: SavedQuerySignal;

  ...ToolTimeRange;

  @doc("`next_cursor` of a previous run_saved_query call with the same arguments.")
  cursor?: Cursor;

  @doc("Saved queries per page without `id`, or trace rows or log records per page of a run; 1 to 1000, default 100.")
  limit?: PageLimit = 100;
}

@doc("Structured output for run_saved_query: the saved queries, or one saved query's answer.")
model RunSavedQueryOutput {
  ...ToolReadContext;

  @doc("Without `id`: the project's saved queries ordered by name, each with its id, current etag and filter.")
  @encodedName("application/json", "saved_queries")
  savedQueries?: SavedQuery[];

  @doc("With `id`: the saved query that ran.")
  @encodedName("application/json", "saved_query")
  savedQuery?: SavedQuery;

  @doc("A traces query's rows, as list_traces returns them.")
  traces?: ToolTraceRow[];

  @doc("A logs query's records, as search_logs returns them.")
  logs?: ToolLogRow[];

  @doc("A metrics query's answer over the run's window, as query_metric returns it.")
  metric?: MetricAnswer;

  @doc("Whether more saved queries, trace rows or log records follow; `next_cursor` continues them. Absent for a metrics query.")
  @encodedName("application/json", "has_more")
  hasMore?: boolean;

  @doc("Pass as `cursor` with the same arguments for the next page. Absent on the last page.")
  @encodedName("application/json", "next_cursor")
  nextCursor?: Cursor;

  @doc("Set when the call failed.")
  error?: ToolError;
}

@doc("The user-created resource kinds the delete tool accepts.")
enum DeletableKind {
  annotation: "annotation",
  savedQuery: "saved_query",
}

@doc("Deletes one annotation or saved query, named by kind, id and current etag; traces, logs and metrics are never deletable. annotate and save_query create and change these records; deleting needs the nami:delete scope, and a stale etag is refused, so re-read the record for its current etag and retry.")
model DeleteInput {
  @doc("What `id` names: `annotation` or `saved_query`.")
  kind: DeletableKind;
  @doc("The id of the annotation or saved query.")
  id: ResourceId;

  @doc("The resource's current `etag`, sent as If-Match.")
  etag: ETag;
}

@doc("Structured output for the delete tool.")
model DeleteOutput {
  deleted: boolean;

  @doc("Set when the call failed.")
  error?: ToolError;
}
