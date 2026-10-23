import "../common/telemetry-query.tsp";
import "../common/types.tsp";
import "../otel/resource.tsp";

using Nami.Api.Contracts.Common;

namespace Nami.Api.Contracts.OTel.Metrics;

@doc("Shape of a stored metric stream.")
enum MetricKind {
  @doc("A value sampled at a point in time.")
  gauge: "gauge",

  @doc("An additive value; see `temporality` and `monotonic` for how to read it.")
  sum: "sum",

  @doc("A distribution recorded against explicit bucket boundaries.")
  histogram: "histogram",

  @doc("""
    An OTLP exponential histogram. Its buckets are stored in the same explicit
    bound vector `histogram` uses, so both kinds answer the same queries; the
    distinct name records only where the distribution came from.
    """)
  exponential_histogram: "exponential_histogram",

  @doc("A legacy OTLP summary, retaining its original quantiles and count/sum.")
  summary: "summary",
}

@doc("How successive points of a sum or histogram relate to each other.")
enum MetricTemporality {
  unspecified: "unspecified",

  @doc("Each point covers only the interval since the previous one.")
  delta: "delta",

  @doc("Each point is the running total since the stream's start time.")
  cumulative: "cumulative",
}

@doc("Server-side reducer applied to the points falling in one time bucket.")
enum MetricAggregation {
  avg: "avg",
  min: "min",
  max: "max",
  sum: "sum",
  count: "count",

  @doc("The value of the latest point in the bucket.")
  last: "last",

  @doc("50th percentile, interpolated from bucket boundaries. Histogram kinds only.")
  p50: "p50",

  @doc("90th percentile, interpolated from bucket boundaries. Histogram kinds only.")
  p90: "p90",

  @doc("95th percentile, interpolated from bucket boundaries. Histogram kinds only.")
  p95: "p95",

  @doc("99th percentile, interpolated from bucket boundaries. Histogram kinds only.")
  p99: "p99",
}

@doc("""
  One resource, instrumentation scope and metric name recorded for a project.
  Incompatible instrument descriptors within that group are reported as conflicts.
  The primary descriptor is an actual recorded variant, never a combination of variants.
  """)
model MetricDescriptor {
  @doc("Whether this resource/scope/name group contains incompatible recorded descriptors. The conflicts array lists the actual variants.")
  @encodedName("application/json", "has_conflict")
  hasConflict: boolean;

  @doc("Stable catalog group identifier, derived from resource, instrumentation scope and metric name.")
  @encodedName("application/json", "instrument_id")
  instrumentId: string;

  @doc("Resource that owns this metric group, when recorded.")
  resource?: Nami.Api.Contracts.OTel.Resource.Resource;

  @doc("Instrumentation scope that owns this metric group, when recorded.")
  @encodedName("application/json", "instrumentation_scope")
  instrumentationScope?: InstrumentationScope;

  @encodedName("application/json", "supported_aggregations")
  supportedAggregations?: MetricAggregation[];
  services?: string[];
  conflicts?: MetricDescriptorConflict[];

  @doc("Instrument name, e.g. `http.server.request.duration`.")
  name: string;

  kind: MetricKind;

  temporality: MetricTemporality;

  @doc("Whether a sum only ever increases. Meaningless for other kinds.")
  monotonic: boolean;

  @doc("UCUM unit as reported by the producer, e.g. `s` or `{request}`.")
  unit?: string;

  @doc("Producer-supplied description of the instrument.")
  description?: string;

  @doc("Number of distinct attribute streams recorded under this name.")
  @encodedName("application/json", "series_count")
  seriesCount: Nami.Api.Contracts.Common.JsonInteger;

  @doc("Timestamp of the most recent point recorded under this name.")
  @encodedName("application/json", "last_seen")
  lastSeen: Nami.Api.Contracts.Common.Timestamp;
}

