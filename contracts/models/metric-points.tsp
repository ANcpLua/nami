import "../common/types.tsp";

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
@discriminated(#{ envelope: "none", discriminatorPropertyName: "kind" })
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
  kind: "number";
  value?: MetricNumberValue;
}

@doc("An original explicit histogram, before any query projection or interpolation.")
model MetricHistogramPointData {
  kind: "histogram";
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
  kind: "exponential_histogram";
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
  kind: "summary";
  count: MetricCount;
  sum: AttributeDouble;

  @encodedName("application/json", "quantile_values")
  quantileValues: MetricQuantileValue[];
}

@doc("The original typed OTLP point data, independent of the collector's derived aggregate columns.")
@discriminated(#{ envelope: "none", discriminatorPropertyName: "kind" })
union MetricPointData {
  number: MetricNumberPointData,
  histogram: MetricHistogramPointData,
  exponential_histogram: MetricExponentialHistogramPointData,
  summary: MetricSummaryPointData,
}
