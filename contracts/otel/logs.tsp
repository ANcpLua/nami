// =============================================================================
// OpenTelemetry log JSON projection
// =============================================================================
// Structured logging following OTel log data model.
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

namespace Nami.Api.Contracts.OTel.Logs;

// =============================================================================
// Log Record Model
// =============================================================================

@doc("OpenTelemetry Log Record")
model LogRecord {
  @doc("Timestamp when the event occurred (nanoseconds since epoch). Absent when the source timestamp is unknown; use observed_time_unix_nano as the display fallback when present.")
  @encodedName("application/json", "time_unix_nano")
  timeUnixNano?: UnixNanos;

  @doc("Timestamp when the log was observed/collected (nanoseconds since epoch). Absent when a historical record did not capture it.")
  @encodedName("application/json", "observed_time_unix_nano")
  observedTimeUnixNano?: UnixNanos;

  @doc("Severity number (1-24)")
  @encodedName("application/json", "severity_number")
  severityNumber: SeverityNumber;

  @doc("Severity text exactly as supplied by the producer; classification uses severity_number.")
  @encodedName("application/json", "severity_text")
  severityText?: string;

  @doc("Log body: the OTLP AnyValue, projected exactly like an attribute value")
  body: AttributeValue;

  @doc("Log attributes")
  attributes?: Attribute[];

  @doc("Dropped attributes count")
  @encodedName("application/json", "dropped_attributes_count")
  droppedAttributesCount?: Count;

  @doc("Flags (trace flags)")
  flags?: OtlpFlags;

  @doc("Associated trace ID")
  @encodedName("application/json", "trace_id")
  traceId?: TraceId;

  @doc("Associated span ID")
  @encodedName("application/json", "span_id")
  spanId?: SpanId;

  @doc("Optional event category whose presence identifies this log record as an event; records with the same name are expected to share one body and attribute schema.")
  @encodedName("application/json", "event_name")
  eventName?: string;

  @doc("Resource describing the entity that produced this log")
  resource: Nami.Api.Contracts.OTel.Resource.Resource;

  @doc("Instrumentation scope")
  @encodedName("application/json", "instrumentation_scope")
  instrumentationScope?: InstrumentationScope;
}
