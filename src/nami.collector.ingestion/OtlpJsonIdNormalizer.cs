namespace Nami.Collector.Ingestion;

/// <summary>
/// Rewrites the hex-encoded id fields of an OTLP/JSON payload to base64 so the protojson parser
/// yields the bytes the sender meant.
/// </summary>
/// <remarks>
/// The OTLP spec special-cases trace and span ids in JSON to hex, while proto3's JSON mapping
/// (which <c>Google.Protobuf.JsonParser</c> implements) decodes every <c>bytes</c> field as base64.
/// Without this rewrite a spec-compliant hex id happens to also be valid base64 and silently
/// decodes to the wrong bytes — mangled, unjoinable ids. The contract is strict spec-hex: a
/// non-hex or wrong-length id throws <see cref="InvalidDataException"/>, which the ingest
/// endpoints surface as a 400, never a mangled row.
/// <para>
/// Strings are read the way OTLP 1.11.1 asks a decoder to: an invalid UTF-8 sequence and an
/// escape of an unpaired surrogate become U+FFFD, where <see cref="Utf8JsonReader.GetString"/>
/// would refuse the whole payload.
/// </para>
/// </remarks>
internal static class OtlpJsonIdNormalizer
{
    private const int TraceIdBytes = 16;
    private const int SpanIdBytes = 8;
    private const int ReplacementCharacter = 0xFFFD;

    public static string NormalizeIdsToProtoJson(ReadOnlySpan<byte> utf8Json)
    {
        // Accept an optional UTF-8 BOM before protojson parsing.
        ReadOnlySpan<byte> utf8Bom = [0xEF, 0xBB, 0xBF];
        if (utf8Json.StartsWith(utf8Bom))
            utf8Json = utf8Json[utf8Bom.Length..];

        if (utf8Json.IsEmpty)
            throw new InvalidDataException("OTLP/JSON payload is empty.");

        var reader = new Utf8JsonReader(utf8Json, new JsonReaderOptions { MaxDepth = 128 });
        var buffer = new ArrayBufferWriter<byte>(utf8Json.Length);
        using var writer = new Utf8JsonWriter(buffer);

        // Protojson must ignore unknown fields, including future objects that happen to contain
        // properties named traceId or spanId. Track the containing OTLP message path so
        // only ids owned by known signal shapes are validated and rewritten.
        var path = new List<string>(8);
        var containerSegments = new Stack<bool>();
        string? pendingProperty = null;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    var name = ReadString(ref reader);
                    writer.WritePropertyName(name);
                    pendingProperty = name;
                    continue;

                case JsonTokenType.String:
                    var value = ReadString(ref reader);
                    var expectedBytes = pendingProperty is { } property
                        ? ExpectedIdByteLength(path, property)
                        : 0;
                    if (expectedBytes > 0)
                        WriteIdAsBase64(writer, value, expectedBytes, pendingProperty!);
                    else
                        writer.WriteStringValue(value);
                    break;

                case JsonTokenType.Number:
                    writer.WriteRawValue(reader.ValueSpan, skipInputValidation: true);
                    break;

                case JsonTokenType.True:
                    writer.WriteBooleanValue(true);
                    break;

                case JsonTokenType.False:
                    writer.WriteBooleanValue(false);
                    break;

                case JsonTokenType.Null:
                    writer.WriteNullValue();
                    break;

                case JsonTokenType.StartObject:
                    writer.WriteStartObject();
                    PushContainerPath(path, containerSegments, ref pendingProperty);
                    break;

                case JsonTokenType.EndObject:
                    writer.WriteEndObject();
                    PopContainerPath(path, containerSegments);
                    break;

                case JsonTokenType.StartArray:
                    writer.WriteStartArray();
                    PushContainerPath(path, containerSegments, ref pendingProperty);
                    break;

                case JsonTokenType.EndArray:
                    writer.WriteEndArray();
                    PopContainerPath(path, containerSegments);
                    break;
            }

