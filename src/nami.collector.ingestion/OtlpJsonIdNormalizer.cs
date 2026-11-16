namespace Nami.Collector.Ingestion;

/// <summary>
///     Rewrites the hex-encoded id fields of an OTLP/JSON payload to base64 so the protojson parser
///     yields the bytes the sender meant.
/// </summary>
/// <remarks>
///     The OTLP spec special-cases trace and span ids in JSON to hex, while proto3's JSON mapping
///     (which <c>Google.Protobuf.JsonParser</c> implements) decodes every <c>bytes</c> field as base64.
///     Without this rewrite a spec-compliant hex id happens to also be valid base64 and silently
///     decodes to the wrong bytes — mangled, unjoinable ids. The contract is strict spec-hex: a
///     non-hex or wrong-length id throws <see cref="InvalidDataException" />, which the ingest
///     endpoints surface as a 400, never a mangled row.
///     <para>
///         The same pass keeps 64-bit integers exact: an integer sent as a JSON number too long for a
///         double is passed on in its string form, which protojson reads without a double in between.
///     </para>
///     <para>
///         Strings are read the way OTLP 1.11.1 asks a decoder to: an invalid UTF-8 sequence and an
///         escape of an unpaired surrogate become U+FFFD, where <see cref="Utf8JsonReader.GetString" />
///         would refuse the whole payload.
///     </para>
/// </remarks>
internal static class OtlpJsonIdNormalizer
{
    private const int TraceIdBytes = 16;
    private const int SpanIdBytes = 8;
    private const int ReplacementCharacter = 0xFFFD;

    /// <summary>The payload as protojson, in UTF-8: read through a reader, with no string of the whole document.</summary>
    public static ArraySegment<byte> NormalizeIdsToProtoJsonUtf8(ReadOnlySpan<byte> utf8Json)
    {
        // Accept an optional UTF-8 BOM before protojson parsing.
        ReadOnlySpan<byte> utf8Bom = [0xEF, 0xBB, 0xBF];
        if (utf8Json.StartsWith(utf8Bom))
            utf8Json = utf8Json[utf8Bom.Length..];

        if (utf8Json.IsEmpty)
            throw new InvalidDataException("OTLP/JSON payload is empty.");

        // Only Google.Protobuf's JSON parser reads the output, and every string and member name it gets is the token
        // as it was sent: no encoder stands in between. System.Text.Json's writer would escape a character outside
        // the Basic Multilingual Plane (four bytes) as twelve bytes under every encoder it has, and <, >, &, ', +
        // and everything outside ASCII as six bytes each under its default one.
        // Replacing is the one thing that makes the output much longer than the input, three bytes for one, so a
        // document that is not UTF-8 is measured first, to the byte: its replaced length, and two quotes for every
        // integer that becomes a string. The buffer then never grows; one that had to would be copied whole
        // (evaluator 17, X13: 48 MiB of replaced text and forty such integers cost +190 MiB). A document that is
        // UTF-8 is at most its own length, but for those integers.
        var options = new JsonReaderOptions { MaxDepth = 128 };
        var output = new Output(System.Text.Unicode.Utf8.IsValid(utf8Json)
            ? utf8Json.Length
            : checked(OtlpUtf8Replacement.ReplacedLength(utf8Json) + 2 * CountIntegersWrittenAsStrings(utf8Json, options)));

        var reader = new Utf8JsonReader(utf8Json, options);
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
                    output.Separate();
                    WriteStringToken(ref output, reader.ValueSpan, reader.ValueIsEscaped);
                    output.NameEnd();
                    pendingProperty = ReadName(ref reader);
                    continue;

                case JsonTokenType.String:
                    var expectedBytes = pendingProperty is not null
                        ? ExpectedIdByteLength(path, pendingProperty)
                        : 0;
                    output.Separate();
                    if (expectedBytes > 0)
                        WriteIdAsBase64(ref output, ReadString(ref reader), expectedBytes, pendingProperty!);
                    else
                        WriteStringToken(ref output, reader.ValueSpan, reader.ValueIsEscaped);
                    output.ValueEnd();
                    break;

                case JsonTokenType.Number:
                    // OTLP/JSON lets a 64-bit integer arrive as a number or as a string. Protojson reads a
                    // number through a double, which holds 53 bits: 9007199254740993 would arrive as ...992.
                    // Its string form is read exactly, so an integer too long for a double is passed on as one.
                    output.Separate();
                    if (IsIntegerBeyondDoublePrecision(reader.ValueSpan))
                    {
                        output.Write((byte)'"');
                        output.Write(reader.ValueSpan);
                        output.Write((byte)'"');
                    }
                    else
                    {
                        output.Write(reader.ValueSpan);
                    }

                    output.ValueEnd();
                    break;

                case JsonTokenType.True:
                    output.Separate();
                    output.Write("true"u8);
                    output.ValueEnd();
                    break;

                case JsonTokenType.False:
                    output.Separate();
                    output.Write("false"u8);
                    output.ValueEnd();
                    break;

                case JsonTokenType.Null:
                    output.Separate();
                    output.Write("null"u8);
                    output.ValueEnd();
                    break;

                case JsonTokenType.StartObject:
                    output.Separate();
                    output.Write((byte)'{');
                    PushContainerPath(path, containerSegments, ref pendingProperty);
                    break;

                case JsonTokenType.EndObject:
                    output.Write((byte)'}');
                    output.ValueEnd();
                    PopContainerPath(path, containerSegments);
                    break;

                case JsonTokenType.StartArray:
                    output.Separate();
                    output.Write((byte)'[');
                    PushContainerPath(path, containerSegments, ref pendingProperty);
                    break;

                case JsonTokenType.EndArray:
                    output.Write((byte)']');
                    output.ValueEnd();
                    PopContainerPath(path, containerSegments);
                    break;
                case JsonTokenType.None:
                case JsonTokenType.Comment:
                default:
                    break;
            }

