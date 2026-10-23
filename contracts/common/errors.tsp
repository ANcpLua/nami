// =============================================================================
// nami product API problem details (RFC 9457)
// =============================================================================
// Every error body is an RFC 9457 problem: the five standard members plus the
// nami extension member `code`. A problem type is named twice, by its `type` URI
// and by its `code`, and each error model below fixes both, so a client branches
// on either and never on `title` or `detail`.
//
// Bodies carry no HTTP headers. The media type is a property of the body type
// (`@mediaTypeHint`), and a header an error response carries is declared on the
// operations whose server sends it: `X-Trace-Id` on the collector's 500
// (api/routes.tsp).
// =============================================================================

import "@typespec/http";

using TypeSpec.Http;

namespace Nami.Api.Contracts.Common.Errors;

@doc("""
  Problem type URI, the RFC 9457 `type` member: one per problem a nami server
  reports. `code` names the same problem type.
  """)
enum ProblemType {
  validationFailed: "https://nami.at/problems/validation-failed",
  unauthorized: "https://nami.at/problems/unauthorized",
  forbidden: "https://nami.at/problems/forbidden",
  notFound: "https://nami.at/problems/not-found",
  conflict: "https://nami.at/problems/conflict",
  preconditionFailed: "https://nami.at/problems/precondition-failed",
  preconditionRequired: "https://nami.at/problems/precondition-required",
  internalError: "https://nami.at/problems/internal-error",
  serviceUnavailable: "https://nami.at/problems/service-unavailable",
}

@doc("""
  Stable machine-readable code of a problem type, the nami `code` extension
  member. `type` names the same problem type as a URI.
  """)
enum ProblemCode {
  validationFailed: "validation_failed",
  unauthorized: "unauthorized",
  forbidden: "forbidden",
  notFound: "not_found",
  conflict: "conflict",
  preconditionFailed: "precondition_failed",
  preconditionRequired: "precondition_required",
  internalError: "internal_error",
  serviceUnavailable: "service_unavailable",
}

// =============================================================================
// RFC 9457 Problem Details Base
// =============================================================================

@doc("RFC 9457 problem details, served as application/problem+json: the standard members plus the nami `code` extension.")
@mediaTypeHint("application/problem+json")
@error
model ProblemDetails {
  @doc("URI of the problem type.")
  type: ProblemType;

  @doc("Short summary of the problem type; the same for every occurrence.")
  title: string;

  @doc("HTTP status code of this response, repeated for the reader of the body.")
  status: int32;

  @doc("Explanation specific to this occurrence.")
  detail?: string;

  @doc("URI reference identifying this occurrence.")
  instance?: Nami.Api.Contracts.Common.UrlString;

  @doc("Stable code of the problem type.")
  code: ProblemCode;
}

// =============================================================================
// Problem Types
// =============================================================================

@doc("The request failed validation (400).")
@error
model ValidationError extends ProblemDetails {
  @statusCode _: 400;
  type: ProblemType.validationFailed;
  code: ProblemCode.validationFailed;
  title: "Validation Failed";

  @doc("Each rule the request broke.")
  errors: ValidationErrorDetail[];
}

@doc("One rule a request broke.")
model ValidationErrorDetail {
  @doc("The field or parameter that failed validation.")
  field: string;

  @doc("What is wrong with it.")
  message: string;

  @doc("The validation rule that failed.")
  code: string;

  @doc("The rejected value, when it is safe to repeat.")
  @encodedName("application/json", "rejected_value")
  rejectedValue?: string;

  @doc("The values the field accepts, when they form a closed set.")
  allowed?: string[];

  @doc("What to send instead.")
  hint?: string;
}

@doc("The request carries no valid credential (401).")
@error
model UnauthorizedError extends ProblemDetails {
  @statusCode _: 401;
  type: ProblemType.unauthorized;
  code: ProblemCode.unauthorized;
  title: "Unauthorized";
}

@doc("The credential is valid but not allowed to do this (403).")
@error
model ForbiddenError extends ProblemDetails {
  @statusCode _: 403;
  type: ProblemType.forbidden;
  code: ProblemCode.forbidden;
  title: "Forbidden";

  @doc("The permission the credential lacks.")
  @encodedName("application/json", "required_permission")
  requiredPermission?: string;
}

@doc("The addressed resource does not exist (404).")
@error
model NotFoundError extends ProblemDetails {
  @statusCode _: 404;
  type: ProblemType.notFound;
  code: ProblemCode.notFound;
  title: "Not Found";

  @doc("The kind of resource that was not found.")
  @encodedName("application/json", "resource_type")
  resourceType?: string;

  @doc("The identifier that was not found.")
  @encodedName("application/json", "resource_id")
  resourceId?: string;
}

@doc("The request conflicts with the current state of a resource (409).")
@error
model ConflictError extends ProblemDetails {
  @statusCode _: 409;
  type: ProblemType.conflict;
  code: ProblemCode.conflict;
  title: "Conflict";

  @doc("The identifier of the conflicting resource.")
  @encodedName("application/json", "conflicting_resource")
  conflictingResource?: string;
}

@doc("The If-Match entity tag is no longer current (412).")
@error
model PreconditionFailedError extends ProblemDetails {
  @statusCode _: 412;
  type: ProblemType.preconditionFailed;
  code: ProblemCode.preconditionFailed;
  title: "Precondition Failed";

  @doc("Entity tag of the current version, when the caller may read it.")
  @encodedName("application/json", "current_etag")
  currentEtag?: string;
}

@doc("The request must send If-Match (428).")
@error
model PreconditionRequiredError extends ProblemDetails {
  @statusCode _: 428;
  type: ProblemType.preconditionRequired;
  code: ProblemCode.preconditionRequired;
  title: "Precondition Required";

  @doc("The header the request must carry.")
  @encodedName("application/json", "required_header")
  requiredHeader?: "If-Match";
}

@doc("The server failed while handling the request (500).")
@error
model InternalServerError extends ProblemDetails {
  @statusCode _: 500;
  type: ProblemType.internalError;
  code: ProblemCode.internalError;
  title: "Internal Server Error";
}

@doc("The server cannot take the request at present (503).")
@error
model ServiceUnavailableError extends ProblemDetails {
  @statusCode _: 503;
  type: ProblemType.serviceUnavailable;
  code: ProblemCode.serviceUnavailable;
  title: "Service Unavailable";

  @doc("Why the server is unavailable.")
  reason?: "maintenance" | "overloaded" | "dependency_failure";
}