            pendingProperty = null;
        }

        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // The reader runs over one span, so the token's bytes are always in ValueSpan. Encoding.UTF8
    // decodes each maximal invalid subsequence as one U+FFFD.
    private static string ReadString(ref Utf8JsonReader reader) =>
        reader.ValueIsEscaped
            ? Unescape(reader.ValueSpan)
            : Encoding.UTF8.GetString(reader.ValueSpan);

    private static string Unescape(ReadOnlySpan<byte> escaped)
    {
        // Unescaping never grows the text: \uXXXX (6 bytes) encodes to at most 3, a pair (12) to 4.
        Span<byte> unescaped = escaped.Length <= 512 ? stackalloc byte[512] : new byte[escaped.Length];
        var written = 0;
        for (var i = 0; i < escaped.Length; i++)
        {
            if (escaped[i] is not (byte)'\\')
            {
                unescaped[written++] = escaped[i];
                continue;
            }

            // The reader validated every escape before handing the token over.
            switch (escaped[++i])
            {
                case (byte)'b':
                    unescaped[written++] = (byte)'\b';
                    break;
                case (byte)'f':
                    unescaped[written++] = (byte)'\f';
                    break;
                case (byte)'n':
                    unescaped[written++] = (byte)'\n';
                    break;
                case (byte)'r':
                    unescaped[written++] = (byte)'\r';
                    break;
                case (byte)'t':
                    unescaped[written++] = (byte)'\t';
                    break;
                case (byte)'u':
                    var scalar = ReadHex(escaped.Slice(i + 1, 4));
                    i += 4;
                    if (char.IsHighSurrogate((char)scalar) &&
                        escaped.Length - i > 6 &&
                        escaped[i + 1] is (byte)'\\' &&
                        escaped[i + 2] is (byte)'u' &&
                        ReadHex(escaped.Slice(i + 3, 4)) is var low &&
                        char.IsLowSurrogate((char)low))
                    {
                        scalar = char.ConvertToUtf32((char)scalar, (char)low);
                        i += 6;
                    }
                    else if (char.IsSurrogate((char)scalar))
                    {
                        scalar = ReplacementCharacter;
                    }

                    written += new Rune(scalar).EncodeToUtf8(unescaped[written..]);
                    break;
                default:
                    // \" \\ and \/ stand for themselves.
                    unescaped[written++] = escaped[i];
                    break;
            }
        }

        return Encoding.UTF8.GetString(unescaped[..written]);
    }

    private static int ReadHex(ReadOnlySpan<byte> utf8Hex) =>
        int.Parse(utf8Hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

    private static void PushContainerPath(
        List<string> path,
        Stack<bool> containerSegments,
        ref string? pendingProperty)
    {
        var addsSegment = pendingProperty is not null;
        if (addsSegment)
            path.Add(pendingProperty!);

        containerSegments.Push(addsSegment);
        pendingProperty = null;
    }

    private static void PopContainerPath(List<string> path, Stack<bool> containerSegments)
    {
        if (containerSegments.Pop())
            path.RemoveAt(path.Count - 1);
    }

    // protojson accepts both lowerCamelCase JSON names and original proto field names.
    private static int ExpectedIdByteLength(IReadOnlyList<string> path, string propertyName)
    {
        if (IsTraceSpanPath(path))
        {
            if (IsName(propertyName, "traceId", "trace_id")) return TraceIdBytes;
            if (IsName(propertyName, "spanId", "span_id") ||
                IsName(propertyName, "parentSpanId", "parent_span_id"))
                return SpanIdBytes;
        }

        if (IsTraceLinkPath(path) || IsLogRecordPath(path) || IsMetricExemplarPath(path))
        {
            if (IsName(propertyName, "traceId", "trace_id")) return TraceIdBytes;
            if (IsName(propertyName, "spanId", "span_id")) return SpanIdBytes;
        }

        return 0;
    }

    private static bool IsTraceSpanPath(IReadOnlyList<string> path) =>
        path.Count is 3 &&
        IsName(path[0], "resourceSpans", "resource_spans") &&
        IsName(path[1], "scopeSpans", "scope_spans") &&
        IsName(path[2], "spans", "spans");

    private static bool IsTraceLinkPath(IReadOnlyList<string> path) =>
        path.Count is 4 &&
        IsTraceSpanPathPrefix(path) &&
        IsName(path[3], "links", "links");

    private static bool IsTraceSpanPathPrefix(IReadOnlyList<string> path) =>
        IsName(path[0], "resourceSpans", "resource_spans") &&
        IsName(path[1], "scopeSpans", "scope_spans") &&
        IsName(path[2], "spans", "spans");

    private static bool IsLogRecordPath(IReadOnlyList<string> path) =>
        path.Count is 3 &&
        IsName(path[0], "resourceLogs", "resource_logs") &&
        IsName(path[1], "scopeLogs", "scope_logs") &&
        IsName(path[2], "logRecords", "log_records");

    private static bool IsMetricExemplarPath(IReadOnlyList<string> path) =>
        path.Count is 6 &&
        IsName(path[0], "resourceMetrics", "resource_metrics") &&
        IsName(path[1], "scopeMetrics", "scope_metrics") &&
        IsName(path[2], "metrics", "metrics") &&
        IsMetricDataName(path[3]) &&
        IsName(path[4], "dataPoints", "data_points") &&
        IsName(path[5], "exemplars", "exemplars");

    private static bool IsMetricDataName(string name) =>
        IsName(name, "gauge", "gauge") ||
        IsName(name, "sum", "sum") ||
        IsName(name, "histogram", "histogram") ||
        IsName(name, "exponentialHistogram", "exponential_histogram");

    private static bool IsName(string actual, string jsonName, string protoName) =>
        string.Equals(actual, jsonName, StringComparison.Ordinal) ||
        string.Equals(actual, protoName, StringComparison.Ordinal);

    private static void WriteIdAsBase64(Utf8JsonWriter writer, string hex, int expectedBytes, string field)
    {
        // Empty means absent (e.g. a root span's parentSpanId); protojson maps it to empty bytes.
        if (hex.Length is 0)
        {
            writer.WriteStringValue(hex);
            return;
        }

        if (hex.Length != expectedBytes * 2)
        {
            throw new InvalidDataException(
                $"OTLP/JSON field '{field}' must be {expectedBytes * 2} hex characters ({expectedBytes} bytes); got {hex.Length} characters.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            throw new InvalidDataException(
                $"OTLP/JSON field '{field}' must be hex-encoded per the OTLP spec; got a non-hex value.");
        }

        writer.WriteBase64StringValue(bytes);
    }
}
