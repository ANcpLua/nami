using System;
using System.Collections.Generic;
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
    public OptionalValue(T value)
    {
        (Value, IsDefined) = (value, true);
    }

    public T Value { get; }
    public bool IsDefined { get; }

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
