using System.Text.Json;
using System.Text.Json.Nodes;
using Nami.Api.Contracts;
using Nami.Api.Contracts.Common;
using Nami.Api.Contracts.OTel.Metrics;
using Nami.Tests.Infrastructure;
using Nami.Tools;
namespace Nami.UnitTests;

[TestClass]
[TestCategory("Unit")]
public sealed class ContractTests
{
    [TestMethod]
    [DataRow("AttributeIntValue", DisplayName = "Signed integer tag")]
    [DataRow("AttributeDoubleValue", DisplayName = "Double tag")]
    [DataRow("AttributeBytesValue", DisplayName = "Bytes tag")]
    [DataRow("AttributeKeyValueListValue", DisplayName = "Key value list tag")]
    public void Serialize_EachCase_EmitsKindTopLevel(string name)
    {
        AttributeObjectValue value = name switch
        {
            "AttributeIntValue" => new AttributeIntValue { Value = 17 },
            "AttributeDoubleValue" => new AttributeDoubleValue { Value = 3.5 },
            "AttributeBytesValue" => new AttributeBytesValue { Base64 = new byte[] { 0, 1, 2 } },
            _ => new AttributeKeyValueListValue { Values = new Dictionary<string,AttributeValue?> { ["text"] = new AttributeValue.StringValue("value") } }
        };
        var json = ContractJson.Serialize(value);
        var expected = name switch { "AttributeIntValue" => "int", "AttributeDoubleValue" => "double", "AttributeBytesValue" => "bytes", _ => "kvlist" };
        Assert.AreEqual(expected, json.GetProperty("kind").GetString());
        Assert.IsFalse(json.TryGetProperty("value", out var nested) && nested.ValueKind == JsonValueKind.Object);
    }

    [TestMethod]
    [DataRow("0", DisplayName = "Zero")]
    [DataRow("9007199254740993", DisplayName = "Beyond JavaScript exact range")]
    [DataRow("-9223372036854775808", DisplayName = "Signed minimum")]
    public void RoundTrip_EachCase_IsByteIdentical(string value)
    {
        var json = "{\"kind\":\"int\",\"value\":\"" + value + "\"}";
        var model = ContractJson.Deserialize<MetricNumberValue>(json);
        Assert.AreEqual(json, ContractJson.Serialize(model).GetRawText());
    }

    [TestMethod]
    public void AttributeMixedCases_RoundTrip_PreservesNestedValues()
    {
        AttributeValue value = new AttributeValue.ArrayValue([new AttributeValue.StringValue("quote\"slash\\"), new AttributeValue.BoolValue(true), null,
            new AttributeValue.ObjectValue(new AttributeIntValue { Value = 9223372036854775807 })]);
        var json=ContractJson.Serialize(value);
        var copied=ContractJson.Deserialize<AttributeValue>(json);
        Assert.AreEqual(json.GetRawText(),ContractJson.Serialize(copied).GetRawText());
        Assert.AreEqual("9223372036854775807",json[3].GetProperty("value").GetString());
        Assert.AreEqual(JsonValueKind.Null,json[2].ValueKind);
        Assert.AreEqual("quote\"slash\\",json[0].GetString());
    }

    [TestMethod]
    [DataRow("2026-10-24t12:00:00.123456789z", DisplayName = "Lowercase and nanosecond text")]
    [DataRow("2026-10-24T14:00:00+02:00", DisplayName = "Positive offset")]
    public void Timestamp_Spellings_PreservedAndRelativeResolved(string stamp)
    {
        var json = new JsonObject { ["watermark"] = stamp, ["complete"] = true, ["examined"] = null, ["matched"] = null, ["returned"] = null, ["truncated"] = false };
        Assert.AreEqual(stamp, ContractJson.Serialize(ContractJson.Deserialize<Freshness>(json.ToJsonString())).GetProperty("watermark").GetString());
        Assert.AreEqual(TestData.Now.AddMinutes(-15), ToolDispatcher.ParseSince("-15m", TestData.Now));
    }

    [TestMethod]
    [DataRow("9007199254740992", DisplayName = "Too large integer")]
    [DataRow("9007199254740990.5", DisplayName = "Rounded fractional integer")]
    [DataRow("1e-1000", DisplayName = "Underflowed fraction")]
    [DataRow("-1e-1000", DisplayName = "Negative underflowed fraction")]
    public void Integers_BeyondSafeRange_RefusedOrString(string token)
    {
        using var json = JsonDocument.Parse(token);
        Assert.IsNotNull(ContractJson.Validate("Common.JsonInteger", json.RootElement));
        var exact = ContractJson.Serialize<MetricNumberValue>(new AttributeIntValue { Value = 9007199254740993 });
        Assert.AreEqual("9007199254740993", exact.GetProperty("value").GetString());
    }