@doc("One distinct attribute stream recorded under a metric name.")
model MetricSeries {
  new?: boolean;
  stale?: boolean;

  @doc("Stable identifier for this stream, derived from its instrument descriptor, attributes, resource and instrumentation scope.")
  @encodedName("application/json", "series_id")
  seriesId: string;

  @doc("Instrument name this series belongs to.")
  name: string;

  kind: MetricKind;

  @doc("The attributes that distinguish this series from others under the same name.")
  attributes: Attribute[];

  @encodedName("application/json", "service_name")
  serviceName?: string;

  @doc("Resource that emitted this stream, with its attributes and schema URL.")
  resource?: Nami.Api.Contracts.OTel.Resource.Resource;

  @doc("Instrumentation scope that owns this instrument; distinct scopes never merge into one series.")
  @encodedName("application/json", "instrumentation_scope")
  instrumentationScope?: InstrumentationScope;

  unit?: string;

  @encodedName("application/json", "first_seen")
  firstSeen: Nami.Api.Contracts.Common.Timestamp;

  @encodedName("application/json", "last_seen")
  lastSeen: Nami.Api.Contracts.Common.Timestamp;
}

@doc("One aggregated time bucket of a metric range query.")
model MetricBucket {
  @doc("Inclusive start of the bucket, aligned to the query's step from the query's start.")
  @encodedName("application/json", "bucket_start")
  bucketStart: Nami.Api.Contracts.Common.Timestamp;

  @doc("""
    The aggregation applied to the points in this bucket. Null when none of them
    holds a value, for example OTLP `NO_RECORDED_VALUE` markers or changes
    without a known interval.
    """)
  value: float64 | null;

  @doc("Number of stored points folded into this bucket.")
  @encodedName("application/json", "point_count")
  pointCount: Nami.Api.Contracts.Common.JsonInteger;
}

@doc("""
  One result stream of a metric range query. With no `group_by` the whole
  result is a single stream whose `attributes` are empty; with `group_by` there
  is one stream per distinct combination of the requested attribute values.
  """)
model MetricSeriesResult {
  @doc("The grouping key that produced this stream.")
  attributes: Attribute[];

  @doc("The aggregation over every point of the window, as if one bucket spanned it; null when none of them holds a value.")
  value?: float64 | null;

  @doc("The buckets that hold points, in time order; a bucket without points is omitted.")
  buckets: MetricBucket[];
}

@doc("The answer to a metric range query: what was asked, and the streams it produced.")
model MetricQueryResult {
  @doc("Aggregate buckets use a potentially approximate float64 projection. Read original points for exact signed int64 and native histogram data.")
  @encodedName("application/json", "numeric_representation")
  numericRepresentation: "float64";

  @doc("Whether the reducer estimates or interpolates values, beyond the stated numeric representation.")
  approximate: boolean;
  @doc("Instrument name the query ran against.")
  name: string;

  kind: MetricKind;

  unit?: string;

  aggregation: MetricAggregation;

  @doc("Bucket width actually used, which may be widened from the requested step.")
  @encodedName("application/json", "step_ms")
  stepMs: Nami.Api.Contracts.Common.JsonInteger;

  @encodedName("application/json", "start_time")
  startTime: Nami.Api.Contracts.Common.Timestamp;

  @encodedName("application/json", "end_time")
  endTime: Nami.Api.Contracts.Common.Timestamp;

  series: MetricSeriesResult[];

  @doc("Whether streams were dropped because the result exceeded the series limit.")
  truncated: boolean;
}

@doc("How a structured attribute matcher compares an attribute value.")
enum MetricMatcherOp { eq, neq, `in`, prefix, contains, exists, gt, gte, lt, lte }

@doc("""
  One structured attribute matcher. Tool inputs and saved queries carry
  matchers in this form; the HTTP routes keep their `key=value` string
  parameters, and a client serializes each matcher to one of them.
  """)
@minLength(1)
scalar MetricGroupKey extends string;

@maxLength(1024)
scalar MetricMatcherText extends string;

union MetricMatcherValue { text: MetricMatcherText, number: float64, boolean: boolean }

model MetricAttributeMatcher {
  ...OmitProperties<TelemetryMatcher, "value">;
  value?: MetricMatcherValue;
}

@doc("MetricDescriptorConflict contract.")
model MetricDescriptorConflict {
  kind: MetricKind;
  temporality: MetricTemporality;
  monotonic: boolean;
  unit?: string;
  services: string[];
}
