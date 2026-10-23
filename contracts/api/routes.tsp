import "@typespec/http";
import "@typespec/openapi";

import "../common/types.tsp";
import "../common/errors.tsp";
import "../common/pagination.tsp";
import "../otel/span.tsp";
import "../otel/logs.tsp";
import "../models/session.tsp";
import "../models/health.tsp";
import "../models/metrics.tsp";
import "../models/metric-points.tsp";
import "../common/resource.tsp";
import "../common/auth.tsp";
import "../models/annotations.tsp";
import "../models/saved-queries.tsp";

using TypeSpec.Http;
using TypeSpec.OpenAPI;

using Nami.Api.Contracts.Common;
using Nami.Api.Contracts.Common.Errors;
using Nami.Api.Contracts.Common.Pagination;
using Nami.Api.Contracts.OTel.Traces;
using Nami.Api.Contracts.OTel.Logs;
using Nami.Api.Contracts.OTel.Metrics;
using Nami.Api.Contracts.OTel.Enums;
using Nami.Api.Contracts.Domains.Observe.Session;
using Nami.Api.Contracts.Health;
using Nami.Api.Contracts.Annotations;
using Nami.Api.Contracts.SavedQueries;

@service(#{ title: "nami API" })
namespace Nami.Api.Contracts;

// The query routes require a key with the `read` role (CollectorKeyRole.read).
alias NamiApiKeyHeaderAuth = ApiKeyAuth<ApiKeyLocation.header, "x-otlp-api-key">;

@doc("Optional project scope applied to collector query operations.")
model ProjectScopeHeader {
  @header("X-Nami-Project")
  projectId?: string;
}

// Problem bodies carry no headers (common/errors.tsp). The one header a
// collector error response carries is declared here, on the collector's own
// operations: its unhandled-exception handler names the trace of the failed
// request.
@doc("The collector failed while handling the request (500). `X-Trace-Id` names the collector's own trace of the failed request.")
@error
model CollectorServerErrorResponse {
  @doc("Trace id of the collector's own trace of the failed request, for finding it in the collector's telemetry.")
  @header("X-Trace-Id")
  traceId?: TraceId;

  @bodyRoot problem: InternalServerError;
}

// -----------------------------------------------------------------------------
// Traces
// -----------------------------------------------------------------------------

@route("/api/v1/traces")
@tag("Traces")
@useAuth(NamiApiKeyHeaderAuth)
interface TracesApi {
  @list
  @get
  list(
    ...ProjectScopeHeader,

    @doc("Keep traces that started at or after this time.")
    @query("start_time")
    startTime?: Nami.Api.Contracts.Common.Timestamp,

    @doc("Keep traces that started before this time.")
    @query("end_time")
    endTime?: Nami.Api.Contracts.Common.Timestamp,

    @doc("Keep traces with at least one span from this service.")
    @query("service_name")
    serviceName?: string,

    @doc("Keep only traces with at least one error span.")
    @query("errors_only")
    errorsOnly?: boolean,

    ...CursorPageQuery,
  ):
    | CursorPage<Trace>
    | ValidationError
    | UnauthorizedError
    | CollectorServerErrorResponse;

  @get
  @route("/{trace_id}")
  get(...ProjectScopeHeader, @path("trace_id") traceId: TraceId):
    | Trace
    | NotFoundError
    | UnauthorizedError
    | CollectorServerErrorResponse;

  @doc("Page through the spans of one trace. `GET /api/v1/traces/{trace_id}` returns the whole trace at once.")
  @list
  @get
  @route("/{trace_id}/spans")
  getSpans(
    ...ProjectScopeHeader,
    @path("trace_id") traceId: TraceId,

    ...CursorPageQuery,
  ):
    | CursorPage<Span>
    | ValidationError
    | NotFoundError
    | UnauthorizedError
    | CollectorServerErrorResponse;
}

// -----------------------------------------------------------------------------
// Logs
// -----------------------------------------------------------------------------

@route("/api/v1/logs")
@tag("Logs")
@useAuth(NamiApiKeyHeaderAuth)
interface LogsApi {
  @list
  @get
  list(
    ...ProjectScopeHeader,
    @query("session_id") sessionId?: SessionId,
    @query("service_name") serviceName?: string,
    @query level?: string,
    @query("severity_min") severityMin?: SeverityNumber,
    @query("trace_id") traceId?: TraceId,
    @query("start_time") startTime?: Nami.Api.Contracts.Common.Timestamp,
    @query("end_time") endTime?: Nami.Api.Contracts.Common.Timestamp,
    @query query?: string,

    ...CursorPageQuery,
  ):
    | CursorPage<LogRecord>
    | ValidationError
    | UnauthorizedError
    | CollectorServerErrorResponse;
}

// -----------------------------------------------------------------------------
// Metrics
//
// Modelled on the Grafana/Prometheus range query — metric, label matchers,
// range, step — because the primary consumer is a coding agent over MCP. The
// collector aggregates server-side and returns buckets. Exact-series point reads
// page the original representation separately, keeping each response bounded.
// -----------------------------------------------------------------------------

@route("/api/v1/metrics")
@tag("Metrics")
@useAuth(NamiApiKeyHeaderAuth)
interface MetricsApi {
  @doc("List the metric names recorded for a project.")
  @list
  @get
  list(
    ...ProjectScopeHeader,

    @doc("Return only names starting with this prefix.")
    @query("name_prefix")
    namePrefix?: string,

    ...CursorPageQuery,
  ):
    | CursorPage<MetricDescriptor>
    | ValidationError
    | UnauthorizedError
    | CollectorServerErrorResponse;

  @doc("List the distinct attribute streams recorded under one metric name.")
  @list
  @get
  @route("/{metric_name}/series")
  listSeries(
    ...ProjectScopeHeader,
    @path("metric_name") metricName: string,

    @doc("""
      Exact attribute match, repeatable, as `key=value`. A series must match
      every matcher given.
      """)
    @query(#{ explode: true })
    attr?: string[],

    @doc("Attribute prefix match, repeatable, as `key=prefix`.")
    @query(#{ name: "attr_prefix", explode: true })
    attrPrefix?: string[],

    ...CursorPageQuery,
  ):
    | CursorPage<MetricSeries>
    | ValidationError
    | NotFoundError
    | UnauthorizedError
    | CollectorServerErrorResponse;

  @doc("Read original points from one exact series. The associated resource and instrumentation scope come from listSeries.")
  @list
  @get
  @route("/{metric_name}/series/{series_id}/points")
  listPoints(
    ...ProjectScopeHeader,
    @path("metric_name") metricName: string,
    @path("series_id") seriesId: string,
    @query("start_time") startTime?: Nami.Api.Contracts.Common.Timestamp,
    @query("end_time") endTime?: Nami.Api.Contracts.Common.Timestamp,
    ...CursorPageQuery,
  ):
    | CursorPage<MetricPoint>
    | ValidationError
    | NotFoundError
    | UnauthorizedError
    | CollectorServerErrorResponse;

  @doc("""
    Aggregate one metric over a time range into buckets of `step_ms`.
    Percentile aggregations require a histogram kind.
    """)
  @get
  @route("/{metric_name}/query")
  query(
    ...ProjectScopeHeader,
    @path("metric_name") metricName: string,
    @query("start_time") startTime: Nami.Api.Contracts.Common.Timestamp,
    @query("end_time") endTime: Nami.Api.Contracts.Common.Timestamp,

    @doc("Bucket width in milliseconds. One bucket spanning the range collapses the query to a single value.")
    @query("step_ms")
    @minValue(1)
    @maxValue(86400000)
    stepMs?: Nami.Api.Contracts.Common.JsonInteger = 60000,

    @query aggregation?: MetricAggregation = MetricAggregation.avg,

    @doc("Select one exact series from listSeries. Required when the matched instrument descriptors are incompatible.")
    @query("series_id")
    @minLength(1)
    seriesId?: string,

    @doc("Attribute keys to split the result by, repeatable. Omitted, every matching series collapses into one stream.")
    @query(#{ name: "group_by", explode: true })
    groupBy?: string[],

    @doc("Exact attribute match, repeatable, as `key=value`.")
    @query(#{ explode: true })
    attr?: string[],

    @doc("Attribute prefix match, repeatable, as `key=prefix`.")
    @query(#{ name: "attr_prefix", explode: true })
    attrPrefix?: string[],

    @doc("Maximum result streams. A grouping that exceeds this is truncated, not refused.")
    @query("series_limit")
    @minValue(1)
    @maxValue(500)
    seriesLimit?: int32 = 50,
  ):
    | MetricQueryResult
    | ValidationError
    | ConflictError
    | NotFoundError
    | UnauthorizedError
    | CollectorServerErrorResponse;
}

// -----------------------------------------------------------------------------
// Sessions
// -----------------------------------------------------------------------------

@route("/api/v1/sessions")
@tag("Sessions")
@useAuth(NamiApiKeyHeaderAuth)
interface SessionsApi {
  @list
  @get
  list(
    ...ProjectScopeHeader,
    @query("is_active") isActive?: boolean,
    @query("start_time") startTime?: Nami.Api.Contracts.Common.Timestamp,
    @query("end_time") endTime?: Nami.Api.Contracts.Common.Timestamp,

    ...CursorPageQuery,
  ):
    | CursorPage<SessionEntity>
    | ValidationError
    | UnauthorizedError
    | CollectorServerErrorResponse;

  @get
  @route("/{session_id}")
  get(...ProjectScopeHeader, @path("session_id") sessionId: SessionId):
    | SessionEntity
    | NotFoundError
    | UnauthorizedError
    | CollectorServerErrorResponse;

  @list
  @get
  @route("/{session_id}/traces")
  getTraces(
    ...ProjectScopeHeader,
    @path("session_id") sessionId: SessionId,

    ...CursorPageQuery,
  ):
    | CursorPage<Trace>
    | ValidationError
    | NotFoundError
    | UnauthorizedError
    | CollectorServerErrorResponse;

  @get
  @route("/stats")
  getStats(
    ...ProjectScopeHeader,
    @query("start_time") startTime?: Nami.Api.Contracts.Common.Timestamp,
    @query("end_time") endTime?: Nami.Api.Contracts.Common.Timestamp,
  ): SessionStats | ValidationError | UnauthorizedError | CollectorServerErrorResponse;
}

// -----------------------------------------------------------------------------
// Annotations
//
// User notes on a trace, span or session (models/annotations.tsp). Reads take
// a `read` key, create and update a `write` key, delete a `delete` key.
// -----------------------------------------------------------------------------

@route("/api/v1/annotations")
@tag("Annotations")
interface AnnotationsApi {
  @doc("Attach a note of at most 2000 characters to a trace, span or session.")
  @useAuth(NamiWriteKeyAuth)
  @post
  create(
    ...ProjectScopeHeader,
    ...IdempotencyKeyHeader,
    @body request: AnnotationCreateRequest,
  ):
    | {
        @statusCode statusCode: 201;

        @doc("URL of the created annotation.")
        @header("Location")
        location: string;

        @header("ETag") etag: ETag;
        @body body: Annotation;
      }
    | ValidationError
    | UnauthorizedError
    | ForbiddenError
    | NotFoundError
    | ConflictError
    | CollectorServerErrorResponse;

  @doc("List annotations, newest first, optionally only those attached to one trace, span or session.")
  @useAuth(NamiApiKeyHeaderAuth)
  @list
  @get
  list(
    ...ProjectScopeHeader,

    @doc("Annotations on this trace, including those on its spans.")
    @query("trace_id")
    traceId?: TraceId,

    @doc("Annotations on this span; requires `trace_id`.")
    @query("span_id")
    spanId?: SpanId,

    @query("session_id") sessionId?: SessionId,

    ...CursorPageQuery,
  ):
    | CursorPage<Annotation>
    | ValidationError
    | UnauthorizedError
    | CollectorServerErrorResponse;

  @useAuth(NamiApiKeyHeaderAuth)
  @get
  @route("/{annotation_id}")
  get(...ProjectScopeHeader, @path("annotation_id") annotationId: ResourceId):
    | {
        @header("ETag") etag: ETag;
        @body body: Annotation;
      }
    | NotFoundError
    | UnauthorizedError
    | CollectorServerErrorResponse;

  @doc("Replace the text of an annotation. The target is fixed at creation.")
  @useAuth(NamiWriteKeyAuth)
  @patch(#{ implicitOptionality: false })
  @route("/{annotation_id}")
  update(
    ...ProjectScopeHeader,
    ...IfMatchHeader,
    @path("annotation_id") annotationId: ResourceId,
    @body request: AnnotationUpdateRequest,
  ):
    | {
        @header("ETag") etag: ETag;
        @body body: Annotation;
      }
    | ValidationError
    | UnauthorizedError
    | ForbiddenError
    | NotFoundError
    | PreconditionFailedError
    | PreconditionRequiredError
    | CollectorServerErrorResponse;

  @doc("Delete one annotation. Telemetry is never deleted.")
  @useAuth(NamiDeleteKeyAuth)
  @delete
  @route("/{annotation_id}")
  delete(
    ...ProjectScopeHeader,
    ...IfMatchHeader,
    @path("annotation_id") annotationId: ResourceId,
  ):
    | NoContentResponse
    | UnauthorizedError
    | ForbiddenError
    | NotFoundError
    | PreconditionFailedError
    | PreconditionRequiredError
    | CollectorServerErrorResponse;
}

// -----------------------------------------------------------------------------
// Saved queries
//
// Named, structured filters for traces, logs or metrics
// (models/saved-queries.tsp). Names are unique per project.
// -----------------------------------------------------------------------------

@route("/api/v1/saved-queries")
@tag("Saved queries")
interface SavedQueriesApi {
  @doc("Save a named filter. A name already used in the project is refused with 409.")
  @useAuth(NamiWriteKeyAuth)
  @post
  create(
    ...ProjectScopeHeader,
    ...IdempotencyKeyHeader,
    @body request: SavedQueryCreateRequest,
  ):
    | {
        @statusCode statusCode: 201;

        @doc("URL of the created saved query.")
        @header("Location")
        location: string;

        @header("ETag") etag: ETag;
        @body body: SavedQuery;
      }
    | ValidationError
    | UnauthorizedError
    | ForbiddenError
    | ConflictError
    | CollectorServerErrorResponse;

  @doc("List the project's saved queries by name.")
  @useAuth(NamiApiKeyHeaderAuth)
  @list
  @get
  list(
    ...ProjectScopeHeader,
    @query signal?: SavedQuerySignal,

    ...CursorPageQuery,
  ):
    | CursorPage<SavedQuery>
    | ValidationError
    | UnauthorizedError
    | CollectorServerErrorResponse;

  @useAuth(NamiApiKeyHeaderAuth)
  @get
  @route("/{saved_query_id}")
  get(...ProjectScopeHeader, @path("saved_query_id") savedQueryId: ResourceId):
    | {
        @header("ETag") etag: ETag;
        @body body: SavedQuery;
      }
    | NotFoundError
    | UnauthorizedError
    | CollectorServerErrorResponse;

  @doc("Change a saved query. Renaming onto a name already used in the project is refused with 409.")
  @useAuth(NamiWriteKeyAuth)
  @patch(#{ implicitOptionality: false })
  @route("/{saved_query_id}")
  update(
    ...ProjectScopeHeader,
    ...IfMatchHeader,
    @path("saved_query_id") savedQueryId: ResourceId,
    @body request: SavedQueryUpdateRequest,
  ):
    | {
        @header("ETag") etag: ETag;
        @body body: SavedQuery;
      }
    | ValidationError
    | UnauthorizedError
    | ForbiddenError
    | NotFoundError
    | ConflictError
    | PreconditionFailedError
    | PreconditionRequiredError
    | CollectorServerErrorResponse;

  @doc("Delete one saved query.")
  @useAuth(NamiDeleteKeyAuth)
  @delete
  @route("/{saved_query_id}")
  delete(
    ...ProjectScopeHeader,
    ...IfMatchHeader,
    @path("saved_query_id") savedQueryId: ResourceId,
  ):
    | NoContentResponse
    | UnauthorizedError
    | ForbiddenError
    | NotFoundError
    | PreconditionFailedError
    | PreconditionRequiredError
    | CollectorServerErrorResponse;

  @doc("Evaluate a saved query against current telemetry. Read-only.")
  @useAuth(NamiApiKeyHeaderAuth)
  @post
  @route("/{saved_query_id}:run")
  run(
    ...ProjectScopeHeader,
    @path("saved_query_id") savedQueryId: ResourceId,
    @body request: SavedQueryRunRequest,
  ):
    | SavedQueryRunResult
    | ValidationError
    | UnauthorizedError
    | NotFoundError
    | CollectorServerErrorResponse;
}

// -----------------------------------------------------------------------------
// Health
// -----------------------------------------------------------------------------

@tag("Health")
interface HealthApi {
  @get
  @route("/alive")
  @operationId("health_alive")
  alive():
    | {
        @statusCode statusCode: 200;
        @body body: HealthReport;
      }
    | {
        @statusCode statusCode: 503;
        @body body: HealthReport;
      };

  @get
  @route("/health")
  @operationId("health_ready")
  ready():
    | {
        @statusCode statusCode: 200;
        @body body: HealthReport;
      }
    | {
        @statusCode statusCode: 503;
        @body body: HealthReport;
      };
}
