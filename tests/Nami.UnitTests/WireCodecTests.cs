using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nami.Api.Contracts;

namespace Nami.UnitTests;

[TestClass]
[TestCategory("Unit")]
public sealed class WireCodecTests
{
    private static readonly NullableEncodedReferenceConverter<IReadOnlyList<long>,
        EncodedListConverter<long, Int64StringConverter>> s_nullable = new();
    private static readonly OptionalConvertedValueConverter<IReadOnlyList<long>?,
        NullableEncodedReferenceConverter<IReadOnlyList<long>, EncodedListConverter<long, Int64StringConverter>>> s_optional = new();
    private static readonly NamedDoubleConverter s_double = new();

    [TestMethod]
    public void NullableReference_Null_RetainsExplicitNull()
    {
        Assert.IsNull(Read(s_nullable, "null"));
        Assert.AreEqual("null", Write(s_nullable, null));
    }

    [TestMethod]
    public void NullableReference_IntegerArray_PreservesBothInt64Endpoints()
    {
        const string Json = "[\"-9223372036854775808\",\"9223372036854775807\"]";
        var value = Read(s_nullable, Json);
        Assert.IsNotNull(value);
        Assert.AreSequenceEqual([long.MinValue, long.MaxValue], value);
        Assert.AreEqual(Json, Write(s_nullable, value));
    }

    [TestMethod]
    [DataRow("[1]")]
    [DataRow("[\"01\"]")]
    [DataRow("[\"9223372036854775808\"]")]
    public void NullableReference_InvalidIntegerEncoding_RejectsTheElement(string json)
    {
        Assert.ThrowsExactly<JsonException>(() => Read(s_nullable, json));
    }

    [TestMethod]
    public void OptionalReference_Absent_RejectsWritingAnUndefinedValue()
    {
        Assert.ThrowsExactly<JsonException>(() => Write(s_optional, default));
    }

    [TestMethod]
    public void OptionalReference_ExplicitNull_RemainsDefined()
    {
        var value = Read(s_optional, "null");
        Assert.IsTrue(value.IsDefined);
        Assert.IsNull(value.Value);
        Assert.AreEqual("null", Write(s_optional, value));
    }

    [TestMethod]
    public void OptionalReference_Value_UsesTheInnerIntegerCodec()
    {
        var value = Read(s_optional, "[\"9223372036854775807\"]");
        Assert.IsTrue(value.IsDefined);
        Assert.IsNotNull(value.Value);
        Assert.AreEqual(long.MaxValue, Assert.ContainsSingle(value.Value));
        Assert.AreEqual("[\"9223372036854775807\"]", Write(s_optional, value));
    }

    [TestMethod]
    [DataRow("NaN", double.NaN)]
    [DataRow("Infinity", double.PositiveInfinity)]
    [DataRow("-Infinity", double.NegativeInfinity)]
    public void NamedDouble_NonFiniteValue_PreservesTheDeclaredName(string name, double expected)
    {
        Assert.AreEqual(expected, Read(s_double, "\"" + name + "\""));
        Assert.AreEqual("\"" + name + "\"", Write(s_double, expected));
    }

    [TestMethod]
    public void NamedDouble_FiniteValue_RemainsAJsonNumber()
    {
        Assert.AreEqual(1.25, Read(s_double, "1.25"));
        Assert.AreEqual("1.25", Write(s_double, 1.25));
    }

    [TestMethod]
    public void NamedDouble_NegativeZero_PreservesTheSign()
    {
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(-0.0), BitConverter.DoubleToInt64Bits(Read(s_double, "-0")));
        Assert.AreEqual("-0", Write(s_double, -0.0));
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("true")]
    [DataRow("[]")]
    [DataRow("{}")]
    [DataRow("\"1.25\"")]
    [DataRow("\"nan\"")]
    public void NamedDouble_InvalidWireType_RejectsTheInput(string json)
    {
        Assert.ThrowsExactly<JsonException>(() => Read(s_double, json));
    }

    private static T Read<T>(JsonConverter<T> converter, string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        Assert.IsTrue(reader.Read());
        return converter.Read(ref reader, typeof(T), new JsonSerializerOptions())!;
    }

    private static string Write<T>(JsonConverter<T> converter, T value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) converter.Write(writer, value, new JsonSerializerOptions());
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
