using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nami.Api.Contracts;

/// <summary>String enums never admit integer ordinals or undefined numeric values.</summary>
public sealed class StringEnumConverter<T>() : JsonStringEnumConverter<T>(null, false) where T : struct, Enum;

public sealed class Int64StringConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ||
            text != value.ToString(CultureInfo.InvariantCulture))
            throw new JsonException("Expected a canonical signed 64-bit decimal string.");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}

public sealed class UInt64StringConverter : JsonConverter<ulong>
{
    public override ulong Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        if (!ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
            text != value.ToString(CultureInfo.InvariantCulture))
            throw new JsonException("Expected a canonical unsigned 64-bit decimal string.");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}

public sealed class IntegerStringListConverter<T> : JsonConverter<IReadOnlyList<T>> where T : struct, IBinaryInteger<T>
{
    public override IReadOnlyList<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected an array of decimal strings.");
        var values = new List<T>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            if (!T.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ||
                text != value.ToString(null, CultureInfo.InvariantCulture))
                throw new JsonException("Expected a canonical integer string.");
            values.Add(value);
        }

        return reader.TokenType != JsonTokenType.EndArray ? throw new JsonException("Unterminated integer array.") : values;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value) writer.WriteStringValue(item.ToString(null, CultureInfo.InvariantCulture));
        writer.WriteEndArray();
    }
}

/// <summary>Distinguishes an absent optional member from an explicitly present null.</summary>
public readonly record struct OptionalValue<T>(T Value)
{
    public T Value { get; } = Value;
    public bool IsDefined { get; } = true;

    public static implicit operator OptionalValue<T>(T value)
    {
        return new OptionalValue<T>(value);
    }
}

public sealed class OptionalValueConverter<T> : JsonConverter<OptionalValue<T>>
{
    public override bool HandleNull => true;

    public override OptionalValue<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return new OptionalValue<T>(JsonSerializer.Deserialize(ref reader, options.GetTypeInfo<T>())!);
    }

    public override void Write(Utf8JsonWriter writer, OptionalValue<T> value, JsonSerializerOptions options)
    {
        if (!value.IsDefined)
            throw new JsonException("An undefined optional value must be omitted by its containing property.");
        JsonSerializer.Serialize(writer, value.Value, options.GetTypeInfo<T>());
    }
}

/// <summary>Reads JSON Schema integers by mathematical value, including integral decimal/exponent tokens.</summary>
public sealed class ExactIntegerConverter<T> : JsonConverter<T> where T : struct, IBinaryInteger<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var number = document.RootElement;
        if (number.ValueKind != JsonValueKind.Number || !ContractJson.IsExactInteger(number) ||
            !number.TryGetDecimal(out var exact))
            throw new JsonException("Expected an exact JSON integer.");
        try
        {
            return T.CreateChecked(exact);
        }
        catch (OverflowException error)
        {
            throw new JsonException("JSON integer is outside the target range.", error);
        }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteRawValue(value.ToString(null, CultureInfo.InvariantCulture));
    }
}

/// <summary>Presence and nullable values compose with the property's exact inner codec.</summary>
public sealed class OptionalNullableValueConverter<T, TConverter> : JsonConverter<OptionalValue<T?>>
    where T : struct where TConverter : JsonConverter<T>, new()
{
    private static readonly TConverter s_inner = new();
    public override bool HandleNull => true;

    public override OptionalValue<T?> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return new OptionalValue<T?>(reader.TokenType == JsonTokenType.Null
            ? null
            : s_inner.Read(ref reader, typeof(T), options));
    }

    public override void Write(Utf8JsonWriter writer, OptionalValue<T?> value, JsonSerializerOptions options)
    {
        if (!value.IsDefined) throw new JsonException("An undefined optional value must be omitted.");
        if (value.Value is { } present) s_inner.Write(writer, present, options);
        else writer.WriteNullValue();
    }
}

