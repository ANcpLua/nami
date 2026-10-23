import "./routes.tsp";
import "../models/mcp-tools.tsp";
import "../models/mcp-analysis.tsp";
using TypeSpec.Http;
using TypeSpec.OpenAPI;
using Nami.Api.Contracts.Common;
using Nami.Api.Contracts.Common.Errors;
using Nami.Api.Contracts.Mcp.Tools;
namespace Nami.Api.Contracts;

@route("/api/v1/analysis")
@tag("Analysis")
@useAuth(NamiApiKeyHeaderAuth)
interface AnalysisApi {
  @doc("Read triage evidence in the authenticated project without changing telemetry or user resources.")
  @post
  @route("/triage")
  @operationId("analysis_triage")
  triage(...ProjectScopeHeader, @body request: TriageInput): TriageOutput | ValidationError | NotFoundError | UnauthorizedError | CollectorServerErrorResponse;
  @doc("Read get_failure_context evidence for exactly one of trace_id, session_id, failure_id or ci_run_id in the authenticated project without changing telemetry or user resources.")
  @post
  @route("/get_failure_context")
  @operationId("analysis_get_failure_context")
  get_failure_context(...ProjectScopeHeader, @body request: GetFailureContextInput): GetFailureContextOutput | ValidationError | NotFoundError | UnauthorizedError | CollectorServerErrorResponse;
  @doc("Read compare_groups evidence in the authenticated project without changing telemetry or user resources.")
  @post
  @route("/compare_groups")
  @operationId("analysis_compare_groups")
  compare_groups(...ProjectScopeHeader, @body request: CompareGroupsInput): CompareGroupsOutput | ValidationError | NotFoundError | UnauthorizedError | CollectorServerErrorResponse;
  @doc("Read compare_traces evidence in the authenticated project without changing telemetry or user resources.")
  @post
  @route("/compare_traces")
  @operationId("analysis_compare_traces")
  compare_traces(...ProjectScopeHeader, @body request: CompareTracesInput): CompareTracesOutput | ValidationError | NotFoundError | UnauthorizedError | CollectorServerErrorResponse;
  @doc("Read verify_fix evidence in the authenticated project without changing telemetry or user resources.")
  @post
  @route("/verify_fix")
  @operationId("analysis_verify_fix")
  verify_fix(...ProjectScopeHeader, @body request: VerifyFixInput): VerifyFixOutput | ValidationError | NotFoundError | UnauthorizedError | CollectorServerErrorResponse;
  @doc("Read aggregate_telemetry evidence in the authenticated project without changing telemetry or user resources.")
  @post
  @route("/aggregate_telemetry")
  @operationId("analysis_aggregate_telemetry")
  aggregate_telemetry(...ProjectScopeHeader, @body request: AggregateTelemetryInput): AggregateTelemetryOutput | ValidationError | NotFoundError | UnauthorizedError | CollectorServerErrorResponse;
  @doc("Read discover_attributes evidence in the authenticated project without changing telemetry or user resources.")
  @post
  @route("/discover_attributes")
  @operationId("analysis_discover_attributes")
  discover_attributes(...ProjectScopeHeader, @body request: DiscoverAttributesInput): DiscoverAttributesOutput | ValidationError | NotFoundError | UnauthorizedError | CollectorServerErrorResponse;
  @doc("Read get_agent_run evidence in the authenticated project without changing telemetry or user resources.")
  @post
  @route("/get_agent_run")
  @operationId("analysis_get_agent_run")
  get_agent_run(...ProjectScopeHeader, @body request: GetAgentRunInput): GetAgentRunOutput | ValidationError | NotFoundError | UnauthorizedError | CollectorServerErrorResponse;
  @doc("Read list_releases evidence in the authenticated project without changing telemetry or user resources.")
  @post
  @route("/list_releases")
  @operationId("analysis_list_releases")
  list_releases(...ProjectScopeHeader, @body request: ListReleasesInput): ListReleasesOutput | ValidationError | NotFoundError | UnauthorizedError | CollectorServerErrorResponse;
  @doc("Read get_service_map evidence in the authenticated project without changing telemetry or user resources.")
  @post
  @route("/get_service_map")
  @operationId("analysis_get_service_map")
  get_service_map(...ProjectScopeHeader, @body request: GetServiceMapInput): GetServiceMapOutput | ValidationError | NotFoundError | UnauthorizedError | CollectorServerErrorResponse;
  @doc("Read check_telemetry_health evidence in the authenticated project without changing telemetry or user resources.")
  @post
  @route("/check_telemetry_health")
  @operationId("analysis_check_telemetry_health")
  check_telemetry_health(...ProjectScopeHeader, @body request: CheckTelemetryHealthInput): CheckTelemetryHealthOutput | ValidationError | NotFoundError | UnauthorizedError | CollectorServerErrorResponse;
  @doc("Read get_mcp_stats evidence in the authenticated project without changing telemetry or user resources.")
  @post
  @route("/get_mcp_stats")
  @operationId("analysis_get_mcp_stats")
  get_mcp_stats(...ProjectScopeHeader, @body request: GetMcpStatsInput): GetMcpStatsOutput | ValidationError | NotFoundError | UnauthorizedError | CollectorServerErrorResponse;
  @doc("Read list_sessions evidence in the authenticated project without changing telemetry or user resources.")
  @post
  @route("/list_sessions")
  @operationId("analysis_list_sessions")
  list_sessions(...ProjectScopeHeader, @body request: ListSessionsInput): ListSessionsOutput | ValidationError | NotFoundError | UnauthorizedError | CollectorServerErrorResponse;
  @doc("Read list_traces evidence in the authenticated project without changing telemetry or user resources.")
  @post
  @route("/list_traces")
  @operationId("analysis_list_traces")
  list_traces(...ProjectScopeHeader, @body request: ListTracesInput): ListTracesOutput | ValidationError | NotFoundError | UnauthorizedError | CollectorServerErrorResponse;
  @doc("Read get_trace evidence in the authenticated project without changing telemetry or user resources.")
  @post
  @route("/get_trace")
  @operationId("analysis_get_trace")
  get_trace(...ProjectScopeHeader, @body request: GetTraceInput): GetTraceOutput | ValidationError | NotFoundError | UnauthorizedError | CollectorServerErrorResponse;
  @doc("Read search_logs evidence in the authenticated project without changing telemetry or user resources.")
  @post
  @route("/search_logs")
  @operationId("analysis_search_logs")
  search_logs(...ProjectScopeHeader, @body request: SearchLogsInput): SearchLogsOutput | ValidationError | NotFoundError | UnauthorizedError | CollectorServerErrorResponse;
  @doc("Read list_metrics evidence in the authenticated project without changing telemetry or user resources.")
  @post
  @route("/list_metrics")
  @operationId("analysis_list_metrics")
  list_metrics(...ProjectScopeHeader, @body request: ListMetricsInput): ListMetricsOutput | ValidationError | NotFoundError | UnauthorizedError | CollectorServerErrorResponse;
  @doc("Read query_metric evidence in the authenticated project without changing telemetry or user resources.")
  @post
  @route("/query_metric")
  @operationId("analysis_query_metric")
  query_metric(...ProjectScopeHeader, @body request: QueryMetricInput): QueryMetricOutput | ValidationError | NotFoundError | UnauthorizedError | CollectorServerErrorResponse;
  @doc("Read ci_log evidence in the authenticated project without changing telemetry or user resources.")
  @post
  @route("/ci_log")
  @operationId("analysis_ci_log")
  ci_log(...ProjectScopeHeader, @body request: CiLogInput): CiLogOutput | ValidationError | NotFoundError | UnauthorizedError | CollectorServerErrorResponse;
  @doc("Read run_saved_query evidence in the authenticated project without changing telemetry or user resources.")
  @post
  @route("/run_saved_query")
  @operationId("analysis_run_saved_query")
  run_saved_query(...ProjectScopeHeader, @body request: RunSavedQueryInput): RunSavedQueryOutput | ValidationError | NotFoundError | UnauthorizedError | CollectorServerErrorResponse;
}

@doc("Mutable status and assignee of a failure; its telemetry fingerprint never changes.")
model UpdateFailureStatusRequest {
  status: FailureStatus;
  @encodedName("application/json", "ignore_until")
  ignoreUntil?: Nami.Api.Contracts.Common.Timestamp;
  @maxLength(256)
  assignee?: string;
}

@route("/api/v1/failures")
@tag("Failures")
interface FailuresApi {
  @doc("Set a project's failure status or assignee using the version returned by triage.")
  @useAuth(NamiWriteKeyAuth)
  @patch(#{ implicitOptionality: false })
  @route("/{failure_id}")
  update(...ProjectScopeHeader, ...IfMatchHeader, @path("failure_id") failureId: FailureId, @body request: UpdateFailureStatusRequest):
    UpdateFailureStatusOutput | ValidationError | UnauthorizedError | ForbiddenError | NotFoundError | PreconditionFailedError | PreconditionRequiredError | CollectorServerErrorResponse;
}
