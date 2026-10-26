using System.Text.Encodings.Web;

namespace Nami.Collector.Storage;

/// <summary>
///     An attribute value as storage persists it: in attribute columns, span event and link attributes,
///     log bodies, exemplar filtered attributes and instrument metadata, and in every identifier hashed
///     from them. A string, boolean or array is written as itself; an integer, float, byte string or map
///     is a tagged <see cref="StoredObjectValue" />. Storage owns this format. Ingestion values map into
///     it at the write boundary and the API contract maps from it on read; neither serializes it.
/// </summary>
[JsonConverter(typeof(StoredAttributeValueJsonConverter))]
internal closed class StoredAttributeValue
{
    private protected StoredAttributeValue()
    {
    }

    /// <summary>
    ///     The canonical text identities hash: the persisted JSON with every map's keys in ordinal order,
    ///     relaxed escaping and floats as ECMAScript prints them; the empty value is <c>null</c>.
    ///     Resource entity identities in log identifiers consist of it, so it never changes.
    /// </summary>
    internal static string CanonicalText(StoredAttributeValue? value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer,
                   new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            WriteCanonical(writer, value);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    internal static void WriteCanonical(Utf8JsonWriter writer, StoredAttributeValue? value)
    {
        if (value is null) writer.WriteNullValue();
        else value.WriteCanonical(writer);
    }

    internal abstract void WriteCanonical(Utf8JsonWriter writer);
    internal abstract void WriteJson(Utf8JsonWriter writer, JsonSerializerOptions options);

    // ECMAScript Number::toString, so a canonical float reads the same in every language.
    internal static void WriteEcmaScriptNumber(Utf8JsonWriter writer, double value)
    {
        if (double.IsNaN(value))
        {
            writer.WriteStringValue("NaN");
            return;
        }

        if (double.IsPositiveInfinity(value))
        {
            writer.WriteStringValue("Infinity");
            return;
        }

        if (double.IsNegativeInfinity(value))
        {
            writer.WriteStringValue("-Infinity");
            return;
        }

        if (value is 0)
        {
            writer.WriteRawValue("0", true);
            return;
        }

        var round = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        var exponentIndex = round.IndexOfAny(['E', 'e']);
        var mantissa = exponentIndex < 0 ? round : round[..exponentIndex];
        var exponent = 0;
        if (exponentIndex >= 0 && !int.TryParse(round.AsSpan(exponentIndex + 1),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out exponent))
            throw new FormatException("The round-trip double exponent is not an integer.");
        var pointIndex = mantissa.IndexOf('.', StringComparison.Ordinal);
        var integerPart = pointIndex < 0 ? mantissa : mantissa[..pointIndex];
        var fraction = pointIndex < 0 ? "" : mantissa[(pointIndex + 1)..];
        var digits = (integerPart + fraction).TrimStart('0');
        var n = integerPart.TrimStart('0').Length + exponent;
        if (integerPart.TrimStart('0').Length is 0)
            n = -(fraction.Length - fraction.TrimStart('0').Length) + exponent;
        digits = digits.TrimEnd('0');
        var k = digits.Length;
        var e = n - 1;
        var text = n switch
        {
            <= 21 when k <= n => digits + new string('0', n - k),
            > 0 and <= 21 => digits[..n] + "." + digits[n..],
            > -6 and <= 0 => "0." + new string('0', -n) + digits,
            _ => (k == 1 ? digits : digits[..1] + "." + digits[1..]) + "e" + (e >= 0 ? "+" : "-") +
                 Math.Abs(e).ToString(CultureInfo.InvariantCulture)
        };

        writer.WriteRawValue((value < 0 ? "-" : "") + text, true);
    }

    internal sealed class StringValue(string value) : StoredAttributeValue
    {
        public string Value { get; } = value;
        internal override void WriteCanonical(Utf8JsonWriter writer) => writer.WriteStringValue(Value);
        internal override void WriteJson(Utf8JsonWriter writer, JsonSerializerOptions options) => writer.WriteStringValue(Value);
    }

    internal sealed class BoolValue(bool value) : StoredAttributeValue
    {
        public bool Value { get; } = value;
        internal override void WriteCanonical(Utf8JsonWriter writer) => writer.WriteBooleanValue(Value);
        internal override void WriteJson(Utf8JsonWriter writer, JsonSerializerOptions options) => writer.WriteBooleanValue(Value);
    }

    internal sealed class ArrayValue(IReadOnlyList<StoredAttributeValue?> items) : StoredAttributeValue
    {
        public IReadOnlyList<StoredAttributeValue?> Items { get; } = items;
        internal override void WriteCanonical(Utf8JsonWriter writer)
        {
            writer.WriteStartArray();
            foreach (var item in Items) WriteCanonical(writer, item);
            writer.WriteEndArray();
        }

        internal override void WriteJson(Utf8JsonWriter writer, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var item in Items)
            {
                if (item is null) writer.WriteNullValue();
                else item.WriteJson(writer, options);
            }
            writer.WriteEndArray();
        }
    }

    internal sealed class ObjectValue(StoredObjectValue value) : StoredAttributeValue
    {
        public StoredObjectValue Value { get; } = value;
        internal override void WriteCanonical(Utf8JsonWriter writer) => Value.WriteCanonical(writer);
        internal override void WriteJson(Utf8JsonWriter writer, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, Value, options.GetTypeInfo<StoredObjectValue>());
    }

}

