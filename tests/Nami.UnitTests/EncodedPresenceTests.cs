using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nami.Api.Contracts;

namespace Nami.UnitTests;

[TestClass]
[TestCategory("Unit")]
public sealed class EncodedPresenceTests
{
    [TestMethod]
    public void Deserialize_AbsentValue_RemainsUndefined()
    {
        var model = JsonSerializer.Deserialize("{}", PresenceContext.Default.PresenceProbe)!;
        Assert.IsFalse(model.Value.IsDefined);
        Assert.AreEqual("{}", JsonSerializer.Serialize(model, PresenceContext.Default.PresenceProbe));
    }

    [TestMethod]
    public void Deserialize_ExplicitNull_RemainsPresent()
    {
        var model = JsonSerializer.Deserialize("{\"value\":null}", PresenceContext.Default.PresenceProbe)!;
        Assert.IsTrue(model.Value.IsDefined);
        Assert.IsNull(model.Value.Value);
        Assert.AreEqual("{\"value\":null}", JsonSerializer.Serialize(model, PresenceContext.Default.PresenceProbe));
    }

    [TestMethod]
    [DataRow(long.MinValue)]
    [DataRow(long.MaxValue)]
    public void Serialize_FullRangeInteger_RoundTripsAsDecimalText(long value)
    {
        var model = new PresenceProbe { Value = new OptionalValue<long?>(value) };
        var json = JsonSerializer.Serialize(model, PresenceContext.Default.PresenceProbe);
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual(JsonValueKind.String, document.RootElement.GetProperty("value").ValueKind);
        Assert.AreEqual(value.ToString(CultureInfo.InvariantCulture),
            document.RootElement.GetProperty("value").GetString());
        Assert.AreEqual(value, JsonSerializer.Deserialize(json, PresenceContext.Default.PresenceProbe)!.Value.Value);
    }

    [TestMethod]
    [DataRow("{\"value\":9223372036854775807}")]
    [DataRow("{\"value\":\"9223372036854775808\"}")]
    [DataRow("{\"value\":\"01\"}")]
    public void Deserialize_InvalidIntegerRepresentation_ThrowsJsonException(string json)
    {
        Assert.ThrowsExactly<JsonException>(() =>
            JsonSerializer.Deserialize(json, PresenceContext.Default.PresenceProbe));
    }
}

internal sealed class PresenceProbe
{
    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonConverter(typeof(OptionalNullableValueConverter<long, Int64StringConverter>))]
    public OptionalValue<long?> Value { get; init; }
}

[JsonSerializable(typeof(PresenceProbe))]
internal sealed partial class PresenceContext : JsonSerializerContext;
