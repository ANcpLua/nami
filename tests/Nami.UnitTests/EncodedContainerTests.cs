using System.Text.Json;
using Nami.Api.Contracts;
using static Nami.UnitTests.ConverterJson;

namespace Nami.UnitTests;

[TestClass]
[TestCategory("Unit")]
public sealed class EncodedContainerTests
{
    private static readonly EncodedMapConverter<IReadOnlyList<long?>,
        EncodedListConverter<long?, NullableEncodedValueConverter<long, Int64StringConverter>>> s_codec = new();

    [TestMethod]
    public void ReadWrite_NullableIntegerArrays_PreservesMapKeysAndExactValues()
    {
        var values = new Dictionary<string, IReadOnlyList<long?>>
        {
            ["type"] = [null, long.MaxValue], ["kind"] = [long.MinValue]
        };
        var json = Write(s_codec, values);
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual(JsonValueKind.Null, document.RootElement.GetProperty("type")[0].ValueKind);
        Assert.AreEqual("9223372036854775807", document.RootElement.GetProperty("type")[1].GetString());
        var restored = Read(s_codec, json);
        Assert.HasCount(2, restored);
        Assert.IsNull(restored["type"][0]);
        Assert.AreEqual(long.MaxValue, restored["type"][1]);
        Assert.AreEqual(long.MinValue, restored["kind"][0]);
    }

    [TestMethod]
    [DataRow("{\"type\":[9223372036854775807]}")]
    [DataRow("{\"type\":[\"9223372036854775808\"]}")]
    [DataRow("{\"type\":[],\"type\":[]}")]
    public void Read_InvalidEncodedMap_ThrowsJsonException(string json)
    {
        Assert.ThrowsExactly<JsonException>(() => Read(s_codec, json));
    }
}
