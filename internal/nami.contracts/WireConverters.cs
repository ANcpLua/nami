using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nami.Api.Contracts;

/// <summary>String enums never admit integer ordinals or undefined numeric values.</summary>
public sealed class StringEnumConverter<T>() : JsonStringEnumConverter<T>(namingPolicy: null, allowIntegerValues: false) where T : struct, Enum;

public sealed class Int64StringConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ||
            text != value.ToString(CultureInfo.InvariantCulture)) throw new JsonException("Expected a canonical signed 64-bit decimal string.");
        return value;
    }
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}

public sealed class UInt64StringConverter : JsonConverter<ulong>
{
    public override ulong Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        if (!ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
            text != value.ToString(CultureInfo.InvariantCulture)) throw new JsonException("Expected a canonical unsigned 64-bit decimal string.");
        return value;
    }
    public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}

public sealed class IntegerStringListConverter<T> : JsonConverter<IReadOnlyList<T>> where T : struct, System.Numerics.IBinaryInteger<T>
{
    public override IReadOnlyList<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected an array of decimal strings.");
        var values = new List<T>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            if (!T.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ||
                text != value.ToString(null, CultureInfo.InvariantCulture)) throw new JsonException("Expected a canonical integer string.");
            values.Add(value);
        }
        if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Unterminated integer array.");
        return values;
    }
    public override void Write(Utf8JsonWriter writer, IReadOnlyList<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value) writer.WriteStringValue(item.ToString(null, CultureInfo.InvariantCulture));
        writer.WriteEndArray();
    }
}

/// <summary>Distinguishes an absent optional member from an explicitly present null.</summary>
public readonly record struct OptionalValue<T>
{
    public OptionalValue(T value) => (Value, IsDefined) = (value, true);
    public T Value { get; }
    public bool IsDefined { get; }
    public static implicit operator OptionalValue<T>(T value) => new(value);
}

public sealed class OptionalValueConverter<T> : JsonConverter<OptionalValue<T>>
{
    public override bool HandleNull => true;
    public override OptionalValue<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(JsonSerializer.Deserialize(ref reader, (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)options.GetTypeInfo(typeof(T)))!);
    public override void Write(Utf8JsonWriter writer, OptionalValue<T> value, JsonSerializerOptions options)
    {
        if (!value.IsDefined) throw new JsonException("An undefined optional value must be omitted by its containing property.");
        JsonSerializer.Serialize(writer, value.Value, (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)options.GetTypeInfo(typeof(T)));
    }
}
