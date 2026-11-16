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

    /// <summary>
    ///     The payload as protojson, normalized while it is read: the parser takes its characters from the request's
    ///     bytes, and no normalized document stands between the two.
    /// </summary>
    public static Reader Read(ReadOnlyMemory<byte> utf8Json)
    {
        // Accept an optional UTF-8 BOM before protojson parsing.
        ReadOnlySpan<byte> utf8Bom = [0xEF, 0xBB, 0xBF];
        if (utf8Json.Span.StartsWith(utf8Bom))
            utf8Json = utf8Json[utf8Bom.Length..];

        if (utf8Json.IsEmpty)
            throw new InvalidDataException("OTLP/JSON payload is empty.");

        return new Reader(utf8Json);
    }

    /// <summary>
    ///     The normalized document as characters, a buffer of them at a time. It writes the commas and colons itself:
    ///     a comma before a name or a value that follows another one in the same container, a colon after a name.
    /// </summary>
    /// <remarks>
    ///     Only Google.Protobuf's JSON parser reads it, and every string and member name it gets is the token as it
    ///     was sent: no encoder stands in between. System.Text.Json's writer would escape a character outside the
    ///     Basic Multilingual Plane (four bytes) as twelve bytes under every encoder it has, and &lt;, &gt;, &amp;,
    ///     ', + and everything outside ASCII as six bytes each under its default one.
    ///     <para>
    ///         A token of any length is written in pieces, so the text that is normalized and not yet parsed is
    ///         never more than the buffer. A normalized document held whole was up to three times the request,
    ///         three bytes for each one replaced, beside the parser's own copies (evaluator 17, X10 and X13).
    ///     </para>
    /// </remarks>
    internal sealed class Reader : TextReader
    {
        private const int BufferChars = 16 * 1024;

        // Room a token needs before its text: a comma, an id as base64 in quotes, or a quote.
        private const int TokenRoom = 32;

        // Room a piece of text needs: a pair of surrogate escapes, the longest thing written as one.
        private const int TextRoom = 12;

        private readonly ReadOnlyMemory<byte> _utf8;
        private char[] _chars = ArrayPool<char>.Shared.Rent(BufferChars);
        private int _position;
        private int _length;

        // Where the JSON reader stands in the request, to go on from when the buffer has been read.
        private JsonReaderState _state = new(new JsonReaderOptions { MaxDepth = 128 });
        private int _consumed;
        private bool _ended;

        // The text of a token that is still to be written, as offsets into the request: up to _plainEnd it holds
        // no escape. Then what closes the token.
        private int _textStart;
        private int _plainEnd;
        private int _textEnd;
        private bool _textEscaped;
        private TextEnd _textCloses;

        // Protojson must ignore unknown fields, including future objects that happen to contain
        // properties named traceId or spanId. Track the containing OTLP message path so
        // only ids owned by known signal shapes are validated and rewritten.
        private readonly List<string> _path = new(8);
        private readonly Stack<bool> _containerSegments = new();
        private string? _pendingProperty;
        private bool _separate;

        internal Reader(ReadOnlyMemory<byte> utf8Json) => _utf8 = utf8Json;

        private enum TextEnd
        {
            None,
            Quote,
            QuoteAndColon
        }

        /// <summary>
        ///     True once the document itself was refused: it is no JSON, or an id in it is no hex of the length OTLP
        ///     asks for. The exception left through the read that met it.
        /// </summary>
        public bool Refused { get; private set; }

        public override int Peek() => _position < _length || Fill() ? _chars[_position] : -1;

        public override int Read() => _position < _length || Fill() ? _chars[_position++] : -1;

        public override int Read(char[] buffer, int index, int count) => Read(buffer.AsSpan(index, count));

        public override int Read(Span<char> buffer)
        {
            if (buffer.IsEmpty || (_position == _length && !Fill())) return 0;
            var count = Math.Min(buffer.Length, _length - _position);
            _chars.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        /// <summary>
        ///     Normalizes what the parser has not read, and keeps none of it. A document the normalizer refuses is
        ///     refused for that wherever in it the parser stopped, as when the whole document was normalized first.
        /// </summary>
        public void Skip()
        {
            while (Fill())
            {
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _chars.Length > 0)
            {
                ArrayPool<char>.Shared.Return(_chars);
                _chars = [];
                _length = _position = 0;
                _ended = true;
            }

            base.Dispose(disposing);
        }

        // The next characters of the document; false at its end.
        private bool Fill()
        {
            _position = 0;
            _length = 0;
            if (_ended) return false;
            try
            {
                var utf8 = _utf8.Span;
                if (!WriteText(utf8)) return true;

                var reader = new Utf8JsonReader(utf8[_consumed..], true, _state);
                while (_chars.Length - _length >= TokenRoom)
                {
                    if (!reader.Read())
                    {
                        _ended = true;
                        break;
                    }

                    if (!WriteToken(ref reader, utf8)) break;
                }

                _consumed += (int)reader.BytesConsumed;
                _state = reader.CurrentState;
                return _length > 0;
            }
            catch
            {
                // What was written before the refusal is not handed over: a reader after it gives nothing.
                _position = 0;
                _length = 0;
                Refused = true;
                _ended = true;
                throw;
            }
        }

        // False when the token's text did not fit: the rest is written into the next buffer.
        private bool WriteToken(ref Utf8JsonReader reader, ReadOnlySpan<byte> utf8)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    Separate();
                    _pendingProperty = ReadName(ref reader);
                    // The token as it was sent, not the name that was read from it.
                    return WriteStringToken(utf8, reader.ValueSpan, reader.ValueIsEscaped, TextEnd.QuoteAndColon);

                case JsonTokenType.String:
                    var expectedBytes = _pendingProperty is not null
                        ? ExpectedIdByteLength(_path, _pendingProperty)
                        : 0;
                    Separate();
                    _separate = true;
                    if (expectedBytes > 0)
                    {
                        WriteIdAsBase64(ref reader, expectedBytes, _pendingProperty!);
                        _pendingProperty = null;
                        return true;
                    }

                    _pendingProperty = null;
                    return WriteStringToken(utf8, reader.ValueSpan, reader.ValueIsEscaped, TextEnd.Quote);

                case JsonTokenType.Number:
                    // OTLP/JSON lets a 64-bit integer arrive as a number or as a string. Protojson reads a
                    // number through a double, which holds 53 bits: 9007199254740993 would arrive as ...992.
                    // Its string form is read exactly, so an integer too long for a double is passed on as one.
                    Separate();
                    _separate = true;
                    _pendingProperty = null;
                    return IsIntegerBeyondDoublePrecision(reader.ValueSpan)
                        ? WriteStringToken(utf8, reader.ValueSpan, false, TextEnd.Quote)
                        : WriteText(utf8, reader.ValueSpan, false, TextEnd.None);

                case JsonTokenType.True:
                    Separate();
                    Write("true");
                    _separate = true;
                    break;

                case JsonTokenType.False:
                    Separate();
                    Write("false");
                    _separate = true;
                    break;

                case JsonTokenType.Null:
                    Separate();
                    Write("null");
                    _separate = true;
                    break;

                case JsonTokenType.StartObject:
                    Separate();
                    Write('{');
                    PushContainerPath(_path, _containerSegments, ref _pendingProperty);
                    break;

                case JsonTokenType.EndObject:
                    Write('}');
                    _separate = true;
                    PopContainerPath(_path, _containerSegments);
                    break;

                case JsonTokenType.StartArray:
                    Separate();
                    Write('[');
                    PushContainerPath(_path, _containerSegments, ref _pendingProperty);
                    break;

                case JsonTokenType.EndArray:
                    Write(']');
                    _separate = true;
                    PopContainerPath(_path, _containerSegments);
                    break;
                case JsonTokenType.None:
                case JsonTokenType.Comment:
                default:
                    break;
            }

            _pendingProperty = null;
            return true;
        }

        // Before a name or a value: the comma, when another member or element went before.
        private void Separate()
        {
            if (_separate) Write(',');
            _separate = false;
        }

        private void Write(char value) => _chars[_length++] = value;

        private void Write(ReadOnlySpan<char> text)
        {
            text.CopyTo(_chars.AsSpan(_length));
            _length += text.Length;
        }

        /// <summary>
        ///     A string or a member name as the token it was sent as, quotes included, with the two things replaced
        ///     that Google.Protobuf would refuse the whole payload for: a sequence that is not UTF-8 and the escape
        ///     of an unpaired surrogate. Each becomes U+FFFD (OTLP 1.11.1). Everything else, every other escape
        ///     included, stays as it was sent, so nothing is escaped again and no string is built.
        /// </summary>
        private bool WriteStringToken(ReadOnlySpan<byte> utf8, ReadOnlySpan<byte> token, bool escaped, TextEnd closes)
        {
            Write('"');
            return WriteText(utf8, token, escaped, closes);
        }

        private bool WriteText(ReadOnlySpan<byte> utf8, ReadOnlySpan<byte> token, bool escaped, TextEnd closes)
        {
            // The reader runs over the request itself, so a token is a part of it.
            _textStart = token.IsEmpty || !utf8.Overlaps(token, out var offset) ? 0 : offset;
            _textEnd = _textStart + token.Length;
            _textEscaped = escaped;
            _plainEnd = PlainEnd(utf8);
            _textCloses = closes;
            return WriteText(utf8);
        }

        private int PlainEnd(ReadOnlySpan<byte> utf8)
        {
            if (!_textEscaped) return _textEnd;
            var backslash = utf8[_textStart.._textEnd].IndexOf((byte)'\\');
            return backslash < 0 ? _textEnd : _textStart + backslash;
        }

        // The reader validated every escape before handing the token over. A backslash is one byte in UTF-8 and
        // part of no longer sequence, so the text between two escapes is replaced on its own. Nothing in the
        // replaced text needs an escape: the reader refused quotes, backslashes and control characters outside one.
        private bool WriteText(ReadOnlySpan<byte> utf8)
        {
            while (_textStart < _textEnd)
            {
                var room = _chars.AsSpan(_length);
                if (room.Length < TextRoom) return false;

                if (_textStart < _plainEnd)
                {
                    // Each maximal invalid subsequence becomes one U+FFFD, the sequences Encoding.UTF8 replaces.
                    System.Text.Unicode.Utf8.ToUtf16(utf8[_textStart.._plainEnd], room, out var read, out var written);
                    _textStart += read;
                    _length += written;
                    continue;
                }

                _textStart += WriteEscape(utf8[_textStart.._textEnd], room);
                _plainEnd = PlainEnd(utf8);
            }

            if (_textCloses is TextEnd.None) return true;
            if (_chars.Length - _length < 2) return false;
            Write('"');
            if (_textCloses is TextEnd.QuoteAndColon) Write(':');
            _textCloses = TextEnd.None;
            return true;
        }

        // One escape, or the two of a surrogate pair; the bytes it took.
        private int WriteEscape(ReadOnlySpan<byte> escaped, Span<char> room)
        {
            const int EscapeBytes = 6;
            // \" \ \/ \b \f \n \r \t
            var taken = 2;
            if (escaped[1] is (byte)'u')
            {
                var unit = (char)ReadHex(escaped.Slice(2, 4));
                if (char.IsHighSurrogate(unit) &&
                    escaped.Length >= 2 * EscapeBytes &&
                    escaped[EscapeBytes] is (byte)'\\' &&
                    escaped[EscapeBytes + 1] is (byte)'u' &&
                    char.IsLowSurrogate((char)ReadHex(escaped.Slice(EscapeBytes + 2, 4))))
                {
                    taken = 2 * EscapeBytes;
                }
                else if (char.IsSurrogate(unit))
                {
                    Write("\\uFFFD");
                    return EscapeBytes;
                }
                else
                {
                    taken = EscapeBytes;
                }
            }

            // An escape is ASCII.
            for (var i = 0; i < taken; i++) room[i] = (char)escaped[i];
            _length += taken;
            return taken;
        }

        private void WriteIdAsBase64(ref Utf8JsonReader reader, int expectedBytes, string field)
        {
            // A token longer than any id is refused on its length, counted on its bytes: no string of it is built.
            if (reader.ValueSpan.Length > MaxIdTokenBytes)
                throw WrongLength(field, expectedBytes, CountCharacters(reader.ValueSpan, reader.ValueIsEscaped));

            var hex = ReadString(ref reader);
            // Empty means absent (e.g. a root span's parentSpanId); protojson maps it to empty bytes.
            if (hex.Length is 0)
            {
                Write("\"\"");
                return;
            }

            if (hex.Length != expectedBytes * 2)
                throw WrongLength(field, expectedBytes, hex.Length);

            Span<byte> bytes = stackalloc byte[TraceIdBytes];
            bytes = bytes[..expectedBytes];
            if (Convert.FromHexString(hex, bytes, out _, out _) is not OperationStatus.Done)
            {
                throw new InvalidDataException(
                    $"OTLP/JSON field '{field}' must be hex-encoded per the OTLP spec; got a non-hex value.");
            }

            Write('"');
            Convert.TryToBase64Chars(bytes, _chars.AsSpan(_length), out var written);
            _length += written;
            Write('"');
        }
    }

    // An id is thirty-two hex characters at most, and an escape spells one character in six bytes.
    private const int MaxIdTokenBytes = 6 * 2 * TraceIdBytes;

    private static InvalidDataException WrongLength(string field, int expectedBytes, int characters) => new(
        $"OTLP/JSON field '{field}' must be {expectedBytes * 2} hex characters ({expectedBytes} bytes); got {characters} characters.");

    /// <summary>
    ///     The characters <see cref="ReadString" /> would give for the token, counted without building them: UTF-16
    ///     units, one for each maximal invalid subsequence and for the escape of an unpaired surrogate (both read
    ///     as U+FFFD), two for a pair of surrogate escapes. An escape stands for a whole character, so the text
    ///     between two escapes is counted on its own.
    /// </summary>
    private static int CountCharacters(ReadOnlySpan<byte> token, bool escaped)
    {
        if (!escaped) return CountUtf16(token);

        const int EscapeBytes = 6;
        var count = 0;
        while (!token.IsEmpty)
        {
            var backslash = token.IndexOf((byte)'\\');
            if (backslash < 0) return count + CountUtf16(token);

            count += CountUtf16(token[..backslash]);
            token = token[backslash..];
            // The reader validated every escape before handing the token over.
            var taken = token[1] is (byte)'u' ? EscapeBytes : 2;
            count++;
            if (taken is EscapeBytes &&
                char.IsHighSurrogate((char)ReadHex(token.Slice(2, 4))) &&
                token.Length >= 2 * EscapeBytes &&
                token[EscapeBytes] is (byte)'\\' &&
                token[EscapeBytes + 1] is (byte)'u' &&
                char.IsLowSurrogate((char)ReadHex(token.Slice(EscapeBytes + 2, 4))))
            {
                taken = 2 * EscapeBytes;
                count++;
            }

            token = token[taken..];
        }

        return count;
    }

    // The UTF-16 units of the text once each maximal invalid subsequence is one U+FFFD, the sequences
    // OtlpUtf8Replacement replaces. Encoding.UTF8 counts the same and allocates for each sequence it replaces.
    private static int CountUtf16(ReadOnlySpan<byte> utf8)
    {
        var count = 0;
        while (!utf8.IsEmpty)
        {
            var beyondAscii = utf8.IndexOfAnyInRange((byte)0x80, (byte)0xFF);
            if (beyondAscii < 0) return count + utf8.Length;

            utf8 = utf8[beyondAscii..];
            var valid = Rune.DecodeFromUtf8(utf8, out var rune, out var consumed) is OperationStatus.Done;
            count += beyondAscii + (valid ? rune.Utf16SequenceLength : 1);
            utf8 = utf8[consumed..];
        }

        return count;
    }

    // No known member name is longer than "exponential_histogram", and an escape spells one character in six bytes.
    // A longer name is no id and no part of a path to one, so no string is built for it.
    private const int MaxKnownNameBytes = 6 * 21;
    private const string UnknownName = "";

    private static string ReadName(ref Utf8JsonReader reader) =>
        reader.ValueSpan.Length > MaxKnownNameBytes ? UnknownName : ReadString(ref reader);

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
}
