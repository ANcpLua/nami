using System.Text.Json;
using Nami.Api.Contracts;
using Nami.Api.Contracts.Common;
using Nami.Collector.Ingestion;
using Nami.Collector.Ingestion.Models;
using Nami.Collector.Ingestion.Primitives;
using Nami.Collector.Storage;
using AttributeIntValue = Nami.Collector.Ingestion.Models.AttributeIntValue;

namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public sealed class GeneratedModelTests
{
    [TestMethod]
    public void ToSpanStorageRows_CopiedValues_PreservesExplicitDeprecationProvenance()
    {
        var attributes = new NormalizedAttributes
        {
            Renames = [new AttributeRename { OriginalKey = "obsolete.key", CanonicalKey = null }]
        };
        var copy = attributes with
        {
            Values = new Dictionary<string, OtlpAttributeValue>(attributes.Values, StringComparer.Ordinal)
        };
        var span = new SpanIngestionRecord
        {
            SpanId = "0123456789abcdef", TraceId = "0123456789abcdef0123456789abcdef",
            Name = "probe", ServiceName = "test", StartTimeUnixNano = 1, EndTimeUnixNano = 2,
            Attributes = copy, ResourceAttributes = new NormalizedAttributes()
        };
        var row = IngestionStorageMapper.ToSpanStorageRows(new TraceIngestionBatch([span]))[0];
        Assert.HasCount(1, row.AttributeRenames);
        Assert.AreEqual("obsolete.key", row.AttributeRenames[0].OriginalKey);
        Assert.IsNull(row.AttributeRenames[0].CanonicalKey);
    }

    [TestMethod]
    public void ToStored_RecursiveReservedMapKeys_RemainData()
    {
        var value = OtlpAttributeValue.FromKeyValueList(new Dictionary<string, OtlpAttributeValue>
        {
            ["type"] = OtlpAttributeValue.FromInt(long.MaxValue),
            ["kind"] = OtlpAttributeValue.FromArray([OtlpAttributeValue.Empty, OtlpAttributeValue.FromBytes([0, 255])])
        });
        var stored = StoredRecordMapper.ToStored(value);
        var json = JsonSerializer.Serialize(stored, StorageJsonSerializerContext.Default.StoredAttributeValue);
        using var document = JsonDocument.Parse(json);
        var map = document.RootElement.GetProperty("values");
        Assert.AreEqual("9223372036854775807", map.GetProperty("type").GetProperty("value").GetString());
        Assert.AreEqual(JsonValueKind.Null, map.GetProperty("kind")[0].ValueKind);
        Assert.AreEqual("AP8=", map.GetProperty("kind")[1].GetProperty("base64").GetString());
    }

    [TestMethod]
    public void ToContract_ReservedMapKeys_ChangesOnlyValueTags()
    {
        var stored = new StoredAttributeValue.ObjectValue(new StoredKeyValueListValue
        {
            Values = new Dictionary<string, StoredAttributeValue?>
            {
                ["type"] = new StoredAttributeValue.ObjectValue(new StoredIntValue { Value = long.MaxValue }),
                ["kind"] = new StoredAttributeValue.StringValue("kept")
            }
        });
        var json = JsonSerializer.Serialize(StoredAttributeProjection.ToContract(stored),
            ContractTypes.TypeInfo<AttributeValue?>());
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual("kvlist", document.RootElement.GetProperty("kind").GetString());
        var values = document.RootElement.GetProperty("values");
        Assert.AreEqual("int", values.GetProperty("type").GetProperty("kind").GetString());
        Assert.AreEqual("9223372036854775807", values.GetProperty("type").GetProperty("value").GetString());
        Assert.AreEqual("kept", values.GetProperty("kind").GetString());
    }

    [TestMethod]
    public void ToStored_GaugeAndSum_SharePayloadWithoutConflatingKinds()
    {
        var data = new MetricNumberPointData { Value = new AttributeIntValue { Value = long.MaxValue } };
        var gauge = new GaugeMetric { Data = data };
        var sum = new SumMetric { Data = data, IsMonotonic = true, Temporality = MetricTemporality.Delta };
        var first = JsonSerializer.Serialize(IngestionMappings.ToStored(gauge.Data),
            StorageJsonSerializerContext.Default.StoredMetricPointData);
        var second = JsonSerializer.Serialize(IngestionMappings.ToStored(sum.Data),
            StorageJsonSerializerContext.Default.StoredMetricPointData);
        Assert.AreEqual(first, second);
        using var document = JsonDocument.Parse(first);
        Assert.AreEqual("number", document.RootElement.GetProperty("type").GetString());
        Assert.AreEqual("9223372036854775807",
            document.RootElement.GetProperty("value").GetProperty("value").GetString());
        Assert.IsTrue(sum.IsMonotonic);
    }
}
