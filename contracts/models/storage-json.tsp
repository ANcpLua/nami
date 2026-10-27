import "../common/values.tsp";
import "../emitters/csharp/lib/main.tsp";
using Nami.Api.Schema.Emit.CSharp;
namespace Nami.Models.Storage.Json;
@encode(string) scalar HistogramCount extends Nami.Models.Values.HistogramCount;
union StoredFloat64 { float64, "NaN", "Infinity", "-Infinity" }

union StoredAttributeValue {
  stringValue: string,
  boolValue: boolean,
  empty: null,
  arrayValue: StoredAttributeValue[],
  objectValue: StoredObjectValue,
}
@discriminator("type") @csharpForm("internal closed partial class")
model StoredObjectValue {}
@discriminator("type") @csharpForm("internal closed partial class")
model StoredNumberValue extends StoredObjectValue {}
@csharpForm("internal sealed partial class")
model StoredIntValue extends StoredNumberValue { type: "int"; @encode(string) value: Nami.Models.Values.AttributeInt64; }
@csharpForm("internal sealed partial class")
model StoredDoubleValue extends StoredNumberValue { type: "double"; value: StoredFloat64; }
@csharpForm("internal sealed partial class")
model StoredBytesValue extends StoredObjectValue { type: "bytes"; base64: bytes; }
@csharpForm("internal sealed partial class")
model StoredKeyValueListValue extends StoredObjectValue { type: "kvlist"; values: Record<StoredAttributeValue>; }

@discriminator("type")
@csharpForm("internal closed partial class")
model StoredMetricPointData {
}

@csharpForm("internal sealed partial class")
model StoredNumberPointData extends StoredMetricPointData {
  type: "number";
  value?: StoredNumberValue;
}

@csharpForm("internal sealed partial class")
model StoredHistogramPointData extends StoredMetricPointData {
  type: "histogram";
  count: HistogramCount;
  sum?: StoredFloat64;
  min?: StoredFloat64;
  max?: StoredFloat64;
  @encodedName("application/json", "explicit_bounds")
  explicitBounds: StoredFloat64[];
  @encodedName("application/json", "bucket_counts")
  bucketCounts: HistogramCount[];
}

@csharpForm("internal sealed partial class")
model StoredExponentialHistogramPointData extends StoredMetricPointData {
  type: "exponential_histogram";
  count: HistogramCount;
  sum?: StoredFloat64;
  min?: StoredFloat64;
  max?: StoredFloat64;
  scale: int32;
  @encodedName("application/json", "zero_count")
  zeroCount: HistogramCount;
  @encodedName("application/json", "zero_threshold")
  zeroThreshold: StoredFloat64;
  positive?: StoredExponentialBuckets;
  negative?: StoredExponentialBuckets;
}

@csharpForm("internal sealed partial class")
model StoredExponentialBuckets {
  offset: int32;
  @encodedName("application/json", "bucket_counts")
  bucketCounts: HistogramCount[];
}

@csharpForm("internal sealed partial class")
model StoredSummaryPointData extends StoredMetricPointData {
  type: "summary";
  count: HistogramCount;
  sum: StoredFloat64;
  @encodedName("application/json", "quantile_values")
  quantileValues: StoredQuantileValue[];
}

@csharpForm("internal sealed partial class")
model StoredQuantileValue {
  quantile: float64;
  value: StoredFloat64;
}

@csharpForm("internal sealed partial class")
model StoredExemplar {
  @encodedName("application/json", "time_unix_nano")
  timeUnixNano: HistogramCount;
  value: StoredNumberValue;
  @encodedName("application/json", "filtered_attributes")
  filteredAttributes: StoredAttribute[];
  @encodedName("application/json", "trace_id")
  traceId?: string;
  @encodedName("application/json", "span_id")
  spanId?: string;
}

@csharpForm("internal sealed partial class")
model StoredAttribute {
  key: string;
  value: StoredAttributeValue | null;
}
