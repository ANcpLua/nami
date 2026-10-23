import "./mcp-tools.tsp";
import "../common/telemetry-query.tsp";
using TypeSpec.OpenAPI;
using Nami.Api.Contracts.Common;
using Nami.Api.Contracts.Annotations;
using Nami.Api.Contracts.OTel.Traces;
using Nami.Api.Contracts.OTel.Metrics;
using Nami.Api.Contracts.Domains.Observe.Session;
using Nami.Api.Contracts.OTel.Logs;
namespace Nami.Api.Contracts.Mcp.Tools;

@doc("Persisted failure triage status.")
enum FailureStatus { open, resolved, ignored }

@doc("Filter for failure status.")
union FailureStatusFilter { status: FailureStatus, all: "all" }

@doc("Failure trend in the selected comparison window.")
enum FailureChange { new, worsening, ongoing, recovering, regressed }

@doc("Comparison window for change detection. Omit for `previous_window`: the window of the same length immediately before the selected one. A `window` object sets explicit `start_time` and `end_time`; an `offset_minutes` object shifts the selected window back by that many minutes.")
union AnalysisBaseline { previous: "previous_window", window: ToolWindow, offset: BaselineOffset }

@doc("What get_mcp_stats compares with: an earlier window in triage's `baseline` shape, or `{server_version}`, another server version's traffic in the same window.")
union McpStatsBaseline { previous: "previous_window", window: ToolWindow, offset: BaselineOffset, version: McpComparisonVersion }

@doc("The run ci_log compares with: `last_success`, the latest earlier run without errors, or `{ci_run_id}`, another run.")
union CiBaseline { lastSuccess: "last_success", run: CiRunReference }

@doc("One CI run, by the `ci_run_id` ci_log lists it under.")
model CiRunReference {
  @doc("A `ci_run_id` of ci_log's run list.")
  @encodedName("application/json", "ci_run_id")
  ciRunId: SessionId;
}

// Arguments every analysis tool shares: the time range, attribute matchers, page size and cursor. An alias, not a
// model: the tools spread it, so it is no tool's input and has no schema of its own.
alias AnalysisInput = {
  ...ToolTimeRange;
  @doc("Attribute predicates that every matched span, resource or log must satisfy; up to 32. Omit for no filtering. discover_attributes lists the keys and values that are recorded.")
  @maxItems(32)
  matchers?: TelemetryMatcher[];
  @doc("Items per page of each list the tool returns, 1 to 200; default 100. `has_more` and `next_cursor` continue the same query.")
  @minValue(1) @maxValue(200)
  limit?: int32 = 100;
  @doc("`next_cursor` of a previous call of this tool with the same filters; the cursor keeps the first page's window.")
  cursor?: Cursor;
};

// The shared analysis arguments plus a service and an environment, spread like AnalysisInput.
alias ServiceAnalysisInput = {
  ...AnalysisInput;
  @doc("Restrict the analysis to this service (`service.name`, exact match).")
  @encodedName("application/json", "service_name")
  serviceName?: string;
  @doc("Restrict the analysis to this environment: resource `deployment.environment.name`, or the older `deployment.environment`.")
  environment?: string;
};

@doc("A baseline: the selected window shifted back by `offset_minutes`, with the same length.")
model BaselineOffset {
  @doc("Minutes to shift the selected window back, 1 to 43200 (30 days), e.g. 10080 for the same time a week earlier.")
  @minValue(1) @maxValue(43200)
  @encodedName("application/json", "offset_minutes")
  offsetMinutes: int32;
}

@doc("FailureRelease contract.")
model FailureRelease {
  service: string;
  version: string;
  @encodedName("application/json", "first_seen")
  firstSeen: Nami.Api.Contracts.Common.Timestamp;
  revision?: string;
}

@doc("AttributeAssociation contract.")
model AttributeAssociation {
  key: string;
  value: MatcherValue;
  @encodedName("application/json", "share_a")
  shareA: float64;
  @encodedName("application/json", "share_b")
  shareB: float64;
  @encodedName("application/json", "count_a")
  countA: Nami.Api.Contracts.Common.JsonInteger;
  @encodedName("application/json", "count_b")
  countB: Nami.Api.Contracts.Common.JsonInteger;
  lift: float64 | null;
}

// What makes failing spans one failure: their service, operation, exception or error type and normalized message, and
// the id they hash to. Spread into a failure's summary and its triage row, like AnalysisInput into the tool inputs.
alias FailureFingerprint = {
  @encodedName("application/json", "failure_id")
  failureId: FailureId;
  service: string;
  @doc("The failing spans' `http.route`, else their span name.")
  operation: string;
  @doc("The failing spans' `exception.type`, else their `error.type`; absent when they carry neither.")
  @encodedName("application/json", "exception_type")
  exceptionType?: string;
  @doc("The status message with numbers, hex ids, UUIDs and quoted strings replaced by `#`; absent when the spans carry none.")
  @encodedName("application/json", "normalized_message")
  normalizedMessage?: string;
};

// A failure's recorded triage decision, which update_failure_status sets; spread like FailureFingerprint.
alias FailureState = {
  @doc("Current state version; pass it to update_failure_status.")
  etag: ETag;
  @doc("The recorded status; update_failure_status sets it.")
  status: FailureStatus;
  @doc("Who owns the failure, when update_failure_status named one.")
  assignee?: string;
  @doc("With `status: ignored`, when triage lists the failure again.")
  @encodedName("application/json", "ignore_until")
  ignoreUntil?: Nami.Api.Contracts.Common.Timestamp;
};

@doc("A failure with its recorded status over the whole retained history: one fingerprint of service, operation, exception or error type and normalized message.")
model FailureSummary {
  ...FailureFingerprint;
  ...FailureState;
  @doc("When the status was last recorded; the first occurrence while none was.")
  @encodedName("application/json", "updated_at")
  updatedAt: Nami.Api.Contracts.Common.Timestamp;
  @encodedName("application/json", "first_seen")
  firstSeen: Nami.Api.Contracts.Common.Timestamp;
  @encodedName("application/json", "last_seen")
  lastSeen: Nami.Api.Contracts.Common.Timestamp;
  count: Nami.Api.Contracts.Common.JsonInteger;
  @encodedName("application/json", "affected_traces")
  affectedTraces: Nami.Api.Contracts.Common.JsonInteger;
  @encodedName("application/json", "affected_sessions")
  affectedSessions: Nami.Api.Contracts.Common.JsonInteger;
  @encodedName("application/json", "releases_seen")
  releasesSeen: string[];
  @encodedName("application/json", "annotation_count")
  annotationCount: Nami.Api.Contracts.Common.JsonInteger;
  @maxItems(3)
  @encodedName("application/json", "example_trace_ids")
  exampleTraceIds: TraceId[];
}

@doc("Start of an investigation and the list of failures: the window's failures with their recorded status, new ones first and then by count, each labeled by how it changed against a baseline (new, worsening, ongoing, recovering, or regressed after being resolved), plus the operations whose error rate, p95 latency or throughput regressed, the releases first seen in the window and the silent services. The default window is the last 60 minutes, so pass a `lookback_minutes` or `start_time` that covers the incident; the baseline defaults to the window of equal length just before, and `status` defaults to `open`, the failures that need attention. A service is silent when its last span over the baseline and the window is older than the window's end minus 15 minutes or five of its usual intervals between spans, whichever is longer. The nami MCP server's own telemetry, service `nami.mcp`, is left out unless `service_name` names it. Explain a failure with get_failure_context and record a decision on it with update_failure_status.")
model TriageInput {
  ...OmitProperties<ServiceAnalysisInput, "limit">;
  @doc("Items per page of each list (failures, regressions, releases, silent services), 1 to 50; default 10. New failures rank first, so they lead the first page.")
  @minValue(1) @maxValue(50)
  limit?: int32 = 10;
  @doc("The window to compare with: `previous_window` (default), the window of equal length just before; a `{start_time, end_time}` window; or `{offset_minutes}`, the selected window shifted back.")
  baseline?: AnalysisBaseline;
  @doc("Which failures to list by their recorded status. `open` (default) lists the failures that need attention: recorded `open`, `resolved` ones that recurred after they were resolved (`change: regressed`) and `ignored` ones whose `ignore_until` has passed. `resolved` or `ignored` list the failures recorded with that status, `all` every failure of the window.")
  status?: FailureStatusFilter;
}

