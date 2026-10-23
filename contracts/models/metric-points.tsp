import "../common/types.tsp";
import "./metrics.tsp";

// OTLP v1.11.1 metric point, numeric oneof and exemplar definitions:
// https://github.com/open-telemetry/opentelemetry-proto/blob/v1.11.1/opentelemetry/proto/metrics/v1/metrics.proto
// Identity and unknown-start semantics are defined separately by OTel spec v1.61.0:
// https://github.com/open-telemetry/opentelemetry-specification/blob/v1.61.0/specification/metrics/data-model.md

using Nami.Api.Contracts.Common;
using TypeSpec.OpenAPI;

namespace Nami.Api.Contracts.OTel.Metrics;

@doc("An unsigned OTLP count encoded as a decimal string to preserve all 64 bits.")
@minValue(0)
@encode(string)
scalar MetricCount extends uint64;

@doc("A numeric OTLP value. Integers preserve all signed 64 bits; doubles retain named non-finite values.")
@discriminated(#{ envelope: "none", discriminatorPropertyName: "type" })
union MetricNumberValue {
  int: AttributeIntValue,
  double: AttributeDoubleValue,
}

@doc("One original metric exemplar, in producer order.")
model MetricExemplar {
  @encodedName("application/json", "time_unix_nano")
  timeUnixNano: UnixNanos;

  value: MetricNumberValue;

  @encodedName("application/json", "filtered_attributes")
  filteredAttributes: Attribute[];

  @encodedName("application/json", "trace_id")
  traceId?: TraceId;

  @encodedName("application/json", "span_id")
  spanId?: SpanId;
}

@doc("The numeric oneof of a gauge or sum point.")
model MetricNumberPointData {
  type: "number";
  value?: MetricNumberValue;
}

@doc("An original explicit histogram, before any query projection or interpolation.")
model MetricHistogramPointData {
  type: "histogram";
  count: MetricCount;
  sum?: AttributeDouble;
  min?: AttributeDouble;
  max?: AttributeDouble;

  @encodedName("application/json", "explicit_bounds")
  explicitBounds: AttributeDouble[];

  @encodedName("application/json", "bucket_counts")
  bucketCounts: MetricCount[];
}

@doc("Native exponential-histogram buckets. Each count corresponds to offset plus its array index.")
model MetricExponentialBuckets {
  offset: int32;

  @encodedName("application/json", "bucket_counts")
  bucketCounts: MetricCount[];
}

@doc("An original exponential histogram. Its native scale and buckets are never reconstructed from explicit bounds.")
model MetricExponentialHistogramPointData {
  type: "exponential_histogram";
  count: MetricCount;
  sum?: AttributeDouble;
  min?: AttributeDouble;
  max?: AttributeDouble;
  scale: int32;

  @encodedName("application/json", "zero_count")
  zeroCount: MetricCount;

  @encodedName("application/json", "zero_threshold")
  zeroThreshold: AttributeDouble;

  positive?: MetricExponentialBuckets;
  negative?: MetricExponentialBuckets;
}

@doc("An original summary quantile and its producer-supplied estimate.")
model MetricQuantileValue {
  @minValue(0)
  @maxValue(1)
  quantile: float64;
  value: AttributeDouble;
}

@doc("An original OTLP summary. Query reducers must explicitly support summaries rather than invent histogram buckets.")
model MetricSummaryPointData {
  type: "summary";
  count: MetricCount;
  sum: AttributeDouble;

  @encodedName("application/json", "quantile_values")
  quantileValues: MetricQuantileValue[];
}

@doc("The original typed OTLP point data, independent of the collector's derived aggregate columns.")
@discriminated(#{ envelope: "none", discriminatorPropertyName: "type" })
union MetricPointData {
  number: MetricNumberPointData,
  histogram: MetricHistogramPointData,
  exponential_histogram: MetricExponentialHistogramPointData,
  summary: MetricSummaryPointData,
}

@doc("A derived, potentially approximate query projection. This is not the original OTLP point.")
model MetricPointProjection {
  value?: AttributeDouble;
  count?: MetricCount;
  sum?: AttributeDouble;
  min?: AttributeDouble;
  max?: AttributeDouble;

  @encodedName("application/json", "explicit_bounds")
  explicitBounds?: AttributeDouble[];

  @encodedName("application/json", "bucket_counts")
  bucketCounts?: MetricCount[];
}

@doc("""
  A stored point from one exact metric series. Null data means an older record did
  not retain the original representation; the collector does not invent it from
  normalized values. Absent flags, exemplars or metadata mean historically unknown,
  while zero flags and an empty array are known producer values on new records.
  """)
model MetricPoint {
  @doc("Stable identifier of this original observation. Identical export retries retain the same identity.")
  @encodedName("application/json", "observation_id")
  observationId: string;

  @doc("Whether a different measurement was recorded at the same series timestamp. Exemplar or metadata enrichment alone is not a measurement conflict.")
  @encodedName("application/json", "has_conflict")
  hasConflict: boolean;

  @encodedName("application/json", "series_id")
  seriesId: string;

  @encodedName("application/json", "time_unix_nano")
  timeUnixNano: UnixNanos;

  @doc("Start timestamp when known; an omitted start is never replaced with an invented interval.")
  @encodedName("application/json", "start_time_unix_nano")
  startTimeUnixNano?: UnixNanos;

  flags?: OtlpFlags;
  attributes: Attribute[];
  data: MetricPointData | null;
  exemplars?: MetricExemplar[];

  @doc("Trace links retained by an older collector that did not record complete exemplars. These do not claim a known exemplar value, timestamp, span or attributes.")
  @encodedName("application/json", "historical_exemplar_trace_ids")
  historicalExemplarTraceIds?: TraceId[];

  @doc("Original non-identifying instrument metadata attached to this point's export.")
  @encodedName("application/json", "instrument_metadata")
  instrumentMetadata?: Attribute[];

  @doc("Resource attribute drop count from this observation's export; absent on historical records that did not retain it.")
  @encodedName("application/json", "resource_dropped_attributes_count")
  resourceDroppedAttributesCount?: Count;

  @doc("Instrumentation scope attribute drop count from this observation's export; absent on historical records that did not retain it.")
  @encodedName("application/json", "scope_dropped_attributes_count")
  scopeDroppedAttributesCount?: Count;

  `projection`?: MetricPointProjection;

  @doc("Why this original point cannot be represented by the derived aggregate projection, when applicable.")
  @encodedName("application/json", "projection_unavailable_reason")
  projectionUnavailableReason?: string;
}
