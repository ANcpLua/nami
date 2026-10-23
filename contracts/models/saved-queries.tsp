import "../common/telemetry-query.tsp";
// =============================================================================
// Saved queries: named, structured filters for traces, logs or metrics
// =============================================================================
// A saved query stores a filter, never results. `:run` evaluates it against
// the current telemetry with a window relative to the time of the run.
// Names are unique per project; a duplicate is refused with 409.
// =============================================================================

import "@typespec/http";
import "@typespec/openapi";
import "@typespec/openapi3";
import "../common/types.tsp";
import "../common/pagination.tsp";
import "../common/resource.tsp";
import "../otel/enums.tsp";
import "../otel/span.tsp";
import "../otel/logs.tsp";
import "./metrics.tsp";

using TypeSpec.OpenAPI;
using Nami.Api.Contracts.Common;
using Nami.Api.Contracts.Common.Pagination;
using Nami.Api.Contracts.OTel.Enums;
using Nami.Api.Contracts.OTel.Logs;
using Nami.Api.Contracts.OTel.Metrics;
using Nami.Api.Contracts.OTel.Traces;

namespace Nami.Api.Contracts.SavedQueries;

@doc("The telemetry signal a saved query reads.")
enum SavedQuerySignal {
  traces: "traces",
  logs: "logs",
  metrics: "metrics",
}

@doc("Look-back window shared by every saved-query filter.")
model SavedQueryWindow {
  @doc("Window ending at the time of the run, in minutes. Omitted, the run's own `start_time`/`end_time` or the last 60 minutes apply.")
  @encodedName("application/json", "lookback_minutes")
  @minValue(1)
  @maxValue(43200)
  lookbackMinutes?: int32;
}

@doc("A saved filter over traces.")
model SavedTraceQueryFilter {
  @doc("Always `traces`: the query lists traces.")
  signal: "traces";

  @doc("Attribute predicates one span of the trace must all satisfy.")
  @maxItems(32)
  matchers?: TelemetryMatcher[];
  ...SavedQueryWindow;

  @doc("Keep traces with at least one span from this service.")
  @encodedName("application/json", "service_name")
  @minLength(1)
  @maxLength(256)
  serviceName?: string;

  @doc("Keep only traces with at least one error span.")
  @encodedName("application/json", "errors_only")
  errorsOnly?: boolean;
}

@doc("A saved filter over log records.")
model SavedLogQueryFilter {
  @doc("Always `logs`: the query searches log records.")
  signal: "logs";

  @doc("Attribute predicates every log record must satisfy.")
  @maxItems(32)
  matchers?: TelemetryMatcher[];
  ...SavedQueryWindow;

  @doc("Keep log records of this service.")
  @encodedName("application/json", "service_name")
  @minLength(1)
  @maxLength(256)
  serviceName?: string;

  @doc("Keep log records of at least this OpenTelemetry severity number: 9 info, 13 warn, 17 error.")
  @encodedName("application/json", "severity_min")
  severityMin?: SeverityNumber;

  @doc("Keep log records of this session.")
  @encodedName("application/json", "session_id")
  sessionId?: SessionId;

  @doc("Keep log records correlated with this trace.")
  @encodedName("application/json", "trace_id")
  traceId?: TraceId;

  @doc("Case-insensitive substring of the log body.")
  @minLength(1)
  @maxLength(1024)
  query?: string;
}

@doc("A saved metric range query.")
model SavedMetricQueryFilter {
  @doc("Always `metrics`: the query is a metric range query.")
  signal: "metrics";

  @doc("Select one exact metric series, when the query must disambiguate instruments.")
  @encodedName("application/json", "series_id")
  @minLength(1)
  seriesId?: string;
  ...SavedQueryWindow;

  @doc("The instrument to query.")
  @encodedName("application/json", "metric_name")
  @minLength(1)
  @maxLength(256)
  metricName: string;

  @doc("Reducer of the points in each bucket; default `avg`.")
  aggregation?: MetricAggregation = MetricAggregation.avg;

  @doc("Bucket width in integer milliseconds. Omit for whole seconds that split the run's window into at most 60 buckets.")
  @encodedName("application/json", "step_ms")
  @minValue(1)
  @maxValue(86400000)
  stepMs?: Nami.Api.Contracts.Common.JsonInteger;

  @doc("Attribute keys to split the result by.")
  @encodedName("application/json", "group_by")
  @maxItems(16)
  groupBy?: MetricGroupKey[];

  @doc("Attribute matchers a series must all satisfy.")
  @maxItems(32)
  matchers?: MetricAttributeMatcher[];
}