@doc("One failure of the window: its fingerprint (service, operation, exception or error type and normalized message), recorded status and how it changed against the baseline.")
model TriageFailure {
  ...FailureFingerprint;
  ...FailureState;
  change: FailureChange;
  @doc("Occurrences in the window.")
  count: Nami.Api.Contracts.Common.JsonInteger;
  @doc("Occurrences in the baseline window.")
  @encodedName("application/json", "baseline_count")
  baselineCount: Nami.Api.Contracts.Common.JsonInteger;
  @encodedName("application/json", "affected_traces")
  affectedTraces: Nami.Api.Contracts.Common.JsonInteger;
  @encodedName("application/json", "first_seen")
  firstSeen: Nami.Api.Contracts.Common.Timestamp;
  @encodedName("application/json", "last_seen")
  lastSeen: Nami.Api.Contracts.Common.Timestamp;
  @encodedName("application/json", "example_trace_id")
  exampleTraceId?: TraceId;
  @doc("The release the failing spans run on (their most common `service.version`), when it was first seen in the window.")
  @encodedName("application/json", "coinciding_release")
  coincidingRelease?: FailureRelease;
  @doc("The failure this one propagated from: in most of its traces, the failure of the earliest error span without a failing child. Equal to `failure_id` when the failure is its own cause.")
  @encodedName("application/json", "root_cause_failure_id")
  rootCauseFailureId?: FailureId;
  @doc("Annotations on the failure; get_failure_context with its `failure_id` returns them.")
  @encodedName("application/json", "annotation_count")
  annotationCount: Nami.Api.Contracts.Common.JsonInteger;
}

@doc("TriageRegression contract.")
model TriageRegression {
  service: string;
  operation: string;
  measure: "error_rate" | "p95_ms" | "throughput";
  value: float64;
  @encodedName("application/json", "baseline_value")
  baselineValue: float64;
  denominator: Nami.Api.Contracts.Common.JsonInteger;
  unit: string;
}

@doc("A service that stopped sending spans, silent by the rule TriageInput states.")
model SilentService {
  service: string;
  @encodedName("application/json", "last_seen")
  lastSeen: Nami.Api.Contracts.Common.Timestamp;
  @encodedName("application/json", "usual_rate")
  usualRate: float64 | null;
  unit: string;
}

@doc("TriageOutput contract.")
model TriageOutput {
  ...ToolReadContext;
  ...ToolPage;
  failures: TriageFailure[];
  regressions: TriageRegression[];
  releases: FailureRelease[];
  @encodedName("application/json", "silent_services")
  silentServices: SilentService[];
  error?: ToolError;
}

@doc("Explains one failed execution: its error spans with their ancestors, the root-cause span, propagation, retries and fan-out, correlated logs, the first divergence from the nearest successful execution of the same operation, similar failures and annotations. Give exactly one of trace_id, session_id, failure_id or ci_run_id; a request with none or several of them is rejected, and a failure_id examines the trace of its latest occurrence. Use it after triage names a failure; get_trace reads every span of a trace and compare_traces compares two executions you choose.")
model GetFailureContextInput {
  ...AnalysisInput;
  @doc("The failing trace, 32 lowercase hex characters. Give exactly one of trace_id, session_id, failure_id or ci_run_id.")
  @encodedName("application/json", "trace_id")
  traceId?: TraceId;
  @doc("A session whose traces hold the failure. Give exactly one of trace_id, session_id, failure_id or ci_run_id.")
  @encodedName("application/json", "session_id")
  sessionId?: SessionId;
  @doc("A failure_id from triage; the trace of its latest occurrence is examined, and the context holds the notes on the failure. Give exactly one of trace_id, session_id, failure_id or ci_run_id.")
  @encodedName("application/json", "failure_id")
  failureId?: FailureId;
  @doc("A CI run: its session id (ci_log's `ci_run_id`) or its provider run id (`cicd.pipeline.run.id`). Give exactly one of trace_id, session_id, failure_id or ci_run_id.")
  @encodedName("application/json", "ci_run_id")
  ciRunId?: SessionId;
  @doc("Maximum spans in `observed.spans` (error spans and their ancestors), or `limit` when lower; `next_cursor` continues.")
  @minValue(1) @maxValue(1000)
  @encodedName("application/json", "max_spans")
  maxSpans?: int32 = 100;
  @doc("Maximum correlated log records in `observed.logs`, or `limit` when lower; `next_cursor` continues.")
  @minValue(1) @maxValue(200)
  @encodedName("application/json", "max_logs")
  maxLogs?: int32 = 100;
}

@doc("One span of a failure's evidence: an error span, or with status ok or unset an ancestor on its path to the root.")
model EvidenceSpan {
  @encodedName("application/json", "trace_id")
  traceId: TraceId;
  @encodedName("application/json", "span_id")
  spanId: SpanId;
  @doc("Absent for a root span.")
  @encodedName("application/json", "parent_span_id")
  parentSpanId?: SpanId;
  @encodedName("application/json", "session_id")
  sessionId?: SessionId;
  service: string;
  name: string;
  @encodedName("application/json", "start_time")
  startTime: Nami.Api.Contracts.Common.Timestamp;
  @encodedName("application/json", "duration_ms")
  durationMs: DurationMs;
  status: "unset" | "ok" | "error";
  @doc("The status description, when the producer set one.")
  @encodedName("application/json", "status_message")
  statusMessage?: string;
  @doc("The span's `exception.type`, else its `error.type`; absent when it carries neither.")
  @encodedName("application/json", "exception_type")
  exceptionType?: string;
  @encodedName("application/json", "key_attributes")
  keyAttributes: Attribute[];
}

@doc("A span named by its trace and span id; a span id is unique only within its trace.")
model SpanRef {
  @encodedName("application/json", "trace_id")
  traceId: TraceId;
  @encodedName("application/json", "span_id")
  spanId: SpanId;
}

@doc("An error passed from a parent span to its failing child.")
model FailurePropagation {
  from: SpanRef;
  to: SpanRef;
}

@doc("Sibling spans of one name under one parent: the attempts of one call.")
model RetryEvidence {
  name: string;
  attempts: int32;
  spans: SpanRef[];
}

@doc("A parent span with several children in one trace.")
model FanoutEvidence {
  @doc("The parent's children.")
  count: int32;
  @encodedName("application/json", "parent_span_id")
  parentSpanId?: SpanId;
  @encodedName("application/json", "trace_id")
  traceId: TraceId;
}

@doc("What the execution shows. Every span appears once in `spans`; the other lists name spans by trace and span id.")
model FailureObserved {
  @doc("The error spans and their ancestors, each once.")
  spans: EvidenceSpan[];
  @doc("The earliest error span without a failing child: where the failure began.")
  @encodedName("application/json", "root_cause_span")
  rootCauseSpan: SpanRef | null;
  @doc("The spans with error status.")
  @encodedName("application/json", "error_spans")
  errorSpans: SpanRef[];
  propagation: FailurePropagation[];
  logs: ToolLogRow[];
  retries: RetryEvidence[];
  fanout: FanoutEvidence[];
}

