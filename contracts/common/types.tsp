import "./values.tsp";
// =============================================================================
// nami common types and scalars
// =============================================================================
// Foundational types used across nami API contracts and OTel-compatible models.
// Telemetry wire projections carry producer values; Nami.SemanticConventions owns registry vocabulary and semantic validation.
// =============================================================================

import "@typespec/openapi";

namespace Nami.Api.Contracts.Common;

// =============================================================================
// Identifier Scalars - Strongly Typed IDs
// =============================================================================

@doc("Unique trace identifier (32 lowercase hex characters)")
@pattern("^[a-f0-9]{32}(?![\\s\\S])")
@minLength(32)
@maxLength(32)
@example("0af7651916cd43dd8448eb211c80319c")
scalar TraceId extends string;

@doc("Unique span identifier (16 lowercase hex characters)")
@pattern("^[a-f0-9]{16}(?![\\s\\S])")
@minLength(16)
@maxLength(16)
@example("b7ad6b7169203331")
scalar SpanId extends string;

@doc("W3C Trace Context tracestate header (vendor-specific key-value pairs)")
@example("rojo=00f067aa0ba902b7,congo=t61rcWkgMzE")
scalar TraceState extends string;

@doc("The complete unsigned 32-bit OTLP flags field, including protocol bits unknown to this API revision.")
@minValue(0)
@maxValue(4294967295)
scalar OtlpFlags extends uint32;

@doc("Unique session identifier: URL-safe characters, at least one, at most 128, not only dots. The same rule the SDK enforces on the baggage header.")
@minLength(1)
@maxLength(128)
@pattern("^(?!\\.+$)[A-Za-z0-9._~-]{1,128}(?![\\s\\S])")
scalar SessionId extends string;

@doc("User identifier (pseudonymized for privacy)")
@minLength(1)
@maxLength(256)
scalar UserId extends string;

// =============================================================================
// Temporal Scalars - Timestamps & Durations
// =============================================================================

@doc("RFC 3339 timestamp preserved exactly as text, including fractional precision and offset. Leap seconds are not supported. Execution windows additionally require an instant representable by the telemetry store.")
@pattern("^(?:[0-9]{4}-(?:(?:0[13578]|1[02])-(?:0[1-9]|[12][0-9]|3[01])|(?:0[469]|11)-(?:0[1-9]|[12][0-9]|30)|02-(?:0[1-9]|1[0-9]|2[0-8]))|(?:[0-9]{2}(?:0[48]|[2468][048]|[13579][26])|(?:[02468][048]|[13579][26])00)-02-29)[Tt](?:[01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9](?:\\.[0-9]+)?(?:[Zz]|[+-](?:[01][0-9]|2[0-3]):[0-5][0-9])(?![\\s\\S])")
scalar Timestamp extends string;

// Encoded as a decimal string, not a JSON number. A 2026 nanosecond timestamp is ~1.79e18,
// well past Number.MAX_SAFE_INTEGER (9.007e15), so a bare JSON number is rounded to the
// nearest 256 ns by every JavaScript consumer — two spans 100 ns apart come back byte-identical
// and the waterfall reorders them on each reload. This is the same reason OTLP/JSON mandates
// decimal strings for its 64-bit fixed64 fields.
@doc("Unsigned Unix timestamp in nanoseconds since epoch, encoded as a decimal string")
@minValue(0)
@encode(string)
scalar UnixNanos extends Nami.Models.Values.UnixNanos;

@doc("Duration in nanoseconds, encoded as a decimal string")
@minValue(0)
@encode(string)
scalar DurationNs extends uint64;

@doc("Duration in milliseconds")
@minValue(0)
scalar DurationMs extends float64;

@doc("ISO 8601 duration format (e.g., PT1H30M)")
@pattern("^P(?!$)(([0-9]+Y)?([0-9]+M)?([0-9]+D)?(T(?=[0-9])([0-9]+H)?([0-9]+M)?([0-9]+S)?)?|([0-9]+W)?)(?![\\s\\S])")
scalar IsoDuration extends string;

// =============================================================================
// Size & Count Scalars
// =============================================================================

#suppress "@ancplua/typespec-nami-lint/unreachable-declaration" "unused at 48bdb08: nothing in the contract references it, no type is generated from it and no hand-written C# or TypeScript names its schema; removing it changes three generated schema files and no generated type"
@doc("Token count (for LLM operations)")
@minValue(0)
scalar TokenCount extends JsonInteger;

@doc("A JSON integer exactly representable in both JavaScript and C#. Larger integers use decimal string encoding.")
@minValue(-9007199254740991)
@maxValue(9007199254740991)
scalar JsonInteger extends int64;

@doc("Generic non-negative counter")
@minValue(0)
scalar Count extends JsonInteger;

// =============================================================================
// Semantic Scalars - Special Purpose Types
// =============================================================================

@doc("""
  Opaque pagination cursor. Pass the `next_cursor` of one page to receive the
  next page of the same query. Its content is not part of the contract and
  must not be parsed or built by a client.
  """)
scalar Cursor extends string;