            pendingProperty = null;
        }

        return output.Written;
    }

    /// <summary>
    ///     The normalized document. It writes the commas and colons itself: a comma before a name or a value that
    ///     follows another one in the same container, a colon after a name.
    /// </summary>
    private ref struct Output(int capacity)
    {
        private byte[] _bytes = GC.AllocateUninitializedArray<byte>(Math.Max(capacity, 16));
        private int _written;
        private bool _separate;

        public readonly ArraySegment<byte> Written => new(_bytes, 0, _written);

        /// <summary>Before a name or a value: the comma, when another member or element went before.</summary>
        public void Separate()
        {
            if (_separate) Write((byte)',');
            _separate = false;
        }

        public void NameEnd() => Write((byte)':');

        public void ValueEnd() => _separate = true;

        public void Write(byte value) => Reserve(1)[0] = value;

        public void Write(ReadOnlySpan<byte> bytes) => bytes.CopyTo(Reserve(bytes.Length));

        /// <summary>The next <paramref name="count" /> bytes of the document, for the caller to fill.</summary>
        public Span<byte> Reserve(int count)
        {
            if (count > _bytes.Length - _written)
            {
                // Rare: a document that is UTF-8 and longer once normalized, which only integers written as strings
                // (two bytes each) make it; they are bounded by the request's element bound.
                var grown = GC.AllocateUninitializedArray<byte>(
                    checked((int)Math.Max((long)_written + count, _bytes.Length + (_bytes.Length >> 2) + 4096L)));
                _bytes.AsSpan(0, _written).CopyTo(grown);
                _bytes = grown;
            }

            var reserved = _bytes.AsSpan(_written, count);
            _written += count;
            return reserved;
        }
    }

    // No known member name is longer than "exponential_histogram", and an escape spells one character in six bytes.
    // A longer name is no id and no part of a path to one, so no string is built for it.
    private const int MaxKnownNameBytes = 6 * 21;
    private const string UnknownName = "";

    private static string ReadName(ref Utf8JsonReader reader) =>
        reader.ValueSpan.Length > MaxKnownNameBytes ? UnknownName : ReadString(ref reader);

    /// <summary>
    ///     A string or a member name as the token it was sent as, quotes included, with the two things replaced that
    ///     Google.Protobuf would refuse the whole payload for: a sequence that is not UTF-8 and the escape of an
    ///     unpaired surrogate. Each becomes U+FFFD (OTLP 1.11.1). Everything else, every other escape included,
    ///     stays byte for byte, so nothing is escaped again and no string is built.
    /// </summary>
    private static void WriteStringToken(ref Output output, ReadOnlySpan<byte> token, bool escaped)
    {
        output.Write((byte)'"');
        if (escaped)
            WriteEscaped(ref output, token);
        else
            WriteUtf8(ref output, token);
        output.Write((byte)'"');
    }

    private static void WriteUtf8(ref Output output, ReadOnlySpan<byte> utf8)
    {
        if (System.Text.Unicode.Utf8.IsValid(utf8))
        {
            output.Write(utf8);
            return;
        }

        // Nothing in the replaced text needs an escape: the reader refused quotes, backslashes and control
        // characters outside an escape. It is written where it belongs, with no token built first.
        OtlpUtf8Replacement.ReplaceInvalidUtf8(utf8, output.Reserve(OtlpUtf8Replacement.ReplacedLength(utf8)));
    }

    // The reader validated every escape before handing the token over. A backslash is one byte in UTF-8 and part of
    // no longer sequence, so the text between two escapes is replaced on its own.
    private static void WriteEscaped(ref Output output, ReadOnlySpan<byte> escaped)
    {
        const int EscapeBytes = 6;
        while (!escaped.IsEmpty)
        {
            var backslash = escaped.IndexOf((byte)'\\');
            if (backslash < 0)
            {
                WriteUtf8(ref output, escaped);
                return;
            }

            WriteUtf8(ref output, escaped[..backslash]);
            escaped = escaped[backslash..];
            if (escaped[1] is not (byte)'u')
            {
                // \" \\ \/ \b \f \n \r \t
                output.Write(escaped[..2]);
                escaped = escaped[2..];
                continue;
            }

            var unit = (char)ReadHex(escaped.Slice(2, 4));
            if (char.IsHighSurrogate(unit) &&
                escaped.Length >= 2 * EscapeBytes &&
                escaped[EscapeBytes] is (byte)'\\' &&
                escaped[EscapeBytes + 1] is (byte)'u' &&
                char.IsLowSurrogate((char)ReadHex(escaped.Slice(EscapeBytes + 2, 4))))
            {
                output.Write(escaped[..(2 * EscapeBytes)]);
                escaped = escaped[(2 * EscapeBytes)..];
                continue;
            }

            if (char.IsSurrogate(unit))
                output.Write("\\uFFFD"u8);
            else
                output.Write(escaped[..EscapeBytes]);
            escaped = escaped[EscapeBytes..];
        }
    }

    // A document that is no JSON ends the count where the pass itself will refuse it.
    private static int CountIntegersWrittenAsStrings(ReadOnlySpan<byte> utf8Json, JsonReaderOptions options)
    {
        var reader = new Utf8JsonReader(utf8Json, options);
        var count = 0;
        try
        {
            while (reader.Read())
                if (reader.TokenType is JsonTokenType.Number && IsIntegerBeyondDoublePrecision(reader.ValueSpan)) count++;
        }
        catch (JsonException)
        {
            // Counted as far as it reads.
        }

        return count;
    }

    // An integer literal (digits, with a sign at most: no fraction, no exponent) of sixteen digits or more. Every
    // integer of fewer digits is below 2^53 and exact as a double. In a field that is no 64-bit integer the string
    // form reads as the number did: a double field parses it to the same double, and no enum or 32-bit field holds
    // a value this long in either form.
    private static bool IsIntegerBeyondDoublePrecision(ReadOnlySpan<byte> number)
    {
        const int DigitsOfTwoToThe53 = 16;
        var digits = number.Length > 0 && number[0] is (byte)'-' ? number[1..] : number;
        return digits.Length >= DigitsOfTwoToThe53 && digits.IndexOfAnyExceptInRange((byte)'0', (byte)'9') < 0;
    }

    private static string DecodeUtf8(ReadOnlySpan<byte> utf8)
    {
        if (System.Text.Unicode.Utf8.IsValid(utf8)) return Encoding.UTF8.GetString(utf8);
        var replaced = new byte[OtlpUtf8Replacement.ReplacedLength(utf8)];
        OtlpUtf8Replacement.ReplaceInvalidUtf8(utf8, replaced);
        return Encoding.UTF8.GetString(replaced);
    }

    // The reader runs over one span, so the token's bytes are always in ValueSpan. Encoding.UTF8
    // decodes each maximal invalid subsequence as one U+FFFD.
    private static string ReadString(ref Utf8JsonReader reader) =>
        reader.ValueIsEscaped
            ? Unescape(reader.ValueSpan)
            : DecodeUtf8(reader.ValueSpan);

    private static string Unescape(ReadOnlySpan<byte> escaped)
    {
        // Unescaping never grows the text: \uXXXX (6 bytes) encodes to at most 3, a pair (12) to 4.
        var unescaped = escaped.Length <= 512 ? stackalloc byte[512] : new byte[escaped.Length];
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

        return DecodeUtf8(unescaped[..written]);
    }

    private static int ReadHex(ReadOnlySpan<byte> utf8Hex) =>
        int.TryParse(utf8Hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException("Expected four hexadecimal digits in a JSON Unicode escape.");

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

        if (!(IsTraceLinkPath(path) || IsLogRecordPath(path) || IsMetricExemplarPath(path)))
            return 0;
        if (IsName(propertyName, "traceId", "trace_id")) return TraceIdBytes;
        return IsName(propertyName, "spanId", "span_id") ? SpanIdBytes : 0;
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

    private static void WriteIdAsBase64(ref Output output, string hex, int expectedBytes, string field)
    {
        // Empty means absent (e.g. a root span's parentSpanId); protojson maps it to empty bytes.
        if (hex.Length is 0)
        {
            output.Write("\"\""u8);
            return;
        }

        if (hex.Length != expectedBytes * 2)
        {
            throw new InvalidDataException(
                $"OTLP/JSON field '{field}' must be {expectedBytes * 2} hex characters ({expectedBytes} bytes); got {hex.Length} characters.");
        }

        Span<byte> bytes = stackalloc byte[TraceIdBytes];
        bytes = bytes[..expectedBytes];
        if (Convert.FromHexString(hex, bytes, out _, out _) is not System.Buffers.OperationStatus.Done)
        {
            throw new InvalidDataException(
                $"OTLP/JSON field '{field}' must be hex-encoded per the OTLP spec; got a non-hex value.");
        }

        output.Write((byte)'"');
        var encoded = output.Reserve(System.Buffers.Text.Base64.GetMaxEncodedToUtf8Length(expectedBytes));
        System.Buffers.Text.Base64.EncodeToUtf8(bytes, encoded, out _, out _);
        output.Write((byte)'"');
    }
}