@doc("Where two executions part: aligned spans that differ, or a span only `a` has (`missing`) or only `b` has (`extra`). `key` is `status`, the attribute, `duration_ms` or the unmatched span's name; `a` and `b` are each side's value.")
model TraceDivergence {
  kind: "missing" | "extra" | "status" | "attribute" | "duration";
  key: string;
  a?: AttributeValue;
  b?: AttributeValue;
  @doc("Absent for an `extra` span.")
  @encodedName("application/json", "a_span_id")
  aSpanId?: SpanId;
  @doc("Absent for a `missing` span.")
  @encodedName("application/json", "b_span_id")
  bSpanId?: SpanId;
}

@doc("One way two aligned spans differ: their `status`, an `attribute` (key is its name) or `duration_ms`, with each side's value.")
model SpanDifference {
  kind: "status" | "attribute" | "duration";
  key: string;
  @doc("Absent when side A has no attribute; null when it explicitly records null.")
  a?: AttributeValue;
  @doc("Absent when side B has no attribute; null when it explicitly records null.")
  b?: AttributeValue;
}

@doc("FailureComparison contract.")
model FailureComparison {
  @encodedName("application/json", "nearest_success_trace_id")
  nearestSuccessTraceId?: TraceId;
  @encodedName("application/json", "first_divergence")
  firstDivergence: TraceDivergence | null;
}

@doc("SimilarFailure contract.")
model SimilarFailure {
  @encodedName("application/json", "failure_id")
  failureId: FailureId;
  count: Nami.Api.Contracts.Common.JsonInteger;
  @encodedName("application/json", "first_seen")
  firstSeen: Nami.Api.Contracts.Common.Timestamp;
  releases: string[];
}

@doc("What surrounds the failure: other failures in the examined spans, regressions of the root cause's service and the notes.")
model FailureContext {
  @doc("Other failures among the examined spans; the explained failure is not among them.")
  @encodedName("application/json", "similar_failures")
  similarFailures: SimilarFailure[];
  changes: TriageRegression[];
  @doc("Annotations on the examined traces, spans and sessions and on the explained failure.")
  annotations: Annotation[];
}

@doc("GetFailureContextOutput contract.")
model GetFailureContextOutput {
  ...ToolReadContext;
  ...ToolPage;
  observed: FailureObserved;
  comparison: FailureComparison;
  context: FailureContext;
  @doc("True once the execution's last span ended at least five seconds before the read, so late spans have arrived; false while it may still be receiving spans, so re-read it before concluding.")
  settled: boolean;
  unknowns: string[];
  error?: ToolError;
}

@doc("One population of compare_groups: the request's records in `window` that satisfy `matchers`, optionally only one failure's occurrences.")
model CompareGroup {
  @doc("Attribute predicates the group's records satisfy, besides the request's `matchers`. Their keys are left out of the discriminators.")
  @maxItems(32)
  matchers?: TelemetryMatcher[];
  @doc("The group's time range; omitted, the request's window.")
  window?: ToolWindow;
  @doc("Only this failure's occurrences; needs `signal: spans`.")
  @encodedName("application/json", "failure_id")
  failureId?: FailureId;
}

@doc("Explains what differs between two populations of spans or logs: the attribute values most over-represented in group A, the operation mix, duration percentiles and whether the traffic mix or the same traffic changed. For example, group A holds a `failure_id` from triage and group B is `rest`; aggregate_telemetry computes numbers per group and list_traces finds single executions. Discriminators leave out the groups' matcher keys, a failure group's own error type, route and service, per-request ids, nearly unique keys, values no more common in A than in B and values fewer than five of group A's records carry, so a group under five records gets none; an empty group is rejected, and the results are associations, not causes.")
model CompareGroupsInput {
  ...AnalysisInput;
  @doc("The records both groups hold: `spans` or `logs`.")
  signal: "spans" | "logs";
  @doc("The population to explain.")
  @encodedName("application/json", "group_a")
  groupA: CompareGroup;
  @doc("The population to compare with: another group; `rest`, the request's other records in its window; or `previous_window`, group A's filters over the window of equal length just before group A's.")
  @encodedName("application/json", "group_b")
  groupB: CompareGroup | "rest" | "previous_window";
  @doc("Which attributes can discriminate: `span` (the records' own span or log attributes), `resource`, or `both` (default).")
  @encodedName("application/json", "attribute_scope")
  attributeScope?: "span" | "resource" | "both";
}

@doc("GroupStructure contract.")
model GroupStructure {
  name: string;
  @encodedName("application/json", "share_a")
  shareA: float64;
  @encodedName("application/json", "share_b")
  shareB: float64;
}

@doc("DurationPercentiles contract.")
model DurationPercentiles {
  @encodedName("application/json", "p50_ms")
  p50Ms: float64 | null;
  @encodedName("application/json", "p95_ms")
  p95Ms: float64 | null;
  @encodedName("application/json", "p99_ms")
  p99Ms: float64 | null;
}

@doc("GroupDurations contract.")
model GroupDurations {
  a: DurationPercentiles;
  b: DurationPercentiles;
}

@doc("GroupComposition contract.")
model GroupComposition {
  changed: boolean;
  @encodedName("application/json", "traffic_mix_changed")
  trafficMixChanged: boolean;
  @encodedName("application/json", "same_traffic_changed")
  sameTrafficChanged: boolean;
  summary: string;
}

@doc("CompareGroupsOutput contract.")
model CompareGroupsOutput {
  ...ToolReadContext;
  ...ToolPage;
  discriminators: AttributeAssociation[];
  structural: GroupStructure[];
  durations: GroupDurations;
  composition: GroupComposition;
  @encodedName("application/json", "size_a")
  sizeA: Nami.Api.Contracts.Common.JsonInteger;
  @encodedName("application/json", "size_b")
  sizeB: Nami.Api.Contracts.Common.JsonInteger;
  @encodedName("application/json", "excluded_keys")
  excludedKeys: string[];
  error?: ToolError;
}

@doc("One execution of compare_traces: a trace or a session.")
model ExecutionReference {
  @doc("The execution's trace; takes precedence over `session_id`.")
  @encodedName("application/json", "trace_id")
  traceId?: TraceId;
  @doc("The execution's session, all of its traces.")
  @encodedName("application/json", "session_id")
  sessionId?: SessionId;
}

@doc("Aligns two executions span by span (by service, name and depth) and returns their first divergence, what differs between aligned spans and the spans only one side holds. Use it to set a failing execution against a successful one you chose; get_failure_context finds the nearest success itself, and compare_groups compares populations. Both executions are read whole, whatever the time range and `matchers`; root spans with different names make them not comparable, and an unknown execution is answered with not_found.")
model CompareTracesInput {
  ...AnalysisInput;
  @doc("The first execution, usually the failing one.")
  a: ExecutionReference;
  @doc("The second execution, usually a successful one of the same operation.")
  b: ExecutionReference;
}

@doc("A span both executions hold, aligned by service, name and depth, with what differs between the two.")
model AlignedSpans {
  @encodedName("application/json", "a_span_id")
  aSpanId: SpanId;
  @encodedName("application/json", "b_span_id")
  bSpanId: SpanId;
  name: string;
  differences: SpanDifference[];
}