    [TestMethod]
    [DataRow("9007199254740991", DisplayName = "Safe maximum")]
    [DataRow("-9007199254740991", DisplayName = "Safe minimum")]
    [DataRow("100.00", DisplayName = "Integral decimal")]
    [DataRow("1e3", DisplayName = "Integral exponent")]
    public void Integers_ExactValues_Accepted(string token)
    {
        using var doc = JsonDocument.Parse(token);
        Assert.IsNull(ContractJson.Validate("Common.JsonInteger", doc.RootElement));
    }

    [TestMethod]
    public void EmptyUnion_Serialization_IsRejected() => Assert.ThrowsExactly<InvalidOperationException>(() => ContractJson.Serialize(default(MetricNumberValue)));

    [TestMethod]
    public void OptionalValue_ThreeStates_ThreeSerializations()
    {
        var absent = JsonSerializer.Serialize(new OptionalFixture(), OptionalFixtureJson.Default.OptionalFixture);
        var explicitNull = JsonSerializer.Serialize(new OptionalFixture { Value = new OptionalValue<string?>(null) }, OptionalFixtureJson.Default.OptionalFixture);
        var present = JsonSerializer.Serialize(new OptionalFixture { Value = new OptionalValue<string?>("yes") }, OptionalFixtureJson.Default.OptionalFixture);
        Assert.AreEqual("{}", absent);
        Assert.AreEqual("{\"value\":null}", explicitNull);
        Assert.AreEqual("{\"value\":\"yes\"}", present);
        Assert.IsFalse(JsonSerializer.Deserialize(absent, OptionalFixtureJson.Default.OptionalFixture)!.Value.IsDefined);
        Assert.IsTrue(JsonSerializer.Deserialize(explicitNull, OptionalFixtureJson.Default.OptionalFixture)!.Value.IsDefined);
    }
    [TestMethod]
    [DataRow("1024.0",1024,DisplayName="Integral decimal budget")]
    [DataRow("1e4",10000,DisplayName="Integral exponent budget")]
    public void IntegerInput_MathematicallyIntegral_Deserializes(string literal,int expected)
    {
        var json="{\"max_bytes\":"+literal+"}";
        var input=ContractJson.Deserialize<Nami.Api.Contracts.Tools.SourcesStatusInput>(json);
        Assert.IsNotNull(input);Assert.AreEqual(expected,input.MaxBytes);
        using var parsed=JsonDocument.Parse(literal);Assert.AreEqual(expected,ContractJson.ReadInteger(parsed.RootElement));
    }

    [TestMethod]
    [DataRow("number",DisplayName="Numeric point union")]
    [DataRow("histogram",DisplayName="Histogram point union")]
    [DataRow("exponential",DisplayName="Exponential histogram union")]
    [DataRow("summary",DisplayName="Summary point union")]
    public void MetricPoint_UnionVariants_RoundTripWithKindTag(string kind)
    {
        var json=kind switch
        {
            "number"=>"{\"kind\":\"number\",\"value\":{\"kind\":\"int\",\"value\":\"17\"}}",
            "histogram"=>"{\"kind\":\"histogram\",\"count\":\"1\",\"explicit_bounds\":[],\"bucket_counts\":[\"1\"]}",
            "exponential"=>"{\"kind\":\"exponential_histogram\",\"count\":\"1\",\"scale\":0,\"zero_count\":\"1\",\"zero_threshold\":0}",
            _=>"{\"kind\":\"summary\",\"count\":\"1\",\"sum\":0,\"quantile_values\":[]}"
        };
        var value=ContractJson.Deserialize<MetricPointData>(json);var serialized=ContractJson.Serialize(value);
        Assert.AreEqual(json,serialized.GetRawText());
    }


}

internal sealed class OptionalFixture
{
    [System.Text.Json.Serialization.JsonPropertyName("value")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.Text.Json.Serialization.JsonConverter(typeof(OptionalValueConverter<string?>))]
    public OptionalValue<string?> Value { get; init; }
}
[System.Text.Json.Serialization.JsonSerializable(typeof(OptionalFixture))]
[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class OptionalFixtureJson : System.Text.Json.Serialization.JsonSerializerContext;