internal sealed class StoredAttributeValueJsonConverter : JsonConverter<StoredAttributeValue>
{
    public override StoredAttributeValue? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return new StoredAttributeValue.StringValue(reader.GetString()!);
            case JsonTokenType.True:
            case JsonTokenType.False:
                return new StoredAttributeValue.BoolValue(reader.GetBoolean());
            case JsonTokenType.StartArray:
            {
                var items = new List<StoredAttributeValue?>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    items.Add(Read(ref reader, typeToConvert, options));
                return new StoredAttributeValue.ArrayValue(items);
            }
            case JsonTokenType.StartObject:
            {
                var value = JsonSerializer.Deserialize(ref reader,
                    options.GetTypeInfo<StoredObjectValue>());
                return value is null ? null : new StoredAttributeValue.ObjectValue(value);
            }
            case JsonTokenType.None:
            case JsonTokenType.EndObject:
            case JsonTokenType.EndArray:
            case JsonTokenType.PropertyName:
            case JsonTokenType.Comment:
            case JsonTokenType.Number:
            default:
                throw new JsonException($"A stored attribute value cannot start with token '{reader.TokenType}'.");
        }
    }

    public override void Write(Utf8JsonWriter writer, StoredAttributeValue value, JsonSerializerOptions options) =>
        value.WriteJson(writer, options);
}

internal partial class StoredObjectValue
{
    internal abstract void WriteCanonical(Utf8JsonWriter writer);
}

internal sealed partial class StoredIntValue
{
    internal override void WriteCanonical(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "int");
        writer.WriteString("value", Value.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }
}

internal sealed partial class StoredDoubleValue
{
    internal override void WriteCanonical(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "double");
        writer.WritePropertyName("value");
        StoredAttributeValue.WriteEcmaScriptNumber(writer, Value);
        writer.WriteEndObject();
    }
}

internal sealed partial class StoredBytesValue
{
    internal override void WriteCanonical(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("base64", Convert.ToBase64String(Base64.Span));
        writer.WriteString("type", "bytes");
        writer.WriteEndObject();
    }
}

internal sealed partial class StoredKeyValueListValue
{
    internal override void WriteCanonical(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "kvlist");
        writer.WriteStartObject("values");
        foreach (var (key, nested) in Values.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(key);
            StoredAttributeValue.WriteCanonical(writer, nested);
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