@doc("CompareTracesOutput contract.")
model CompareTracesOutput {
  ...ToolReadContext;
  ...ToolPage;
  @encodedName("application/json", "first_divergence")
  firstDivergence: TraceDivergence | null;
  aligned: AlignedSpans[];
  @doc("Differences of the aligned spans that start after the first divergence.")
  @encodedName("application/json", "after_divergence")
  afterDivergence: TraceDivergence[];
  @doc("Spans only one execution holds.")
  unmatched: TraceDivergence[];
  comparable: boolean;
  reason?: string;
  error?: ToolError;
}

@doc("Checks whether a fix held: counts the failure's occurrences and its operation's requests before and after the change and answers `passed`, `failed` (it recurred), `pending` (fewer than `min_requests` requests or `min_minutes` minutes since the change) or `inconclusive`. Give exactly one of `failure_id` or `matchers` (the matched spans are the requests and their errors the failures), and a `since` time or a `release` of the failure's service whose first span marks the change; `change` names the anchor used. The before window runs from the failure's first retained occurrence (with `matchers`: from the change minus the lookback) to the change, the after window from the change to now. Repeat the check later with the returned `verify_id`.")
model VerifyFixInput {
  ...AnalysisInput;
  @doc("The failure to verify; its requests are the spans of its service and operation. Give exactly one of `failure_id` or `matchers`.")
  @encodedName("application/json", "failure_id")
  failureId?: FailureId;
  @doc("When the fix took effect, RFC 3339; the after window runs from here to now. Takes precedence over `release`.")
  since?: Nami.Api.Contracts.Common.Timestamp;
  @doc("A `service.version` of the failure's service (with `matchers`, of the matched requests' services) whose first recorded span marks the change, when `since` is omitted. A service that records no such version, such as a database, takes it from the services its executions run through. list_releases lists each service's versions.")
  release?: string;
  @doc("Requests the after window needs before the verdict can be `passed`, 0 or more; default 100.")
  @minValue(0)
  @encodedName("application/json", "min_requests")
  minRequests?: int32 = 100;
  @doc("Minutes after the change before the verdict can be `passed`, 0 or more; default 30.")
  @minValue(0)
  @encodedName("application/json", "min_minutes")
  minMinutes?: int32 = 30;
  @doc("`verify_id` of an earlier verify_fix result: repeats that verification with its original arguments, which replace any others sent.")
  @encodedName("application/json", "verify_id")
  verifyId?: VerifyId;
}

@doc("One side of a verification. `before` runs from the failure's first retained occurrence, or the request's lookback when matchers select the requests, up to the change; `after` runs from the change to now.")
model VerificationPeriod {
  window: ToolWindow;
  occurrences: Nami.Api.Contracts.Common.JsonInteger;
  requests: Nami.Api.Contracts.Common.JsonInteger;
  rate: float64 | null;
  unit: "requests";
}

@doc("The change a verification is anchored on: the `since` it was given, or the first recorded span of a release.")
model VerificationChange {
  @doc("`since` for a time the request gave, `release` for the first span of a release.")
  kind: "since" | "release";
  @doc("When the change took effect; the after window starts here.")
  time: Nami.Api.Contracts.Common.Timestamp;
  @doc("With `release`: the service whose first span of `version` marks the change.")
  service?: string;
  @doc("With `release`: the release, a `service.version`.")
  version?: string;
}

@doc("What a pending verification still needs before it can pass.")
model VerificationNeeds {
  @doc("Requests still to observe after the change.")
  requests: Nami.Api.Contracts.Common.JsonInteger;
  @doc("Whole minutes still to wait, rounded up.")
  minutes: Nami.Api.Contracts.Common.JsonInteger;
}

@doc("The verdict of a fix and the before and after windows it weighs; each window states its own bounds.")
model VerifyFixOutput {
  coverage: ToolCoverage;
  @doc("Item totals by paged collection path. Omitted totals are unknown, not zero.")
  @encodedName("application/json", "page_totals")
  pageTotals?: Record<Nami.Api.Contracts.Common.JsonInteger>;
  ...ToolPage;
  verdict: "passed" | "failed" | "pending" | "inconclusive";
  change: VerificationChange;
  before: VerificationPeriod;
  after: VerificationPeriod;
  needs?: VerificationNeeds;
  @maxItems(3)
  recurrences: TraceId[];
  guardrails: TriageFailure[];
  @encodedName("application/json", "verify_id")
  verifyId: VerifyId;
  error?: ToolError;
}

@doc("Computes numbers per group of spans or logs: counts, error counts and rates, exact duration statistics in ms or sums of a numeric attribute, optionally per time bucket. Use it for how many and how slow; list_traces finds single executions, compare_groups explains what differs between two populations, and query_metric reads pre-aggregated metrics in their own unit. Default: the last 60 minutes, 20 groups per page ordered by the first measure, descending.")
model AggregateTelemetryInput {
  ...OmitProperties<AnalysisInput, "limit">;
  @doc("Groups per page in `order_by` order, 1 to 200; default 20. `other` counts the groups after this page and their records.")
  @minValue(1) @maxValue(200)
  limit?: int32 = 20;
  @doc("Which records to aggregate: `spans` (duration measures apply) or `logs`.")
  signal: "spans" | "logs";
  @doc("Attribute keys that define the groups, up to 4; omit for one overall group. A key is looked up in the record's attributes, then its resource, and `service.name` and `span.name` work too; a key no record carries is rejected. Each group's `key` echoes the values.")
  @maxItems(4)
  @encodedName("application/json", "group_by")
  groupBy?: string[];
  @doc("Measures to compute per group; at least one. Accepted values: `count`, `error_count`, `error_rate` (share from 0 to 1), `duration_avg`, `duration_p50`, `duration_p90`, `duration_p95`, `duration_p99`, `duration_max` (milliseconds, spans only) and `sum:<attribute key>` for a numeric attribute. The result's `units` names the unit of each measure. Example: [\"count\", \"error_rate\", \"duration_p95\"].")
  measures: string[];
  @doc("Bucket width in integer milliseconds for a per-group time series in `buckets`, 1 to 86400000 (one day) like query_metric's; omit for group totals only. A step at least as long as the window yields one bucket per group.")
  @minValue(1) @maxValue(86400000)
  @encodedName("application/json", "step_ms")
  stepMs?: Nami.Api.Contracts.Common.JsonInteger;
  @doc("One of the requested measures, optionally followed by ` asc` or ` desc`. Default: the first measure, descending. Examples: `error_rate`, `duration_p95 desc`, `count asc`.")
  @encodedName("application/json", "order_by")
  orderBy?: string;
}

@doc("AggregateBucket contract.")
model AggregateBucket {
  @encodedName("application/json", "start_time")
  startTime: Nami.Api.Contracts.Common.Timestamp;
  measures: Record<float64 | null>;
}

@doc("AggregateGroup contract.")
model AggregateGroup {
  @doc("Each group_by key's value; null for records that do not carry the key.")
  key: Record<MatcherValue | null>;
  measures: Record<float64 | null>;
  buckets?: AggregateBucket[];
}

@doc("AggregateOther contract.")
model AggregateOther {
  groups: Nami.Api.Contracts.Common.JsonInteger;
  count: Nami.Api.Contracts.Common.JsonInteger;
}

@doc("AggregateTelemetryOutput contract.")
model AggregateTelemetryOutput {
  ...ToolReadContext;
  ...ToolPage;
  groups: AggregateGroup[];
  other: AggregateOther;
  units: Record<string>;
  error?: ToolError;
}

@doc("Lists the attribute keys recorded in the window with their type, coverage share, cardinality, ten most common values and semantic-convention description. Call it before building `matchers`, `group_by` or `measures` for another tool, so they name keys and values that exist. Default: the last 60 minutes, 100 keys per page, ordered by key.")
model DiscoverAttributesInput {
  ...AnalysisInput;
  @doc("Whose attributes: `spans` (span and resource attributes of spans), `logs` (log and resource attributes of log records) or `resources` (resource attributes of spans).")
  signal: "spans" | "logs" | "resources";
  @doc("Return only keys starting with this prefix (case-sensitive), e.g. `http.`.")
  @encodedName("application/json", "key_prefix")
  keyPrefix?: string;
  @doc("Return only this exact key.")
  key?: string;
}

