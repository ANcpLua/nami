import "../common/values.tsp";
import "../emitters/csharp/lib/main.tsp";
using Nami.Api.Schema.Emit.CSharp;
namespace Nami.Models.Ingestion;
model AttributeRename { originalKey: string; canonicalKey: string | null; }
@slotValue
union OtlpAttributeValue {
  Empty: null, String: string, Bool: boolean, Int: Nami.Models.Values.AttributeInt64, Double: float64,
  Bytes: bytes, Array: OtlpAttributeValue[], KeyValueList: Record<OtlpAttributeValue>
}
@csharpType("Nami.Collector.Ingestion.Models.Attribute") model AttributeReference {}
@csharpType("Nami.Collector.Ingestion.Models.MetricExemplar") model ExemplarReference {}
model NormalizedAttributes {
  @csharpType("Dictionary<string, OtlpAttributeValue>") values: Record<OtlpAttributeValue> = #{};
  @csharpType("List<AttributeRename>") renames: AttributeRename[] = #[];
}
model NormalizedContractAttributes {
  @csharpType("List<Nami.Collector.Ingestion.Models.Attribute>") values: AttributeReference[] = #[];
  @csharpType("List<AttributeRename>") renames: AttributeRename[] = #[];
}
model NormalizedMetricExemplars {
  @csharpType("List<Nami.Collector.Ingestion.Models.MetricExemplar>") values: ExemplarReference[] = #[];
  @csharpType("List<AttributeRename>") renames: AttributeRename[] = #[];
}
model LogTimeOrigin {
  producerEventTime?: Nami.Models.Values.UnixNanos;
  producerObservedTime?: Nami.Models.Values.UnixNanos;
  collectorReceivedTime?: Nami.Models.Values.UnixNanos;
  untimedRequestOrdinal?: int32;
}
