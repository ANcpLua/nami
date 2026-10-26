using System.Collections.ObjectModel;
using System.Text.Json;

namespace Nami.UnitTests.Receiver;

[TestClass]
[TestCategory("Fast")]
public class JsonReflectionGuardTests
{
    [TestMethod]
    public void Should_DisableDefaultJsonReflection_When_TheTestExecutableRuns()
    {
        Assert.IsFalse(JsonSerializer.IsReflectionEnabledByDefault);
    }

    [TestMethod]
    public void Should_PreserveIntegerPrecision_When_TelemetryUsesGeneratedMetadata()
    {
        var point = new NumberPoint(new Dictionary<string, object?>(), 1, 2, long.MaxValue, 0, []);

        using var json = JsonSerializer.SerializeToDocument(point, TelemetryJsonContext.Default.NumberPoint);

        Assert.AreEqual(long.MaxValue, json.RootElement.GetProperty("Value").GetInt64());
    }

    [TestMethod]
    public void Should_PreserveNestedTelemetryValues_When_ReflectionIsDisabled()
    {
        var attributes = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>
        {
            ["nested"] = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>
            {
                ["values"] = new ReadOnlyCollection<object?>(["first", null, true, long.MinValue]),
                ["bytes"] = new byte[] { 0, 128, 255 },
                ["nonFinite"] = double.PositiveInfinity,
                ["single"] = 0.1f
            })
        });
        var point = new NumberPoint(attributes, 1, 2, -0.0d, 0, []);

        using var json = JsonSerializer.SerializeToDocument(point, TelemetryJsonContext.Default.NumberPoint);

        var nested = json.RootElement.GetProperty("Attributes").GetProperty("nested");
        var values = nested.GetProperty("values");
        Assert.AreEqual("first", values[0].GetString());
        Assert.AreEqual(JsonValueKind.Null, values[1].ValueKind);
        Assert.IsTrue(values[2].GetBoolean());
        Assert.AreEqual(long.MinValue, values[3].GetInt64());
        Assert.AreSequenceEqual(new byte[] { 0, 128, 255 }, nested.GetProperty("bytes").GetBytesFromBase64());
        Assert.AreEqual("Infinity", nested.GetProperty("nonFinite").GetString());
        Assert.AreEqual("0.1", nested.GetProperty("single").GetRawText());
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(-0.0d),
            BitConverter.DoubleToInt64Bits(json.RootElement.GetProperty("Value").GetDouble()));
    }

    [TestMethod]
    [DataRow(double.NaN, "NaN")]
    [DataRow(double.PositiveInfinity, "Infinity")]
    [DataRow(double.NegativeInfinity, "-Infinity")]
    public void Should_PreserveNonFiniteValues_When_TelemetryUsesGeneratedMetadata(double number, string expected)
    {
        var point = new NumberPoint(new Dictionary<string, object?>(), 1, 2, number, 0, []);

        using var json = JsonSerializer.SerializeToDocument(point, TelemetryJsonContext.Default.NumberPoint);

        Assert.AreEqual(expected, json.RootElement.GetProperty("Value").GetString());
    }

    [TestMethod]
    public void Should_RejectUnknownTelemetryObjects_When_NoExplicitValueShapeExists()
    {
        var point = new NumberPoint(new Dictionary<string, object?>(), 1, 2, new UnregisteredPayload(), 0, []);

        _ = Assert.ThrowsExactly<JsonException>(() =>
            JsonSerializer.Serialize(point, TelemetryJsonContext.Default.NumberPoint));
    }

    [TestMethod]
    public void Serialize_ProducerSchemaUrl_PreservesItsOriginalText()
    {
        const string Schema = "../schema/%2f?Case=Kept";
        var resource = new ResourceRecord(new Dictionary<string, object?>())
        {
            SchemaUrl = Schema,
            EntityReferences = [new ResourceEntityReference("service", ["service.name"]) { SchemaUrl = Schema }]
        };
        var span = new SpanRecord(TestSpan.TraceId, "0011223344556677", "schema")
        {
            Resource = resource,
            Scope = new InstrumentationScopeRecord { SchemaUrl = Schema }
        };
        using var json = JsonSerializer.SerializeToDocument(new TelemetrySnapshot([span], [], [], []),
            TelemetryJsonContext.Default.TelemetrySnapshot);
        var written = json.RootElement.GetProperty("Spans")[0];
        Assert.AreEqual(Schema, written.GetProperty("Resource").GetProperty("SchemaUrl").GetString());
        Assert.AreEqual(Schema, written.GetProperty("Scope").GetProperty("SchemaUrl").GetString());
        Assert.AreEqual(Schema, written.GetProperty("Resource").GetProperty("EntityReferences")[0]
            .GetProperty("SchemaUrl").GetString());
    }

    private sealed class UnregisteredPayload;
}