@doc("AttributeValueCount contract.")
model AttributeValueCount {
  @doc("The recorded value in the lossless attribute form: an int64 is a tagged string that keeps every digit.")
  value: AttributeValue;
  count: Nami.Api.Contracts.Common.JsonInteger;
  share: float64;
}

@doc("AttributeSemconv contract.")
model AttributeSemconv {
  brief: string;
  stability: string;
  @encodedName("application/json", "requirement_level")
  requirementLevel: string;
}

@doc("DiscoveredAttribute contract.")
model DiscoveredAttribute {
  key: string;
  scope: MatcherScope;
  type: string;
  @encodedName("application/json", "coverage_share")
  coverageShare: float64;
  @doc("Distinct values the key holds; with `coverage.scan_complete` false, among the records the read held.")
  cardinality: Nami.Api.Contracts.Common.JsonInteger;
  @maxItems(10)
  @encodedName("application/json", "top_values")
  topValues: AttributeValueCount[];
  semconv: AttributeSemconv | null;
}

@doc("DiscoverAttributesOutput contract.")
model DiscoverAttributesOutput {
  ...ToolReadContext;
  ...ToolPage;
  attributes: DiscoveredAttribute[];
  error?: ToolError;
}

@doc("Reconstructs one agent run from its session or trace: its LLM, tool, agent and retrieval steps in time order with durations, tokens and errors, plus repeated calls, failed tool calls, the slowest steps and the token share per span name. Give `session_id` or `trace_id` (list_sessions finds agent sessions); an unknown one is answered with not_found, and the run is read whole whatever the time range and `matchers`. get_trace shows every span of a trace and get_mcp_stats aggregates MCP calls across runs.")
model GetAgentRunInput {
  ...OmitProperties<AnalysisInput, "limit">;
  @doc("The agent session to reconstruct. Give `session_id` or `trace_id`; `trace_id` wins when both are sent.")
  @encodedName("application/json", "session_id")
  sessionId?: SessionId;
  @doc("One trace of an agent run.")
  @encodedName("application/json", "trace_id")
  traceId?: TraceId;
  @doc("Steps per page, and items per page of each findings list, 1 to 1000; default 100. `has_more` and `next_cursor` continue them.")
  @minValue(1) @maxValue(1000)
  limit?: int32 = 100;
}

@doc("AgentTokens contract.")
model AgentTokens {
  input?: Nami.Api.Contracts.Common.JsonInteger;
  output?: Nami.Api.Contracts.Common.JsonInteger;
  @encodedName("application/json", "cache_read")
  cacheRead?: Nami.Api.Contracts.Common.JsonInteger;
  @encodedName("application/json", "cache_write")
  cacheWrite?: Nami.Api.Contracts.Common.JsonInteger;
  reasoning?: Nami.Api.Contracts.Common.JsonInteger;
}

@doc("AgentRun contract.")
model AgentRun {
  @encodedName("application/json", "agent_name")
  agentName?: string;
  models: string[];
  started: Nami.Api.Contracts.Common.Timestamp;
  @encodedName("application/json", "duration_ms")
  durationMs: DurationMs;
  tokens: AgentTokens;
  errors: Nami.Api.Contracts.Common.JsonInteger;
  @encodedName("application/json", "session_id")
  sessionId?: SessionId;
  @encodedName("application/json", "trace_id")
  traceId?: TraceId;
}

@doc("AgentStep contract.")
model AgentStep {
  index: int32;
  kind: "llm_call" | "tool_call" | "agent" | "retrieval";
  name: string;
  @encodedName("application/json", "mcp_method")
  mcpMethod?: string;
  @encodedName("application/json", "mcp_server")
  mcpServer?: string;
  @encodedName("application/json", "duration_ms")
  durationMs: DurationMs;
  tokens: AgentTokens;
  status: "unset" | "ok" | "error";
  error?: string;
  @encodedName("application/json", "trace_id")
  traceId: TraceId;
  @encodedName("application/json", "span_id")
  spanId: SpanId;
}

@doc("RepeatedCall contract.")
model RepeatedCall {
  tool: string;
  @encodedName("application/json", "argument_hash")
  argumentHash: string;
  count: Nami.Api.Contracts.Common.JsonInteger;
}

@doc("AgentTokenShare contract.")
model AgentTokenShare {
  name: string;
  tokens: Nami.Api.Contracts.Common.JsonInteger;
  share: float64;
}

@doc("AgentFindings contract.")
model AgentFindings {
  @encodedName("application/json", "repeated_calls")
  repeatedCalls: RepeatedCall[];
  @doc("Indexes into `steps` of the tool calls that failed, at most ten, in step order.")
  @encodedName("application/json", "failed_tool_calls")
  failedToolCalls: int32[];
  @doc("Indexes into `steps` of the three slowest steps, slowest first.")
  @encodedName("application/json", "slowest_steps")
  slowestSteps: int32[];
  @encodedName("application/json", "token_share")
  tokenShare: AgentTokenShare[];
}

@doc("GetAgentRunOutput contract.")
model GetAgentRunOutput {
  ...ToolReadContext;
  ...ToolPage;
  run: AgentRun;
  steps: AgentStep[];
  findings: AgentFindings;
  unknowns: string[];
  error?: ToolError;
}

@doc("Lists each service version (`service.version`) seen in the window per service and environment: first and last seen, traffic share, whether it is current or runs concurrently with another version, error rate and p95 before and after it appeared, and the failures new with it. Its default window is the last 10080 minutes (7 days), newest release first. Use it to tie a failure or regression to a deployment; triage lists only the releases first seen in its window, and verify_fix takes a `release` as the change.")
model ListReleasesInput {
  ...ServiceAnalysisInput;
}

@doc("ReleasePerformance contract.")
model ReleasePerformance {
  @encodedName("application/json", "error_rate")
  errorRate: float64 | null;
  @encodedName("application/json", "p95_ms")
  p95Ms: float64 | null;
}

@doc("ReleaseSummary contract.")
model ReleaseSummary {
  service: string;
  environment?: string;
  version: string;
  revision?: string;
  @encodedName("application/json", "first_seen")
  firstSeen: Nami.Api.Contracts.Common.Timestamp;
  @encodedName("application/json", "last_seen")
  lastSeen: Nami.Api.Contracts.Common.Timestamp;
  current: boolean;
  concurrent: boolean;
  @encodedName("application/json", "traffic_share")
  trafficShare: float64;
  before: ReleasePerformance;
  after: ReleasePerformance;
  @encodedName("application/json", "new_failure_ids")
  newFailureIds: FailureId[];
}

@doc("ListReleasesOutput contract.")
model ListReleasesOutput {
  ...ToolReadContext;
  ...ToolPage;
  releases: ReleaseSummary[];
  error?: ToolError;
}

@doc("Returns the services and the calls between them, from parent and child spans of different services, with calls, error rate and p95 latency in ms per node and edge, plus the databases, messaging systems and external hosts the spans call. `service_name` centers the map on one service and keeps the services within `depth` calls of it. Use it to see who calls whom and where errors cross service boundaries; get_trace follows one execution.")
model GetServiceMapInput {
  ...ServiceAnalysisInput;
  @doc("With `service_name`, how many calls away from it services stay on the map, 1 to 3; default 1.")
  @minValue(1) @maxValue(3)
  depth?: int32;
  @doc("Add the databases, messaging systems and external hosts that client, producer and consumer spans name (`service.peer.name`, `server.address`, `db.system.name` or `messaging.system`); default true.")
  @encodedName("application/json", "include_external")
  includeExternal?: boolean;
}

