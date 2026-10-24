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
internal abstract class StoredAttributeValue
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

    private static void WriteCanonical(Utf8JsonWriter writer, StoredAttributeValue? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                return;
            case StringValue text:
                writer.WriteStringValue(text.Value);
                return;
            case BoolValue flag:
                writer.WriteBooleanValue(flag.Value);
                return;
            case ArrayValue array:
                writer.WriteStartArray();
                foreach (var item in array.Items) WriteCanonical(writer, item);
                writer.WriteEndArray();
                return;
            case ObjectValue tagged:
                WriteCanonical(writer, tagged.Value);
                return;
            default:
                throw new InvalidOperationException("Unknown stored attribute value shape.");
        }
    }

    private static void WriteCanonical(Utf8JsonWriter writer, StoredObjectValue value)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case StoredIntValue integer:
                writer.WriteString("type", "int");
                writer.WriteString("value", integer.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case StoredDoubleValue number:
                writer.WriteString("type", "double");
                writer.WritePropertyName("value");
                WriteEcmaScriptNumber(writer, number.Value);
                break;
            case StoredBytesValue bytes:
                writer.WriteString("base64", Convert.ToBase64String(bytes.Base64.Span));
                writer.WriteString("type", "bytes");
                break;
            case StoredKeyValueListValue map:
                writer.WriteString("type", "kvlist");
                writer.WriteStartObject("values");
                foreach (var (key, nested) in map.Values.OrderBy(static item => item.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(key);
                    WriteCanonical(writer, nested);
                }

                writer.WriteEndObject();
                break;
            default:
                throw new InvalidOperationException("Unknown stored attribute object shape.");
        }

        writer.WriteEndObject();
    }

    // ECMAScript Number::toString, so a canonical float reads the same in every language.
    private static void WriteEcmaScriptNumber(Utf8JsonWriter writer, double value)
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

        if (value == 0)
        {
            writer.WriteRawValue("0", true);
            return;
        }

        var round = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        var exponentIndex = round.IndexOfAny(['E', 'e']);
        var mantissa = exponentIndex < 0 ? round : round[..exponentIndex];
        var exponent = exponentIndex < 0 ? 0 : int.Parse(round[(exponentIndex + 1)..], CultureInfo.InvariantCulture);
        var pointIndex = mantissa.IndexOf('.', StringComparison.Ordinal);
        var integerPart = pointIndex < 0 ? mantissa : mantissa[..pointIndex];
        var fraction = pointIndex < 0 ? "" : mantissa[(pointIndex + 1)..];
        var digits = (integerPart + fraction).TrimStart('0');
        var n = integerPart.TrimStart('0').Length + exponent;
        if (integerPart.TrimStart('0').Length == 0)
            n = -(fraction.Length - fraction.TrimStart('0').Length) + exponent;
        digits = digits.TrimEnd('0');
        var k = digits.Length;
        string text;
        if (k <= n && n <= 21) text = digits + new string('0', n - k);
        else if (n is > 0 and <= 21) text = digits[..n] + "." + digits[n..];
        else if (n is > -6 and <= 0) text = "0." + new string('0', -n) + digits;
        else
        {
            var e = n - 1;
            text = (k == 1 ? digits : digits[..1] + "." + digits[1..]) + "e" + (e >= 0 ? "+" : "-") +
                   Math.Abs(e).ToString(CultureInfo.InvariantCulture);
        }

        writer.WriteRawValue((value < 0 ? "-" : "") + text, true);
    }

    internal sealed class StringValue(string value) : StoredAttributeValue
    {
        public string Value { get; } = value;
    }

    internal sealed class BoolValue(bool value) : StoredAttributeValue
    {
        public bool Value { get; } = value;
    }

    internal sealed class ArrayValue(IReadOnlyList<StoredAttributeValue?> items) : StoredAttributeValue
    {
        public IReadOnlyList<StoredAttributeValue?> Items { get; } = items;
    }

    internal sealed class ObjectValue(StoredObjectValue value) : StoredAttributeValue
    {
        public StoredObjectValue Value { get; } = value;
    }
}

/// <summary>The tagged forms of a stored attribute value, discriminated by <c>type</c>.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(StoredIntValue), "int")]
[JsonDerivedType(typeof(StoredDoubleValue), "double")]
[JsonDerivedType(typeof(StoredBytesValue), "bytes")]
[JsonDerivedType(typeof(StoredKeyValueListValue), "kvlist")]
internal abstract class StoredObjectValue
{
    private protected StoredObjectValue()
    {
    }
}

/// <summary>An exact int64 or a float64: a metric or exemplar measurement, or a numeric attribute value.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(StoredIntValue), "int")]
[JsonDerivedType(typeof(StoredDoubleValue), "double")]
internal abstract class StoredNumberValue : StoredObjectValue
{
    private protected StoredNumberValue()
    {
    }
}

internal sealed class StoredIntValue : StoredNumberValue
{
    // Decimal text: an int64 outside ±2^53 must survive JSON readers that parse numbers as float64.
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    [JsonPropertyName("value")]
    public required long Value { get; init; }
}

internal sealed class StoredDoubleValue : StoredNumberValue
{
    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    [JsonPropertyName("value")]
    public required double Value { get; init; }
}

internal sealed class StoredBytesValue : StoredObjectValue
{
    [JsonPropertyName("base64")] public required ReadOnlyMemory<byte> Base64 { get; init; }
}

internal sealed class StoredKeyValueListValue : StoredObjectValue
{
    /// <summary>Entries in ordinal key order; the persisted bytes depend on it.</summary>
    [JsonPropertyName("values")]
    public required IReadOnlyDictionary<string, StoredAttributeValue?> Values { get; init; }
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
            default:
                throw new JsonException($"A stored attribute value cannot start with token '{reader.TokenType}'.");
        }
    }

    public override void Write(Utf8JsonWriter writer, StoredAttributeValue value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case StoredAttributeValue.StringValue text:
                writer.WriteStringValue(text.Value);
                return;
            case StoredAttributeValue.BoolValue flag:
                writer.WriteBooleanValue(flag.Value);
                return;
            case StoredAttributeValue.ArrayValue array:
                writer.WriteStartArray();
                foreach (var item in array.Items)
                {
                    if (item is null) writer.WriteNullValue();
                    else Write(writer, item, options);
                }

                writer.WriteEndArray();
                return;
            case StoredAttributeValue.ObjectValue tagged:
                JsonSerializer.Serialize(writer, tagged.Value, options.GetTypeInfo<StoredObjectValue>());
                return;
            default:
                throw new JsonException("Unknown stored attribute value shape.");
        }
    }
}
