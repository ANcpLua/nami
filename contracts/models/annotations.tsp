import "../common/telemetry-query.tsp";
// =============================================================================
// Annotations: user notes on a trace, span or session
// =============================================================================
// A note the user writes inside the verified subject's nami project. It never
// changes the telemetry it points at. get_trace and list_sessions read
// annotations back, so there is no annotation listing tool.
// =============================================================================

import "@typespec/http";
import "@typespec/openapi";
import "@typespec/openapi3";
import "../common/types.tsp";
import "../common/resource.tsp";

using TypeSpec.OpenAPI;
using Nami.Api.Contracts.Common;

namespace Nami.Api.Contracts.Annotations;

@doc("An annotation on one whole trace.")
model TraceAnnotationTarget {
  @doc("Always `trace`.")
  kind: "trace";

  @doc("The annotated trace.")
  @encodedName("application/json", "trace_id")
  traceId: TraceId;
}

@doc("An annotation on one span of a trace.")
model SpanAnnotationTarget {
  @doc("Always `span`.")
  kind: "span";

  @doc("The trace that holds the span.")
  @encodedName("application/json", "trace_id")
  traceId: TraceId;

  @doc("The annotated span.")
  @encodedName("application/json", "span_id")
  spanId: SpanId;
}

@doc("An annotation on one session.")
model SessionAnnotationTarget {
  @doc("Always `session`.")
  kind: "session";

  @doc("The annotated session.")
  @encodedName("application/json", "session_id")
  sessionId: SessionId;
}

@doc("Exactly one telemetry object an annotation is attached to.")
@oneOf
union AnnotationTarget {
  trace: TraceAnnotationTarget,
  span: SpanAnnotationTarget,
  session: SessionAnnotationTarget,
  failure: FailureAnnotationTarget,
}

@doc("A user note attached to a trace, span or session.")
model Annotation extends ResourceBase {
  @maxItems(10)
  labels?: AnnotationLabel[];
  classification?: AnnotationClassification;

  @doc("What the note is attached to. Fixed at creation.")
  @visibility(Lifecycle.Read, Lifecycle.Create)
  target: AnnotationTarget;

  @doc("The note, plain text, at most 2000 characters.")
  @minLength(1)
  @maxLength(2000)
  text: string;
}

@doc("Body of POST /api/v1/annotations: the `Lifecycle.Create` members of Annotation.")
model AnnotationCreateRequest {
  @doc("Up to 10 labels of 1 to 64 characters each; `annotation_label` filters by them.")
  @maxItems(10)
  labels?: AnnotationLabel[];
  @doc("What the note records: `note` (default), `hypothesis`, `finding` or `rejected`.")
  classification?: AnnotationClassification;

  @doc("What the note is attached to: a `trace`, `span`, `session` or `failure` recorded in the project. Fixed at creation.")
  target: AnnotationTarget;

  @doc("The note, plain text, at most 2000 characters.")
  @minLength(1)
  @maxLength(2000)
  text: string;
}

@doc("Body of PATCH /api/v1/annotations/{annotation_id}: the `Lifecycle.Update` members of Annotation.")
model AnnotationUpdateRequest {
  @doc("New labels, replacing the old ones: up to 10 of 1 to 64 characters each. Omitted, the labels stay.")
  @maxItems(10)
  labels?: AnnotationLabel[];
  @doc("New classification: `note`, `hypothesis`, `finding` or `rejected`. Omitted, it stays.")
  classification?: AnnotationClassification;

  @doc("The new note, plain text, at most 2000 characters. Omitted, the text stays.")
  @minLength(1)
  @maxLength(2000)
  text?: string;
}

@doc("An annotation on one failure, named by its `failure_id`.")
model FailureAnnotationTarget {
  @doc("Always `failure`.")
  kind: "failure";
  @doc("The annotated failure, e.g. from triage.")
  @encodedName("application/json", "failure_id")
  failureId: FailureId;
}


@doc("Classification of the human annotation.")
enum AnnotationClassification { note, hypothesis, finding, rejected }

@doc("A short user-defined annotation label.")
@minLength(1)
@maxLength(64)
scalar AnnotationLabel extends string;