@doc("ServiceMapNode contract.")
model ServiceMapNode {
  name: string;
  kind: "service" | "database" | "external" | "messaging";
  calls: Nami.Api.Contracts.Common.JsonInteger;
  @encodedName("application/json", "error_rate")
  errorRate: float64;
  @encodedName("application/json", "p95_ms")
  p95Ms: float64 | null;
  unit: "spans";
}

@doc("ServiceMapEdge contract.")
model ServiceMapEdge {
  from: string;
  to: string;
  calls: Nami.Api.Contracts.Common.JsonInteger;
  @encodedName("application/json", "error_rate")
  errorRate: float64;
  @encodedName("application/json", "p95_ms")
  p95Ms: float64 | null;
  @encodedName("application/json", "example_operations")
  exampleOperations: string[];
  unit: "spans";
}

@doc("GetServiceMapOutput contract.")
model GetServiceMapOutput {
  ...ToolReadContext;
  ...ToolPage;
  nodes: ServiceMapNode[];
  edges: ServiceMapEdge[];
  @encodedName("application/json", "unknown_callers")
  unknownCallers: float64;
  error?: ToolError;
}

@doc("Checks whether the telemetry can be trusted: silent services, orphan spans whose parent is missing, metric points recorded twice with different values, missing required semantic-convention attributes, the share of unknown services, schema URLs, how far back the data reaches and the logs' ingestion lag. Run it when answers look incomplete or before trusting an empty one; silent services follow the triage rule, and like triage it leaves out the nami MCP server's own telemetry, service `nami.mcp`, unless `service_name` names it. A `service_name` that sent no telemetry in the window or its baseline is answered with service.not_found, not with an empty report.")
model CheckTelemetryHealthInput {
  ...ServiceAnalysisInput;
}

@doc("Spans whose parent span nami does not retain. A span whose producer marked its parent remote (OTLP span flags) is not counted: its caller runs in another process.")
model OrphanSpans {
  count: Nami.Api.Contracts.Common.JsonInteger;
  share: float64;
  services: string[];
  unit: "spans";
}

@doc("SemconvGap contract.")
model SemconvGap {
  service: string;
  operation: string;
  attribute: string;
  @encodedName("application/json", "requirement_level")
  requirementLevel: string;
  @encodedName("application/json", "missing_share")
  missingShare: float64;
}

@doc("How far back the read's data reaches in the report's scope: the oldest span or log record of the services, environment and matchers the rest of the report covers, without `nami.mcp` unless `service_name` names it, and the days from it to now. This is not the configured retention.")
model TelemetryDataSpan {
  oldest: Nami.Api.Contracts.Common.Timestamp | null;
  days: float64 | null;
}

@doc("The conflicting points of one metric series.")
model MetricConflictSeries {
  @encodedName("application/json", "metric_name")
  metricName: string;
  @encodedName("application/json", "series_id")
  seriesId: string;
  service: string;
  @doc("Timestamps of this series in the window that hold different measurements.")
  points: Nami.Api.Contracts.Common.JsonInteger;
  @encodedName("application/json", "first_time")
  firstTime: Nami.Api.Contracts.Common.Timestamp;
  @encodedName("application/json", "last_time")
  lastTime: Nami.Api.Contracts.Common.Timestamp;
}

@doc("Metric points at which a producer recorded different measurements for one series timestamp. Each point keeps the measurement recorded first, query_metric with `raw` and the series_id returns every observation, and query_metric refuses to aggregate a window that holds one.")
model MetricConflicts {
  points: Nami.Api.Contracts.Common.JsonInteger;
  unit: "points";
  series: MetricConflictSeries[];
}

@doc("CheckTelemetryHealthOutput contract.")
model CheckTelemetryHealthOutput {
  ...ToolReadContext;
  ...ToolPage;
  @encodedName("application/json", "silent_services")
  silentServices: SilentService[];
  @encodedName("application/json", "orphan_spans")
  orphanSpans: OrphanSpans;
  @encodedName("application/json", "metric_conflicts")
  metricConflicts: MetricConflicts;
  @encodedName("application/json", "semconv_gaps")
  semconvGaps: SemconvGap[];
  @encodedName("application/json", "unknown_service_share")
  unknownServiceShare: float64;
  @encodedName("application/json", "schema_urls")
  schemaUrls: string[];
  @encodedName("application/json", "data_span")
  dataSpan: TelemetryDataSpan;
  @doc("Mean milliseconds from a log record's observed time to the Collector's receipt, over the window's records whose producer set an observed time; omitted when none did.")
  @encodedName("application/json", "ingestion_lag_ms")
  ingestionLagMs?: float64;
  error?: ToolError;
}

@doc("Another server version's traffic in the same window.")
model McpComparisonVersion {
  @doc("The server version to compare with (`mcp.server.version`, or resource `service.version`).")
  @encodedName("application/json", "server_version")
  serverVersion: string;
}

@doc("Summarizes the MCP traffic recorded as spans with `mcp.method.name`: requests, errors, error rate and latency in total and per `group_by` group, per-tool statistics, request counts per server, transport and method, and failure classes. Use it for MCP tool health across many calls; list_traces with `tool_name` or `failure_class` finds the single calls, and get_agent_run follows one agent's run. Time buckets come only with `buckets: true`, and `baseline` adds the same statistics for an earlier window or another server version.")
model GetMcpStatsInput {
  ...AnalysisInput;
  @doc("Only spans whose `mcp.server.name`, or service name, is this value.")
  @encodedName("application/json", "server_name")
  serverName?: string;
  @doc("Only spans whose `mcp.server.version`, or resource `service.version`, is this value.")
  @encodedName("application/json", "server_version")
  serverVersion?: string;
  @doc("Only spans of this tool (`gen_ai.tool.name`).")
  @encodedName("application/json", "tool_name")
  toolName?: string;
  @doc("Only spans whose `mcp.transport`, or `network.transport`, is this value.")
  transport?: string;
  @doc("Also compute the statistics for: `previous_window`, the window of equal length just before; a `{start_time, end_time}` window; `{offset_minutes}`, the selected window shifted back; or `{server_version}`, that server version's traffic in the same window.")
  baseline?: McpStatsBaseline;
  @doc("What `groups` breaks the traffic down by, one of `tool` (default), `server`, `method`, `transport` or `failure_class`.")
  @encodedName("application/json", "group_by")
  @maxItems(1)
  groupBy?: ("server" | "tool" | "method" | "transport" | "failure_class")[];
  @doc("Also return the window's request and error counts per bucket of one sixtieth of the window (at least one second), only the buckets that saw requests. Default false.")
  buckets?: boolean;
}

@doc("McpTrafficTotals contract.")
model McpTrafficTotals {
  requests: Nami.Api.Contracts.Common.JsonInteger;
  errors: Nami.Api.Contracts.Common.JsonInteger;
  @encodedName("application/json", "error_rate")
  errorRate: float64;
  @encodedName("application/json", "p50_ms")
  p50Ms: float64 | null;
  @encodedName("application/json", "p95_ms")
  p95Ms: float64 | null;
  unit: "requests";
}

@doc("McpTrafficGroup contract.")
model McpTrafficGroup {
  ...McpTrafficTotals;
  name: string;
  @maxItems(3)
  @encodedName("application/json", "example_trace_ids")
  exampleTraceIds: TraceId[];
}