/// <summary>Composes the explicitly selected value codec with nullable value presence.</summary>
public sealed class NullableEncodedValueConverter<T, TConverter> : JsonConverter<T?>
    where T : struct where TConverter : JsonConverter<T>, new()
{
    private static readonly TConverter s_inner = new();
    public override bool HandleNull => true;

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.Null ? null : s_inner.Read(ref reader, typeof(T), options);
    }

    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
    {
        if (value is { } present) s_inner.Write(writer, present, options);
        else writer.WriteNullValue();
    }
}

public sealed class NullableEncodedReferenceConverter<T, TConverter> : JsonConverter<T?>
    where T : class where TConverter : JsonConverter<T>, new()
{
    private static readonly TConverter s_inner = new();
    public override bool HandleNull => true;

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.Null ? null : s_inner.Read(ref reader, typeof(T), options);
    }

    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else s_inner.Write(writer, value, options);
    }
}

public sealed class EncodedListConverter<T, TConverter> : JsonConverter<IReadOnlyList<T>>
    where TConverter : JsonConverter<T>, new()
{
    private static readonly TConverter s_inner = new();

    public override IReadOnlyList<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected an encoded array.");
        var result = new List<T>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) return result;
            result.Add(s_inner.Read(ref reader, typeof(T), options)!);
        }

        throw new JsonException("Unterminated encoded array.");
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value) s_inner.Write(writer, item, options);
        writer.WriteEndArray();
    }
}

public sealed class EncodedMapConverter<T, TConverter> : JsonConverter<IReadOnlyDictionary<string, T>>
    where TConverter : JsonConverter<T>, new()
{
    private static readonly TConverter s_inner = new();

    public override IReadOnlyDictionary<string, T> Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected an encoded map.");
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return result;
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a map key.");
            var key = reader.GetString()!;
            if (!reader.Read()) throw new JsonException("Missing map value.");
            if (!result.TryAdd(key, s_inner.Read(ref reader, typeof(T), options)!))
                throw new JsonException("Duplicate map key.");
        }

        throw new JsonException("Unterminated encoded map.");
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<string, T> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var pair in value)
        {
            writer.WritePropertyName(pair.Key);
            s_inner.Write(writer, pair.Value, options);
        }

        writer.WriteEndObject();
    }
}

public sealed class OptionalConvertedValueConverter<T, TConverter> : JsonConverter<OptionalValue<T>>
    where TConverter : JsonConverter<T>, new()
{
    private static readonly TConverter s_inner = new();
    public override bool HandleNull => true;

    public override OptionalValue<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return new OptionalValue<T>(reader.TokenType == JsonTokenType.Null
            ? default!
            : s_inner.Read(ref reader, typeof(T), options)!);
    }

    public override void Write(Utf8JsonWriter writer, OptionalValue<T> value, JsonSerializerOptions options)
    {
        if (!value.IsDefined) throw new JsonException("An undefined optional value must be omitted.");
        if (value.Value is null) writer.WriteNullValue();
        else s_inner.Write(writer, value.Value, options);
    }
}

public sealed class NamedDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetDouble(),
            JsonTokenType.String => reader.GetString() switch
            {
                "NaN" => double.NaN,
                "Infinity" => double.PositiveInfinity,
                "-Infinity" => double.NegativeInfinity,
                _ => throw new JsonException("Expected a named nonfinite value.")
            },
            JsonTokenType.None or JsonTokenType.StartObject or JsonTokenType.EndObject or
                JsonTokenType.StartArray or JsonTokenType.EndArray or JsonTokenType.PropertyName or
                JsonTokenType.Comment or JsonTokenType.True or JsonTokenType.False or JsonTokenType.Null =>
                throw new JsonException("Expected a float64 or a named nonfinite value."),
            _ => throw new JsonException("Expected a float64 or a named nonfinite value.")
        };
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        if (double.IsFinite(value)) writer.WriteNumberValue(value);
        else
        {
            writer.WriteStringValue(double.IsNaN(value) ? "NaN" :
                double.IsPositiveInfinity(value) ? "Infinity" : "-Infinity");
        }
    }
}
