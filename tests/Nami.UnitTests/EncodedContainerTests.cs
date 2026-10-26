using System.Text;
using System.Text.Json;
using Nami.Api.Contracts;

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
        var json = Write(values);
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual(JsonValueKind.Null, document.RootElement.GetProperty("type")[0].ValueKind);
        Assert.AreEqual("9223372036854775807", document.RootElement.GetProperty("type")[1].GetString());
        var restored = Read(json);
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
        Assert.ThrowsExactly<JsonException>(() => Read(json));
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<long?>> Read(string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        return s_codec.Read(ref reader, typeof(IReadOnlyDictionary<string, IReadOnlyList<long?>>),
            new JsonSerializerOptions());
    }

    private static string Write(IReadOnlyDictionary<string, IReadOnlyList<long?>> values)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        s_codec.Write(writer, values, new JsonSerializerOptions());
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