@doc("McpFailureClasses contract.")
model McpFailureClasses {
  transport: Nami.Api.Contracts.Common.JsonInteger;
  protocol: Nami.Api.Contracts.Common.JsonInteger;
  @encodedName("application/json", "tool_result")
  toolResult: Nami.Api.Contracts.Common.JsonInteger;
  authentication: Nami.Api.Contracts.Common.JsonInteger;
  cancellation: Nami.Api.Contracts.Common.JsonInteger;
  timeout: Nami.Api.Contracts.Common.JsonInteger;
}

@doc("McpStatsComparisonResult contract.")
model McpStatsComparisonResult {
  window: ToolWindow;
  totals: McpTrafficTotals;
  groups: McpTrafficGroup[];
  @encodedName("application/json", "failure_classes")
  failureClasses: McpFailureClasses;
}

@doc("Requests and errors of one bucket of the window.")
model McpTrafficBucket {
  @doc("Start of the bucket (inclusive); it ends `bucket_ms` later, or at the window's end.")
  start: Nami.Api.Contracts.Common.Timestamp;

  @minValue(0)
  requests: int32;

  @minValue(0)
  errors: int32;
}

@doc("Requests counted under one name of a breakdown.")
model McpNameRequestCount {
  name: string;

  @minValue(0)
  requests: int32;
}

@doc("Requests, errors and latency of one tool.")
model McpToolStats {
  @doc("The tool's `gen_ai.tool.name`.")
  name: string;

  @minValue(0)
  requests: int32;

  @minValue(0)
  errors: int32;

  @encodedName("application/json", "error_rate")
  @minValue(0)
  @maxValue(1)
  errorRate: float64;

  @encodedName("application/json", "avg_ms")
  @minValue(0)
  avgMs: float64;

  @encodedName("application/json", "p95_ms")
  @minValue(0)
  p95Ms: float64;
}

@doc("MCP traffic of the window, computed by the Collector from the recorded MCP spans that match the filters: totals, the `group_by` groups, failure classes, time buckets, per-tool statistics and simultaneous server, transport and method breakdowns.")
model GetMcpStatsOutput {
  ...ToolReadContext;
  ...ToolPage;
  totals: McpTrafficTotals;
  groups: McpTrafficGroup[];
  @encodedName("application/json", "failure_classes")
  failureClasses: McpFailureClasses;
  comparison?: McpStatsComparisonResult;

  @doc("With `buckets`, the width of every bucket in integer milliseconds: the window split into 60 buckets of at least one second.")
  @encodedName("application/json", "bucket_ms")
  @minValue(1000)
  bucketMs?: Nami.Api.Contracts.Common.JsonInteger;

  @doc("With `buckets`, requests and errors of each bucket that saw requests, oldest first.")
  buckets?: McpTrafficBucket[];

  @doc("Requests per MCP server: `mcp.server.name`, else the service name. Most requests first.")
  @encodedName("application/json", "by_server")
  byServer: McpNameRequestCount[];

  @doc("Requests per transport: `mcp.transport`, else `network.transport`; empty when neither is recorded. Most requests first.")
  @encodedName("application/json", "by_transport")
  byTransport: McpNameRequestCount[];

  @doc("Requests per MCP method (`mcp.method.name`). Most requests first.")
  @encodedName("application/json", "by_method")
  byMethod: McpNameRequestCount[];

  @doc("Per tool (`gen_ai.tool.name`), the requests that name one: most requests first, paged with `groups`.")
  tools: McpToolStats[];

  error?: ToolError;
}

@doc("Records a triage decision on one failure: status `open`, `resolved` or `ignored`, an optional assignee and, for `ignored`, an optional `ignore_until`. Each call replaces all three and needs the failure's current `etag`; the stored failure returns with its new etag. Triage then reports a resolved failure that recurs as `regressed` and leaves an ignored one out until its `ignore_until`, if any, passes. Needs the nami:write scope; telemetry is not changed.")
model UpdateFailureStatusInput {
  @doc("The failure to update, from triage.")
  @encodedName("application/json", "failure_id")
  failureId: FailureId;
  @doc("The failure's current `etag` from triage (`status: all` lists every failure), sent as If-Match; a stale etag is refused.")
  etag: ETag;
  @doc("The new status: `open`, `resolved` or `ignored`.")
  status: FailureStatus;
  @doc("With `status: ignored`, when triage shows the failure again, RFC 3339; omitted, it stays ignored. Refused with any other status.")
  @encodedName("application/json", "ignore_until")
  ignoreUntil?: Nami.Api.Contracts.Common.Timestamp;
  @doc("Who owns the failure, at most 256 characters; omitted, the failure has no assignee afterwards.")
  @maxLength(256)
  assignee?: string;
}

@doc("UpdateFailureStatusOutput contract.")
model UpdateFailureStatusOutput {
  failure: FailureSummary;
  error?: ToolError;
}

@doc("TraceLinkAvailability contract.")
model TraceLinkAvailability {
  @encodedName("application/json", "trace_id")
  traceId: TraceId;
  @encodedName("application/json", "span_id")
  spanId: SpanId;
  @doc("Whether this project retains the linked span, in this trace or in another one.")
  available: boolean;
}

@doc("The service and name of a trace's root span.")
model TraceRoot {
  service: string;
  name: string;
}

@doc("One trace of list_traces.")
model ToolTraceRow {
  @encodedName("application/json", "trace_id")
  traceId: TraceId;
  root: TraceRoot;
  @doc("Start of the trace's earliest span.")
  @encodedName("application/json", "start_time")
  startTime: Nami.Api.Contracts.Common.Timestamp;
  @doc("From the earliest span's start to the latest span's end.")
  @encodedName("application/json", "duration_ms")
  durationMs: DurationMs;
  @encodedName("application/json", "span_count")
  spanCount: int32;
  @doc("Spans with error status.")
  @encodedName("application/json", "error_count")
  errorCount: Nami.Api.Contracts.Common.JsonInteger;
  @doc("Services with a span in the trace.")
  services: string[];
  @doc("Failures of the trace's error spans; get_failure_context explains one.")
  @maxItems(3)
  @encodedName("application/json", "failure_ids")
  failureIds: FailureId[];
  @encodedName("application/json", "session_id")
  sessionId?: SessionId;
}

@doc("One session of list_sessions; `genai_usage` comes only with GenAI requests.")
model ToolSession {
  ...SessionEntity;
  @maxItems(5)
  @encodedName("application/json", "failure_ids")
  failureIds?: FailureId[];
  @encodedName("application/json", "agent_name")
  agentName?: string;
  @doc("Tokens per model of `genai_usage.models_used`; absent for a session without GenAI requests.")
  @encodedName("application/json", "tokens_by_model")
  tokensByModel?: Record<AgentTokens>;
  @doc("Annotations on the session; absent when it has none.")
  annotations?: Annotation[];
}

@doc("LogPattern contract.")
model LogPattern {
  template: string;
  count: Nami.Api.Contracts.Common.JsonInteger;
  severity: int32;
  @encodedName("application/json", "first_seen")
  firstSeen: Nami.Api.Contracts.Common.Timestamp;
  @encodedName("application/json", "last_seen")
  lastSeen: Nami.Api.Contracts.Common.Timestamp;
  @doc("Traces the pattern was seen in.")
  @maxItems(3)
  @encodedName("application/json", "example_trace_ids")
  exampleTraceIds: TraceId[];
}

