import "./metric-kinds.tsp";
import "./ingestion.tsp";
import "./storage-json.tsp";
using Nami.Api.Schema.Emit.CSharp;
namespace Nami.Models.Ingestion.Metrics;
alias S = Nami.Models.Storage.Json;
@csharpForm("internal closed partial class") model MetricPointData {}
@csharpForm("internal closed partial class") model MetricNumberValue {}
@csharpForm("internal sealed partial class") model AttributeIntValue extends MetricNumberValue { @encode(string) value: int64; }
@csharpForm("internal sealed partial class") model AttributeDoubleValue extends MetricNumberValue { value: S.StoredFloat64; }
@csharpForm("internal sealed partial class") model MetricNumberPointData extends MetricPointData { value?: MetricNumberValue; }
@csharpForm("internal sealed partial class") model MetricHistogramPointData extends MetricPointData { ...OmitProperties<S.StoredHistogramPointData, "type">; }
@csharpForm("internal sealed partial class") model MetricExponentialBuckets { ...S.StoredExponentialBuckets; }
@csharpForm("internal sealed partial class") model MetricExponentialHistogramPointData extends MetricPointData {
  ...OmitProperties<S.StoredExponentialHistogramPointData, "type" | "positive" | "negative">;
  positive?: MetricExponentialBuckets;
  negative?: MetricExponentialBuckets;
}
@csharpForm("internal sealed partial class") model MetricQuantileValue { ...S.StoredQuantileValue; }
@csharpForm("internal sealed partial class") model MetricSummaryPointData extends MetricPointData {
  ...OmitProperties<S.StoredSummaryPointData, "type" | "quantileValues">;
  @encodedName("application/json", "quantile_values") quantileValues: MetricQuantileValue[];
}
@csharpForm("internal sealed partial class") model Attribute { key: string; value: Nami.Models.Ingestion.OtlpAttributeValue; }
@csharpForm("internal sealed partial class") model MetricExemplar {
  timeUnixNano: uint64;
  value: MetricNumberValue;
  filteredAttributes: Attribute[];
  traceId?: string;
  spanId?: string;
}
alias Temporality = Nami.Models.Ingestion.Primitives.MetricTemporality;
@csharpForm("internal closed partial class") model MetricShape {}
@csharpForm("internal sealed partial class") model GaugeMetric extends MetricShape { data: MetricNumberPointData; }
@csharpForm("internal sealed partial class") model SumMetric extends MetricShape { data: MetricNumberPointData; temporality: Temporality; isMonotonic: boolean; }
@csharpForm("internal sealed partial class") model HistogramMetric extends MetricShape { data: MetricHistogramPointData; temporality: Temporality; }
@csharpForm("internal sealed partial class") model ExponentialHistogramMetric extends MetricShape { data: MetricExponentialHistogramPointData; temporality: Temporality; }
@csharpForm("internal sealed partial class") model SummaryMetric extends MetricShape { data: MetricSummaryPointData; }
