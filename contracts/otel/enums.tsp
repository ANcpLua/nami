// =============================================================================
// OpenTelemetry JSON projection enumerations
// =============================================================================
// Closed numeric enums from the OpenTelemetry protocol data model. Semantic-convention vocabulary is owned by Nami.Telemetry.SemanticConventions.
// =============================================================================

namespace Nami.Api.Contracts.OTel.Enums;

// =============================================================================
// Span Enumerations
// =============================================================================

@doc("Span kind describing the relationship between spans")
enum SpanKind {
  @doc("Default. Internal operation within the application")
  unspecified: 0,

  @doc("Internal operation within the application")
  internal: 1,

  @doc("Server-side operation handling a request")
  server: 2,

  @doc("Client-side operation making a request")
  client: 3,

  @doc("Producer creating a message for async processing")
  producer: 4,

  @doc("Consumer processing a message")
  consumer: 5,
}

@doc("Span status code")
enum SpanStatusCode {
  @doc("Status not set")
  unset: 0,

  @doc("Operation completed successfully")
  ok: 1,

  @doc("Operation failed with an error")
  error: 2,
}

// =============================================================================
// Log Enumerations
// =============================================================================

@doc("Log severity number following OTel specification (1-24)")
enum SeverityNumber {
  @doc("Unspecified severity")
  unspecified: 0,

  @doc("TRACE level")
  trace: 1,

  @doc("TRACE2 level")
  trace2: 2,

  @doc("TRACE3 level")
  trace3: 3,

  @doc("TRACE4 level")
  trace4: 4,

  @doc("DEBUG level")
  debug: 5,

  @doc("DEBUG2 level")
  debug2: 6,

  @doc("DEBUG3 level")
  debug3: 7,

  @doc("DEBUG4 level")
  debug4: 8,

  @doc("INFO level")
  info: 9,

  @doc("INFO2 level")
  info2: 10,

  @doc("INFO3 level")
  info3: 11,

  @doc("INFO4 level")
  info4: 12,

  @doc("WARN level")
  warn: 13,

  @doc("WARN2 level")
  warn2: 14,

  @doc("WARN3 level")
  warn3: 15,

  @doc("WARN4 level")
  warn4: 16,

  @doc("ERROR level")
  error: 17,

  @doc("ERROR2 level")
  error2: 18,

  @doc("ERROR3 level")
  error3: 19,

  @doc("ERROR4 level")
  error4: 20,

  @doc("FATAL level")
  fatal: 21,

  @doc("FATAL2 level")
  fatal2: 22,

  @doc("FATAL3 level")
  fatal3: 23,

  @doc("FATAL4 level")
  fatal4: 24,
}