@doc("A log record as the analysis tools return it.")
model ToolLogRow {
  @doc("The record's event time, else the time it was observed.")
  time: Nami.Api.Contracts.Common.Timestamp;
  @doc("The severity text, else the OTel short name of its severity number.")
  severity?: string;
  @encodedName("application/json", "severity_number")
  severityNumber: int32;
  service: string;
  @doc("The body as text; a structured body is its JSON.")
  body: string;
  @encodedName("application/json", "trace_id")
  traceId?: TraceId;
  @encodedName("application/json", "span_id")
  spanId?: SpanId;
  attributes: Attribute[];
}

@doc("One metric descriptor of list_metrics, its name, kind, unit, temporality and monotonicity, across every resource and scope version that reports it.")
model MetricCatalogRow {
  name: string;
  kind: MetricKind;
  unit?: string;
  temporality: MetricTemporality;
  @doc("Whether the sum only increases; false for an up-down sum and every other kind.")
  monotonic: boolean;
  @doc("Services that report the metric with this descriptor.")
  services: string[];
  @doc("Series of this descriptor, across every resource and scope version that reports it.")
  @encodedName("application/json", "series_count")
  seriesCount: Nami.Api.Contracts.Common.JsonInteger;
  @doc("When the latest of those series was last seen.")
  @encodedName("application/json", "last_seen")
  lastSeen: Nami.Api.Contracts.Common.Timestamp;
  @doc("The aggregations query_metric accepts for it.")
  @encodedName("application/json", "supported_aggregations")
  supportedAggregations: MetricAggregation[];
  @doc("Whether the name is also recorded with another kind, unit or monotonicity; query_metric then needs a `series_id`, which its conflict error names. Another temporality alone is no conflict: query_metric reads each series by its own.")
  @encodedName("application/json", "has_conflict")
  hasConflict: boolean;
  @doc("The attribute keys of the descriptor's series with their numbers of distinct values across them, most values first.")
  keys: MetricKeySummary[];
  @doc("The keys with 2 to 20 values: the ones to pass as query_metric's `group_by`.")
  @encodedName("application/json", "suggested_group_by")
  suggestedGroupBy: string[];
}

@doc("One attribute key of a metric instrument's series.")
model MetricKeySummary {
  key: string;
  @doc("Distinct values the key holds across the descriptor's series.")
  cardinality: Nami.Api.Contracts.Common.JsonInteger;
}

@doc("One original point of a metric series, as the producer recorded it.")
model MetricRawPoint {
  @doc("When the point was recorded.")
  time: Nami.Api.Contracts.Common.Timestamp;
  @doc("Start of the interval the point covers, when the producer recorded one.")
  @encodedName("application/json", "start_time")
  startTime?: Nami.Api.Contracts.Common.Timestamp;
  @doc("The original OTLP data: a number (an int64 is a tagged string that keeps every digit), a histogram, an exponential histogram or a summary; null when an older record did not retain it.")
  data: MetricPointData | null;
  @doc("Whether a different measurement was recorded at the same timestamp; each one is its own point.")
  @encodedName("application/json", "has_conflict")
  hasConflict: boolean;
  @doc("The point's exemplars, when it has any.")
  exemplars?: MetricRawExemplar[];
}

@doc("One exemplar of an original metric point: a sampled measurement and the span it came from.")
model MetricRawExemplar {
  time: Nami.Api.Contracts.Common.Timestamp;
  value: MetricNumberValue;
  @encodedName("application/json", "trace_id")
  traceId?: TraceId;
  @encodedName("application/json", "span_id")
  spanId?: SpanId;
  @doc("Attributes the exemplar carries beyond its series'.")
  @encodedName("application/json", "filtered_attributes")
  filteredAttributes?: Attribute[];
}

@doc("The denominator of a ratio: another metric, queried over the same window and step with the same `group_by` and `function`.")
model MetricRatio {
  @doc("The denominator instrument.")
  @encodedName("application/json", "metric_name")
  metricName: string;
  @doc("The denominator's reducer; default the numerator's `aggregation`.")
  aggregation?: MetricAggregation;
  @doc("Attribute predicates for the denominator's series; the numerator's `matchers` do not apply to it.")
  @maxItems(32)
  matchers?: TelemetryMatcher[];
}

@doc("MetricComparisonResult contract.")
model MetricComparisonResult {
  window: ToolWindow;
  series: MetricSeriesResult[];
}

@doc("CiAttempt contract.")
model CiAttempt {
  @encodedName("application/json", "ci_run_id")
  ciRunId: SessionId;
  attempt: int32;
  @encodedName("application/json", "provider_run_id")
  providerRunId?: string;
  @encodedName("application/json", "provider_run_url")
  providerRunUrl?: UrlString;
}

@doc("CiPhaseChange contract.")
model CiPhaseChange {
  leg: string;
  phase: string;
  @encodedName("application/json", "current_duration_ms")
  currentDurationMs?: DurationMs;
  @encodedName("application/json", "baseline_duration_ms")
  baselineDurationMs?: DurationMs;
  ratio?: float64;
  @encodedName("application/json", "trace_ids")
  traceIds?: TraceId[];
}

@doc("CiComparison contract.")
model CiComparison {
  @doc("The run the selected one was compared with; absent when no run matched the baseline.")
  @encodedName("application/json", "baseline_ci_run_id")
  baselineCiRunId?: SessionId;
  @encodedName("application/json", "new_failures")
  newFailures: CiPhaseChange[];
  fixed: CiPhaseChange[];
  @encodedName("application/json", "duration_regressions")
  durationRegressions: CiPhaseChange[];
}

@doc("CiErrorExcerpt contract.")
model CiErrorExcerpt {
  id: string;
  body: string;
  @encodedName("application/json", "trace_id")
  traceId?: TraceId;
  @encodedName("application/json", "span_id")
  spanId?: SpanId;
}


@doc("One span of get_trace; its service's resource is in the trace's `resources`.")
model TraceSpanRow {
  @encodedName("application/json", "span_id")
  spanId: SpanId;
  @doc("Absent for a root span.")
  @encodedName("application/json", "parent_span_id")
  parentSpanId?: SpanId;
  service: string;
  name: string;
  @encodedName("application/json", "start_time")
  startTime: Nami.Api.Contracts.Common.Timestamp;
  @encodedName("application/json", "duration_ms")
  durationMs: DurationMs;
  status: "unset" | "ok" | "error";
  @doc("The status description, when the producer set one.")
  message?: string;
  @doc("Span attributes; omitted when `include_attributes` is false.")
  attributes?: Attribute[];
  @doc("With `selection: critical_path`, the time not covered by the span's children.")
  @encodedName("application/json", "self_time_ms")
  selfTimeMs?: DurationMs;
  @encodedName("application/json", "self_time_share")
  selfTimeShare?: float64;
}

@doc("The resource attributes one service reported for this trace.")
model TraceResource {
  service: string;
  attributes: Attribute[];
}

@doc("One trace: its range, size and errors, the selected spans and each service's resource once.")
model ToolTrace {
  @encodedName("application/json", "trace_id")
  traceId: TraceId;
  @encodedName("application/json", "start_time")
  startTime: Nami.Api.Contracts.Common.Timestamp;
  @encodedName("application/json", "end_time")
  endTime: Nami.Api.Contracts.Common.Timestamp;
  @encodedName("application/json", "duration_ms")
  durationMs: DurationMs;
  @doc("Spans of the trace, before `selection` and paging.")
  @encodedName("application/json", "span_count")
  spanCount: int32;
  @encodedName("application/json", "error_count")
  errorCount: Nami.Api.Contracts.Common.JsonInteger;
  services: string[];
  spans: TraceSpanRow[];
  @doc("Omitted when `include_attributes` is false.")
  resources?: TraceResource[];
}

@doc("Canonical classification shared by MCP aggregates and their trace drilldowns.")
enum McpFailureClass { transport, protocol, tool_result, authentication, cancellation, timeout }