// The revision is a canonical digest of the semantic OpenAPI projection and the
// exported telemetry-key projection, not a package version. Presentation-only
// fields and the release version stamped into `info.version` do not change it.
// The complete SHA-256 digest is retained so every semantic revision has its full fingerprint.
@doc("Deterministic revision of the generated API contract: 'sha256:' followed by all 64 hex characters of its canonical semantic digest.")
@pattern("^sha256:[a-f0-9]{64}(?![\\s\\S])")
@minLength(71)
@maxLength(71)
@example("sha256:0f1e2d3c4b5a69780f1e2d3c4b5a69780f1e2d3c4b5a69780f1e2d3c4b5a6978")
scalar ContractRevision extends string;

// =============================================================================
// OpenTelemetry Attribute Value Types
// =============================================================================

@doc("Base64 bytes projected by nami without colliding with an ordinary string attribute.")
model AttributeBytesValue {
  kind: "bytes";
  base64: bytes;
}

@doc("An OTLP signed 64-bit attribute integer encoded without JavaScript precision loss.")
@encode(string)
scalar AttributeInt64 extends Nami.Models.Values.AttributeInt64;

@doc("An OTLP attribute double encoded as a JSON number when finite or a canonical named string when non-finite.")
union AttributeDouble {
  finite: float64,
  nan: "NaN",
  positiveInfinity: "Infinity",
  negativeInfinity: "-Infinity",
}

@doc("Tagged OTLP signed 64-bit integer attribute.")
model AttributeIntValue {
  kind: "int";
  value: AttributeInt64;
}

@doc("Tagged OTLP double attribute.")
model AttributeDoubleValue {
  kind: "double";
  value: AttributeDouble;
}

@doc("Tagged recursive OTLP key-value-list, distinct from other object-shaped attribute variants.")
model AttributeKeyValueListValue {
  kind: "kvlist";
  values: Record<AttributeValue>;
}

@doc("Object-shaped AttributeValue variants. The variant name is the wire `kind` tag.")
@discriminated(#{ envelope: "none", discriminatorPropertyName: "kind" })
union AttributeObjectValue {
  @doc("Lossless 64-bit integer value")
  int: AttributeIntValue,

  @doc("Lossless finite or non-finite 64-bit floating point value")
  double: AttributeDoubleValue,

  @doc("Bytes value projected as an explicit tagged object")
  bytes: AttributeBytesValue,

  @doc("Recursive key-value-list value")
  kvlist: AttributeKeyValueListValue,
}

@doc("Lossless JSON projection of an OpenTelemetry AnyValue attribute. Primitive variants are bare JSON values; object variants carry a `type` tag.")
union AttributeValue {
  @doc("Empty AnyValue")
  emptyValue: null,

  @doc("String value")
  stringValue: string,

  @doc("Boolean value")
  boolValue: boolean,

  @doc("Nested and heterogeneous array value")
  arrayValue: AttributeValue[],

  @doc("Tagged object value")
  objectValue: AttributeObjectValue,
}

@doc("Key-value attribute pair following OTel conventions")
model Attribute {
  @doc("Attribute key (dot-separated namespace)")
  @minLength(1)
  @maxLength(256)
  key: string;

  @doc("Attribute value")
  value: AttributeValue;

  @doc("Registry stability of this key, when recorded at ingestion.")
  stability?:
    | "stable"
    | "development"
    | "alpha"
    | "beta"
    | "release_candidate"
    | "deprecated"
    | "unknown";
}

@doc("Collection of attributes")
model Attributes {
  @doc("List of key-value attributes")
  items: Attribute[];
}

@doc("A non-empty Resource attribute key referenced by an OpenTelemetry entity.")
@minLength(1)
scalar EntityAttributeKey extends string;

@doc("A reference to an OpenTelemetry entity described by attributes on the containing Resource.")
model EntityRef {
  @doc("Schema URL applying to the entity and its referenced Resource attributes, when known.")
  @encodedName("application/json", "schema_url")
  schemaUrl?: string;

  @doc("Stable non-empty entity type, such as service or host.")
  @minLength(1)
  type: string;

  @doc("Non-empty set of Resource attribute keys that identify the entity.")
  @encodedName("application/json", "id_keys")
  @minItems(1)
  idKeys: EntityAttributeKey[];

  @doc("Resource attribute keys that describe but do not identify the entity.")
  @encodedName("application/json", "description_keys")
  descriptionKeys?: EntityAttributeKey[];
}

// =============================================================================
// Instrumentation Scope
// =============================================================================

@doc("Instrumentation scope identifying the library/component emitting telemetry")
model InstrumentationScope {
  @doc("Name of the instrumentation scope (library name)")
  @encodedName("application/json", "name")
  scopeName?: string;

  @doc("Version of the instrumentation scope")
  @encodedName("application/json", "version")
  scopeVersion?: string;

  @doc("Scope schema URL supplied by the producer.")
  @encodedName("application/json", "schema_url")
  schemaUrl?: string;

  @doc("Additional attributes for the scope")
  @encodedName("application/json", "attributes")
  scopeAttributes?: Attribute[];

  @doc("Dropped attributes count")
  @encodedName("application/json", "dropped_attributes_count")
  droppedAttributesCount?: Count;
}