@doc("Exactly one structured filter; `signal`, which each form declares first, selects which.")
@oneOf
union SavedQueryFilter {
  traces: SavedTraceQueryFilter,
  logs: SavedLogQueryFilter,
  metrics: SavedMetricQueryFilter,
}

@doc("A named filter for traces, logs or metrics, owned by one project.")
model SavedQuery extends ResourceBase {
  @doc("Unique within the project; a duplicate is refused with 409.")
  @minLength(1)
  @maxLength(200)
  name: string;

  @maxLength(2000)
  description?: string;

  filter: SavedQueryFilter;
}

@doc("Body of POST /api/v1/saved-queries: the `Lifecycle.Create` members of SavedQuery.")
model SavedQueryCreateRequest {
  @doc("Unique within the project; a duplicate is refused with 409.")
  @minLength(1)
  @maxLength(200)
  name: string;

  @doc("What the query answers, at most 2000 characters.")
  @maxLength(2000)
  description?: string;

  @doc("The stored filter; its `signal` (`traces`, `logs` or `metrics`) selects which fields apply.")
  filter: SavedQueryFilter;
}

@doc("Body of PATCH /api/v1/saved-queries/{saved_query_id}: the `Lifecycle.Update` members of SavedQuery, each optional; omitted members keep their value.")
model SavedQueryUpdateRequest {
  @doc("New name, unique within the project; a duplicate is refused with 409.")
  @minLength(1)
  @maxLength(200)
  name?: string;

  @doc("New description, at most 2000 characters.")
  @maxLength(2000)
  description?: string;

  @doc("New filter, replacing the stored one whole.")
  filter?: SavedQueryFilter;
}

@doc("Optional overrides for one run of a saved query.")
model SavedQueryRunRequest {
  @doc("Start of this run's window (inclusive), RFC 3339; overrides the saved look-back window.")
  @encodedName("application/json", "start_time")
  startTime?: Nami.Api.Contracts.Common.Timestamp;

  @doc("End of this run's window (exclusive), RFC 3339; default the time of the run.")
  @encodedName("application/json", "end_time")
  endTime?: Nami.Api.Contracts.Common.Timestamp;

  @doc("Page size for trace and log runs; 100 when omitted.")
  limit?: PageLimit = 100;

  @doc("`next_cursor` of a previous run of the same saved query.")
  cursor?: Cursor;
}

@doc("One page of trace rows produced by a saved trace query, as list_traces returns them; get_trace reads one trace's spans.")
model SavedTraceQueryRun {
  signal: "traces";
  items: Nami.Api.Contracts.Mcp.Tools.ToolTraceRow[];

  @doc("Pass back as `cursor` for the next page of the same run. Absent on the last page.")
  @encodedName("application/json", "next_cursor")
  nextCursor?: Cursor;

  @doc("Whether more items follow this page. `next_cursor` is present exactly when this is true.")
  @encodedName("application/json", "has_more")
  hasMore: boolean;
}

@doc("One page of log records produced by a saved log query, as search_logs returns them.")
model SavedLogQueryRun {
  signal: "logs";
  items: Nami.Api.Contracts.Mcp.Tools.ToolLogRow[];

  @doc("Pass back as `cursor` for the next page of the same run. Absent on the last page.")
  @encodedName("application/json", "next_cursor")
  nextCursor?: Cursor;

  @doc("Whether more items follow this page. `next_cursor` is present exactly when this is true.")
  @encodedName("application/json", "has_more")
  hasMore: boolean;
}

@doc("The range-query result produced by a saved metric query.")
model SavedMetricQueryRun {
  signal: "metrics";
  result: MetricQueryResult;
}

@doc("The result of running a saved query; `signal` matches the query's filter.")
@oneOf
union SavedQueryRunResult {
  traces: SavedTraceQueryRun,
  logs: SavedLogQueryRun,
  metrics: SavedMetricQueryRun,
}
