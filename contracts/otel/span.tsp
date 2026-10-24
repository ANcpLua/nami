// =============================================================================
// OpenTelemetry span JSON projection
// =============================================================================
// Distributed tracing span representation following OTel specification.
// =============================================================================

import "@typespec/http";
import "@typespec/openapi";
import "../common/types.tsp";
import "./enums.tsp";
import "./resource.tsp";

using TypeSpec.Http;
using TypeSpec.OpenAPI;
using Nami.Api.Contracts.Common;
using Nami.Api.Contracts.OTel.Enums;
using Nami.Api.Contracts.OTel.Resource;

namespace Nami.Api.Contracts.OTel.Traces;

// =============================================================================
// Span Model
// =============================================================================

@doc("OpenTelemetry Span representing a single operation in a distributed trace")
model Span {
  @doc("Unique span identifier (16 hex chars)")
  @encodedName("application/json", "span_id")
  spanId: SpanId;

  @doc("Trace identifier (32 hex chars)")
  @encodedName("application/json", "trace_id")
  traceId: TraceId;

  @doc("Parent span identifier (null for root spans)")
  @encodedName("application/json", "parent_span_id")
  parentSpanId?: SpanId;

  @doc("W3C trace state")
  @encodedName("application/json", "trace_state")
  traceState?: TraceState;

  @doc("Human-readable span name")
  @minLength(1)
  name: string;

  @doc("Span kind")
  kind: SpanKind;

  @doc("Start timestamp in nanoseconds since epoch")
  @encodedName("application/json", "start_time_unix_nano")
  startTimeUnixNano: UnixNanos;

  @doc("End timestamp in nanoseconds since epoch")
  @encodedName("application/json", "end_time_unix_nano")
  endTimeUnixNano: UnixNanos;

  @doc("Span attributes")
  attributes?: Attribute[];

  @doc("Dropped attributes count")
  @encodedName("application/json", "dropped_attributes_count")
  droppedAttributesCount?: Count;

  @doc("Span events (logs attached to span)")
  events?: SpanEvent[];

  @doc("Dropped events count")
  @encodedName("application/json", "dropped_events_count")
  droppedEventsCount?: Count;

  @doc("Links to other spans")
  links?: SpanLink[];

  @doc("Dropped links count")
  @encodedName("application/json", "dropped_links_count")
  droppedLinksCount?: Count;

  @doc("Span status")
  status: SpanStatus;

  @doc("Span flags")
  flags?: OtlpFlags;

  @doc("Resource describing the entity that produced this span")
  resource: Nami.Api.Contracts.OTel.Resource.Resource;

  @doc("Instrumentation scope")
  @encodedName("application/json", "instrumentation_scope")
  instrumentationScope?: InstrumentationScope;
}

@doc("Span status")
model SpanStatus {
  @doc("Status code")
  code: SpanStatusCode;

  @doc("Status message (only for ERROR status)")
  message?: string;
}

@doc("Event occurring during a span's lifetime")
model SpanEvent {
  @doc("Event name")
  @minLength(1)
  name: string;

  @doc("Event timestamp in nanoseconds since epoch")
  @encodedName("application/json", "time_unix_nano")
  timeUnixNano: UnixNanos;

  @doc("Event attributes")
  attributes?: Attribute[];

  @doc("Dropped attributes count")
  @encodedName("application/json", "dropped_attributes_count")
  droppedAttributesCount?: Count;
}

@doc("Link to another span (e.g., batch processing)")
model SpanLink {
  @doc("Linked trace ID")
  @encodedName("application/json", "trace_id")
  traceId: TraceId;

  @doc("Linked span ID")
  @encodedName("application/json", "span_id")
  spanId: SpanId;

  @doc("Trace state of the linked span")
  @encodedName("application/json", "trace_state")
  traceState?: TraceState;

  @doc("Link attributes")
  attributes?: Attribute[];

  @doc("Dropped attributes count")
  @encodedName("application/json", "dropped_attributes_count")
  droppedAttributesCount?: Count;

  @doc("Link flags")
  flags?: OtlpFlags;
}

// =============================================================================
// Trace Model (Collection of Spans)
// =============================================================================

@doc("Trace summary without the full span collection")
model TraceSummary {
  @doc("Trace identifier")
  @encodedName("application/json", "trace_id")
  traceId: TraceId;

  @doc("Root span of the trace")
  @encodedName("application/json", "root_span")
  rootSpan?: Span;

  @doc("Total span count")
  @encodedName("application/json", "span_count")
  spanCount: int32;

  @doc("Trace duration in nanoseconds")
  @encodedName("application/json", "duration_ns")
  durationNs: DurationNs;

  @doc("Trace start time")
  @encodedName("application/json", "start_time")
  startTime: Nami.Api.Contracts.Common.Timestamp;

  @doc("Trace end time")
  @encodedName("application/json", "end_time")
  endTime: Nami.Api.Contracts.Common.Timestamp;

  @doc("Services involved in this trace")
  services: string[];

  @doc("Whether trace contains errors")
  @encodedName("application/json", "has_error")
  hasError: boolean;
}

@doc("Complete trace containing all related spans")
model Trace extends TraceSummary {
  @doc("All spans in this trace")
  spans: Span[];
}
