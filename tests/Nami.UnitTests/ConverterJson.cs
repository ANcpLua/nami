using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nami.UnitTests;

/// <summary>Runs one <see cref="JsonConverter{T}" /> directly on JSON text, without a serializer context.</summary>
internal static class ConverterJson
{
    public static T Read<T>(JsonConverter<T> converter, string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        Assert.IsTrue(reader.Read());
        return converter.Read(ref reader, typeof(T), new JsonSerializerOptions())!;
    }

    public static string Write<T>(JsonConverter<T> converter, T value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) converter.Write(writer, value, new JsonSerializerOptions());
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
