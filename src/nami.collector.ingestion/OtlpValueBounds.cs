using System.Buffers.Text;
using Google.Protobuf.Reflection;
using ProtoAnyValue = OpenTelemetry.Proto.Common.V1.AnyValue;
using ProtoLogs = OpenTelemetry.Proto.Logs.V1;
using ProtoMetrics = OpenTelemetry.Proto.Metrics.V1;
using ProtoTrace = OpenTelemetry.Proto.Trace.V1;

namespace Nami.Collector.Ingestion;

/// <summary>What <see cref="OtlpValueBounds" /> changed in one payload before it was parsed.</summary>
/// <param name="Oversized">Structures over the value cap, replaced by <see cref="OtlpValueBounds.OversizedValueSentinel" />.</param>
/// <param name="TooDeep">Values nested deeper than the bound, replaced by an empty value.</param>
internal readonly record struct OtlpValueBoundsResult(int Oversized, int TooDeep);

/// <summary>
///     What the parser will build from one request, counted by <see cref="OtlpValueBounds" /> before it does. Each
///     count stops one past its bound.
/// </summary>
/// <param name="Messages">Every message: the request itself and each message in it, of whatever type.</param>
/// <param name="Records">The messages stored as rows of their own: spans, log records, metric data points, exemplars.</param>
internal readonly record struct OtlpRequestCounts(int Messages, int Records);

/// <summary>
///     The value bounds of <see cref="IngestLimits" />, applied to the bytes of an export before its objects exist.
///     <para>
///         Why before: an empty AnyValue is two bytes of protobuf and three of JSON, and some fifty bytes once
///         it is an object. One 7.6 MiB export carried four million of them in one attribute and took the engine
///         from 138 MiB to 872 MiB while it was ingested (engine audit A3, 15 Nov 2026). The converter drops such
///         a value, but only after the parser has built it.
///     </para>
///     <para>
///         nami receiver policy (compare the SDK attribute limits in the OpenTelemetry Common Specification,
///         https://opentelemetry.io/docs/specs/otel/common/#attribute-limits; the data model, what an AnyValue is
///         and how it nests, is the specification's): an array or a key-value list that is an attribute value or
///         a log body is over the value cap when
///         <list type="bullet">
///             <item>its OTLP protobuf encoding is longer than <see cref="IngestLimits.MaxValueUtf8Bytes" />, or</item>
///             <item>
///                 it holds, at all levels together, more values than
///                 <see cref="MaxNestedValues" />: each costs at least five bytes of the cap once stored.
///             </item>
///         </list>
///         Such a structure is dropped whole: its bytes are overwritten, in place and in the same length, by the
///         string <see cref="OversizedValueSentinel" />. The converter drops the attribute that carries the sentinel
///         and counts it in the record's <c>dropped_attributes_count</c>; a log body that carries it is emptied.
///         A string or bytes value over the cap is left alone: it is one object however long it is, and the
///         converter cuts it. An array or key-value list nested deeper than
///         <see cref="IngestLimits.MaxValueNesting" /> is replaced by an empty value, in place as well.
///     </para>
///     <para>
///         The same scan counts the messages of the request for <see cref="IngestLimits.MaxRequestMessages" />: every
///         message the parser will build an object for, of whatever type; and among them the records for
///         <see cref="IngestLimits.MaxRequestRecords" />: the messages stored as rows of their own. What a dropped
///         structure or an emptied value held is not counted, because it is never built. Over JSON a message is a
///         JSON object, and a record is an object in an array named spans, logRecords, dataPoints or exemplars.
///     </para>
///     <para>
///         Everything else stays byte for byte as it was sent, so an export within the bounds parses exactly as it
///         did without this step. What is replaced is not validated: a structure over the cap that is also
///         malformed inside is dropped, not refused. Protobuf bytes the scan cannot read exactly as it counts them
///         are refused with an <see cref="OtlpMalformedPayloadException" />: a field that does not end inside its
///         message, a group, a tag or a length longer than five bytes. Google.Protobuf accepts some of those and
///         would build what the scan did not count. JSON that is no well-formed document is left to the parser. The size counts the value as it was sent: over protobuf its length on the
///         wire, over JSON the length its protobuf encoding has. <see cref="IsOversizedStructure" /> is the same
///         rule on a parsed value, for the converter and for the tests that hold the three forms together.
///     </para>
/// </summary>
internal static class OtlpValueBounds
{
    /// <summary>
    ///     What a structure over the value cap is replaced by. The NUL makes it a string no instrumentation sends
    ///     by accident; a sender that sends it on purpose loses that one value, as if it had been over the cap.
    /// </summary>
    internal const string OversizedValueSentinel = "\0nami:value-over-the-cap";

    // What one nested value costs of the cap at least, as nami stores it.
    private const int StoredBytesPerNestedValue = 5;

    // Below this an AnyValue is not replaced: it must be able to carry the sentinel, and nothing so short is over a
    // bound anyone would configure. The converter applies the same bounds to what passes.
    private const int MinScannedValueBytes = 32;

    // Google.Protobuf's recursion limit; a message nested deeper fails the parse anyway.
    private const int MaxMessageDepth = 100;

    private const int WireTypeVarint = 0;
    private const int WireTypeFixed64 = 1;
    private const int WireTypeLengthDelimited = 2;
    private const int WireTypeFixed32 = 5;

    // AnyValue (opentelemetry/proto/common/v1/common.proto).
    private const int StringValueField = 1;
    private const int BoolValueField = 2;
    private const int ArrayValueField = 5;
    private const int KvlistValueField = 6;
    private const int BytesValueField = 7;
    private const int LastOneofField = 8;

    // A field number AnyValue does not define: the parser keeps it as an unknown field and reads no value from it.
    private const int PaddingField = 15;

    // ArrayValue.values, KeyValueList.values, KeyValue.value.
    private const int ValuesField = 1;
    private const int KeyValueValueField = 2;

    private static readonly byte[] s_sentinelUtf8 = Encoding.UTF8.GetBytes(OversizedValueSentinel);

    /// <summary>The most values an array or key-value list may hold at all levels together.</summary>
    internal static int MaxNestedValues(IngestLimits limits) => limits.MaxValueUtf8Bytes / StoredBytesPerNestedValue;

    /// <summary>
    ///     The rule on a parsed value: whether <paramref name="value" /> is an array or a key-value list over the
    ///     value cap. What the two scans below replace is, for a payload as an OTLP exporter encodes it, exactly
    ///     what this answers true for.
    /// </summary>
    internal static bool IsOversizedStructure(ProtoAnyValue? value, IngestLimits limits)
    {
        if (value?.ValueCase is not (ProtoAnyValue.ValueOneofCase.ArrayValue or ProtoAnyValue.ValueOneofCase.KvlistValue))
            return false;
        var left = MaxNestedValues(limits);
        return !NestedValuesFit(value, ref left) || value.CalculateSize() > limits.MaxValueUtf8Bytes;
    }

    private static bool NestedValuesFit(ProtoAnyValue? value, ref int left)
    {
        switch (value?.ValueCase)
        {
            case ProtoAnyValue.ValueOneofCase.ArrayValue:
                foreach (var item in value.ArrayValue.Values)
                    if (--left < 0 || !NestedValuesFit(item, ref left)) return false;
                break;
            case ProtoAnyValue.ValueOneofCase.KvlistValue:
                foreach (var member in value.KvlistValue.Values)
                    if (--left < 0 || !NestedValuesFit(member.Value, ref left)) return false;
                break;
        }

        return true;
    }

    // ---------------------------------------------------------------------------------------------- protobuf

    /// <summary>
    ///     Applies the value bounds to the protobuf encoding of a message of type <paramref name="descriptor" />, in
    ///     place. The length of <paramref name="message" /> and of everything in it stays as it was.
    /// </summary>
    internal static OtlpValueBoundsResult BoundProtobuf(Span<byte> message, MessageDescriptor descriptor,
        IngestLimits limits) =>
        BoundProtobuf(message, descriptor, limits, out _);

    /// <summary>
    ///     The same, and <paramref name="counted" /> is what the parser will build from what is left. The scan ends
    ///     one past <see cref="IngestLimits.MaxRequestMessages" /> or <see cref="IngestLimits.MaxRequestRecords" />;
    ///     the caller refuses such a request.
    /// </summary>
    internal static OtlpValueBoundsResult BoundProtobuf(Span<byte> message, MessageDescriptor descriptor,
        IngestLimits limits, out OtlpRequestCounts counted)
    {
        var scan = new Scan(limits) { Messages = 1 };
        ScanMessage(message, descriptor, 0, ref scan);
        counted = new OtlpRequestCounts(scan.Messages, scan.Records);
        return new OtlpValueBoundsResult(scan.Oversized, scan.TooDeep);
    }

    // The messages nami stores as rows of their own. Span events and links are stored inside their span's row.
    private static readonly FrozenSet<MessageDescriptor> s_recordTypes = FrozenSet.Create(
        ProtoTrace.Span.Descriptor, ProtoLogs.LogRecord.Descriptor, ProtoMetrics.NumberDataPoint.Descriptor,
        ProtoMetrics.HistogramDataPoint.Descriptor, ProtoMetrics.ExponentialHistogramDataPoint.Descriptor,
        ProtoMetrics.SummaryDataPoint.Descriptor, ProtoMetrics.Exemplar.Descriptor);

    private struct Scan(IngestLimits limits)
    {
        internal readonly int MaxValueBytes = limits.MaxValueUtf8Bytes;
        internal readonly int MaxNested = MaxNestedValues(limits);
        internal readonly int MaxNesting = limits.MaxValueNesting;
        internal readonly int MaxMessages = limits.MaxRequestMessages;
        internal readonly int MaxRecords = limits.MaxRequestRecords;
        internal readonly int MaxElements = limits.MaxRequestElements;
        internal int Oversized;
        internal int TooDeep;
        internal int Messages;
        internal int Records;
        internal long Elements;

        internal readonly bool TooMany => Messages > MaxMessages || Records > MaxRecords;
    }

    private enum Walk
    {
        Fits,
        Over
    }

    // Every message of the request: each is counted, an attribute value or a log body is bounded, and every other
    // message is read through to the messages it holds. The elements of repeated scalar fields are counted on the
    // way. The scan ends when the request holds too many messages or too many records.
    private static void ScanMessage(Span<byte> message, MessageDescriptor descriptor, int depth, ref Scan scan)
    {
        if (depth > MaxMessageDepth) throw Malformed("messages nested deeper than a parser reads");
        var position = 0;
        while (position < message.Length && !scan.TooMany)
        {
            ReadField(message, ref position, out var number, out var wireType, out var start, out var length);
            var field = descriptor.FindFieldByNumber(number);
            if (field is null) continue;
            if (field.FieldType is not FieldType.Message)
            {
                if (field.IsRepeated) CountElements(field, wireType, message.Slice(start, length), ref scan);
                continue;
            }

            if (wireType != WireTypeLengthDelimited) continue;
            scan.Messages++;
            if (s_recordTypes.Contains(field.MessageType)) scan.Records++;
            if (length == 0) continue;
            var body = message.Slice(start, length);
            if (ReferenceEquals(field.MessageType, ProtoAnyValue.Descriptor))
                BoundValue(body, ref scan);
            else
                ScanMessage(body, field.MessageType, depth + 1, ref scan);
        }
    }

    // The elements one occurrence of a repeated scalar field adds: one when it is not packed, and for a packed
    // field as many as its bytes hold, by the width of the type or by the varints that end in them.
    private static void CountElements(FieldDescriptor field, int wireType, ReadOnlySpan<byte> packed, ref Scan scan)
    {
        if (wireType != WireTypeLengthDelimited || field.FieldType is FieldType.String or FieldType.Bytes)
        {
            scan.Elements++;
        }
        else
        {
            switch (field.FieldType)
            {
                case FieldType.Double or FieldType.Fixed64 or FieldType.SFixed64:
                    scan.Elements += packed.Length / 8;
                    break;
                case FieldType.Float or FieldType.Fixed32 or FieldType.SFixed32:
                    scan.Elements += packed.Length / 4;
                    break;
                default:
                    foreach (var value in packed)
                        if (value < 0x80) scan.Elements++;
                    break;
            }
        }

        if (scan.Elements > scan.MaxElements) throw OtlpPayloadTooLargeException.TooManyElements(scan.MaxElements);
    }

    private static OtlpMalformedPayloadException Malformed(string what) =>
        new("The OTLP request is not protobuf as an OTLP exporter writes it: " + what + ".");

    // One attribute value or log body: the body of an AnyValue that no other AnyValue holds.
    private static void BoundValue(Span<byte> value, ref Scan scan)
    {
        // Which member of the oneof the parser ends with, and whether the bytes hold a structure at all.
        var last = 0;
        var holdsStructure = false;
        var position = 0;
        while (position < value.Length)
        {
            ReadField(value, ref position, out var number, out var wireType, out _, out _);
            if (number is < StringValueField or > LastOneofField) continue;
            last = number;
            holdsStructure |= wireType == WireTypeLengthDelimited && number is ArrayValueField or KvlistValueField;
        }

        if (!holdsStructure) return;
        // A value too short to carry the sentinel is left to the converter; nothing so short is over a bound
        // anyone would configure.
        var replaceable = value.Length >= MinScannedValueBytes;
        var over = replaceable && last is ArrayValueField or KvlistValueField && value.Length > scan.MaxValueBytes;
        var nested = 0;
        if (!over)
        {
            // Every structure on the wire is counted, also one a later member of the oneof replaces: the parser
            // builds it before it discards it.
            var left = scan.MaxNested;
            // Walked also when it is too short to replace: the messages inside it count.
            over = WalkValue(value, 0, ref left, ref nested, ref scan) is Walk.Over && replaceable;
        }

        if (!over)
        {
            // Kept: the messages inside it are messages of the request.
            scan.Messages += nested;
            return;
        }

        // The sentinel last, so that it is the member the parser ends with; before it, bytes_value over the rest,
        // which the parser reads as one object and discards. Nothing the structure held is built, so nothing of it
        // is counted.
        var padding = value[..^(2 + s_sentinelUtf8.Length)];
        Pad(padding, BytesValueField, BoolValueField);
        var sentinel = value[padding.Length..];
        sentinel[0] = (StringValueField << 3) | WireTypeLengthDelimited;
        sentinel[1] = (byte)s_sentinelUtf8.Length;
        s_sentinelUtf8.CopyTo(sentinel[2..]);
        scan.Oversized++;
    }

    // Counts the values inside an AnyValue, through every level it may nest to, and empties what nests deeper. left
    // is what the value cap still allows; messages is every message found inside: each array, key-value list, item
    // and member, and each member's value.
    private static Walk WalkValue(Span<byte> value, int nesting, ref int left, ref int messages, ref Scan scan)
    {
        var position = 0;
        while (position < value.Length)
        {
            ReadField(value, ref position, out var number, out var wireType, out var start, out var length);
            if (wireType != WireTypeLengthDelimited || number is not (ArrayValueField or KvlistValueField)) continue;
            if (nesting >= scan.MaxNesting)
            {
                // An array or a key-value list below the deepest level: the value that holds it becomes empty.
                Pad(value, PaddingField, PaddingField);
                scan.TooDeep++;
                return Walk.Fits;
            }

            messages++;
            var container = value.Slice(start, length);
            var inner = 0;
            while (inner < container.Length)
            {
                ReadField(container, ref inner, out var itemNumber, out var itemType, out var itemStart,
                    out var itemLength);
                if (itemType != WireTypeLengthDelimited || itemNumber != ValuesField) continue;
                if (--left < 0) return Walk.Over;
                messages++;
                var item = container.Slice(itemStart, itemLength);
                if (number == ArrayValueField)
                {
                    var walked = WalkValue(item, nesting + 1, ref left, ref messages, ref scan);
                    if (walked is not Walk.Fits) return walked;
                    continue;
                }

                var member = 0;
                while (member < item.Length)
                {
                    ReadField(item, ref member, out var memberNumber, out var memberType, out var memberStart,
                        out var memberLength);
                    if (memberType != WireTypeLengthDelimited || memberNumber != KeyValueValueField) continue;
                    messages++;
                    var walked = WalkValue(item.Slice(memberStart, memberLength), nesting + 1, ref left, ref messages,
                        ref scan);
                    if (walked is not Walk.Fits) return walked;
                }
            }
        }

        return Walk.Fits;
    }

    // Fills region with one length-delimited field of zeros, in exactly the region's length. A length whose own
    // varint does not come out even gets a two-byte varint field (value 0) in front. The region is two bytes at least.
    private static void Pad(Span<byte> region, int lengthDelimitedField, int varintField)
    {
        region.Clear();
        if (region.IsEmpty) return;
        for (var filler = 0; filler <= 2; filler += 2)
        {
            for (var varint = 1; varint <= 5; varint++)
            {
                var zeros = region.Length - filler - 1 - varint;
                if (zeros < 0 || VarintLength((ulong)zeros) != varint) continue;
                var at = 0;
                if (filler == 2)
                {
                    region[at] = (byte)((varintField << 3) | WireTypeVarint);
                    at += 2;
                }

                region[at++] = (byte)((lengthDelimitedField << 3) | WireTypeLengthDelimited);
                for (var rest = (uint)zeros;; rest >>= 7)
                {
                    if (rest < 0x80)
                    {
                        region[at] = (byte)rest;
                        return;
                    }

                    region[at++] = (byte)(rest | 0x80);
                }
            }
        }

        throw new InvalidOperationException("A value could not be overwritten in its own length.");
    }

    private static int VarintLength(ulong value)
    {
        var length = 1;
        for (; value >= 0x80; value >>= 7) length++;
        return length;
    }

    // Reads one field, exactly or not at all. For a length-delimited field start and length are its content and
    // position is left behind it. What an OTLP exporter does not write is refused: groups, and a tag or a length
    // of more than five bytes or more than 32 bits, which Google.Protobuf cuts to 32 bits and reads on.
    private static void ReadField(ReadOnlySpan<byte> message, ref int position, out int number, out int wireType,
        out int start, out int length)
    {
        start = 0;
        length = 0;
        var tag = ReadVarint(message, ref position, 5);
        if (tag > uint.MaxValue || tag >> 3 is 0) throw Malformed("a field tag is not a field number in 29 bits");
        number = (int)(tag >> 3);
        wireType = (int)(tag & 7);
        switch (wireType)
        {
            case WireTypeVarint:
                _ = ReadVarint(message, ref position, 10);
                return;
            case WireTypeFixed64:
                position += 8;
                break;
            case WireTypeFixed32:
                position += 4;
                break;
            case WireTypeLengthDelimited:
                var size = ReadVarint(message, ref position, 5);
                if (size > (ulong)(message.Length - position)) throw Malformed("a field is longer than the message that holds it");
                start = position;
                length = (int)size;
                position += length;
                return;
            default:
                throw Malformed("it holds a group, and OTLP defines none");
        }

        if (position > message.Length) throw Malformed("a field is longer than the message that holds it");
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> message, ref int position, int maxBytes)
    {
        ulong value = 0;
        for (var read = 0; read < maxBytes && position < message.Length; read++)
        {
            var next = message[position++];
            value |= (ulong)(next & 0x7F) << (7 * read);
            if (next < 0x80) return value;
        }

        throw Malformed("a number does not end where it has to");
    }

    // -------------------------------------------------------------------------------------------------- JSON

    private static ReadOnlySpan<byte> JsonSentinel => "\"stringValue\":\"\\u0000nami:value-over-the-cap\""u8;

    private enum Structure
    {
        None,
        Array,
        KeyValueList
    }

    /// <summary>
    ///     Applies the value bounds to an OTLP/JSON payload, in place. A structure over the cap becomes
    ///     <c>"stringValue":"\u0000nami:value-over-the-cap"</c> and a value nested too deep becomes <c>{}</c>, each
    ///     followed by spaces up to the length of what it replaces. Property names are matched as the protobuf JSON
    ///     mapping reads them: in either spelling and with their escapes resolved.
    /// </summary>
    internal static OtlpValueBoundsResult BoundJson(Span<byte> utf8Json, IngestLimits limits) =>
        BoundJson(utf8Json, limits, out _);

    /// <summary>
    ///     The same, and <paramref name="counted" /> is what the parser will build from what is left: a message is a
    ///     JSON object, a record an object in an array named spans, logRecords, dataPoints or exemplars. The count
    ///     ends one past <see cref="IngestLimits.MaxRequestMessages" /> or
    ///     <see cref="IngestLimits.MaxRequestRecords" />.
    /// </summary>
    internal static OtlpValueBoundsResult BoundJson(Span<byte> utf8Json, IngestLimits limits,
        out OtlpRequestCounts counted)
    {
        ReadOnlySpan<byte> utf8Bom = [0xEF, 0xBB, 0xBF];
        var json = utf8Json.StartsWith(utf8Bom) ? utf8Json[utf8Bom.Length..] : utf8Json;
        var bounded = BoundJsonValues(json, limits);
        // Counted after the values are bounded: what a dropped structure held is spaces now.
        counted = CountJson(json, limits);
        return bounded;
    }

    // No array of records lies deeper than this in OTLP/JSON (the exemplars of a data point are at depth 11).
    private const int MaxRecordArrayDepth = 32;

    private static OtlpRequestCounts CountJson(ReadOnlySpan<byte> json, IngestLimits limits)
    {
        var messages = 0;
        var records = 0;
        var elements = 0;
        // Per depth: whether the container that starts there is an array of records.
        Span<bool> recordArray = stackalloc bool[MaxRecordArrayDepth];
        var namesRecords = false;
        try
        {
            var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = int.MaxValue });
            while (messages <= limits.MaxRequestMessages && records <= limits.MaxRequestRecords && reader.Read())
            {
                var depth = reader.CurrentDepth;
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        namesRecords = reader.ValueTextEquals("spans"u8) || reader.ValueTextEquals("logRecords"u8) ||
                                       reader.ValueTextEquals("log_records"u8) || reader.ValueTextEquals("dataPoints"u8) ||
                                       reader.ValueTextEquals("data_points"u8) || reader.ValueTextEquals("exemplars"u8);
                        continue;
                    case JsonTokenType.StartArray:
                        if (depth < MaxRecordArrayDepth) recordArray[depth] = namesRecords;
                        break;
                    case JsonTokenType.String or JsonTokenType.Number or JsonTokenType.True or JsonTokenType.False
                        or JsonTokenType.Null:
                        // The JSON reader builds a token for every scalar, in unknown members too.
                        if (++elements > limits.MaxRequestElements)
                            throw OtlpPayloadTooLargeException.TooManyElements(limits.MaxRequestElements);
                        break;
                    case JsonTokenType.StartObject:
                        messages++;
                        if (depth is > 0 and <= MaxRecordArrayDepth && recordArray[depth - 1]) records++;
                        if (depth < MaxRecordArrayDepth) recordArray[depth] = false;
                        break;
                }

                namesRecords = false;
            }
        }
        catch (JsonException)
        {
            // No well-formed JSON: the parser's own failure stands.
        }

        return new OtlpRequestCounts(messages, records);
    }

    private static OtlpValueBoundsResult BoundJsonValues(Span<byte> json, IngestLimits limits)
    {
        var scan = new Scan(limits);
        if (json.Length < MinScannedValueBytes) return default;
        try
        {
            // No depth bound of its own: the reader keeps one bit per level and this scan does not recurse with
            // the document, so a value nested too deep is found and emptied before a parser can refuse it.
            var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = int.MaxValue });
            while (reader.Read())
            {
                if (reader.TokenType is not JsonTokenType.PropertyName) continue;
                var structure = StructureNamed(ref reader);
                if (structure is Structure.None) continue;
                var start = (int)reader.TokenStartIndex;
                if (!reader.Read()) break;
                if (reader.TokenType is not JsonTokenType.StartObject)
                {
                    reader.Skip();
                    continue;
                }

                var depth = reader.CurrentDepth;
                var left = scan.MaxNested;
                var over = !WalkJsonContainer(ref reader, json, structure, 0, ref left, out var body, ref scan);
                // Over the count: the rest of the structure is passed over, token by token, without a count.
                while (over && !(reader.TokenType is JsonTokenType.EndObject && reader.CurrentDepth == depth))
                    if (!reader.Read()) return new OtlpValueBoundsResult(scan.Oversized, scan.TooDeep);
                over |= LengthDelimited(body) > scan.MaxValueBytes;
                var region = json[start..(int)reader.BytesConsumed];
                if (!over || region.Length < JsonSentinel.Length) continue;
                region.Fill((byte)' ');
                JsonSentinel.CopyTo(region);
                scan.Oversized++;
            }
        }
        catch (Exception malformed) when (malformed is JsonException or InvalidOperationException)
        {
            // No well-formed JSON: the payload goes on as it is and the parser's own failure stands.
        }

        return new OtlpValueBoundsResult(scan.Oversized, scan.TooDeep);
    }

    private static Structure StructureNamed(ref Utf8JsonReader reader) =>
        reader.ValueTextEquals("arrayValue"u8) || reader.ValueTextEquals("array_value"u8) ? Structure.Array
        : reader.ValueTextEquals("kvlistValue"u8) || reader.ValueTextEquals("kvlist_value"u8) ? Structure.KeyValueList
        : Structure.None;

    // The reader is on the opening brace of an ArrayValue or a KeyValueList and ends on its closing brace. body is
    // the length of its protobuf encoding. False as soon as the values no longer fit the count.
    private static bool WalkJsonContainer(ref Utf8JsonReader reader, Span<byte> json, Structure structure, int nesting,
        ref int left, out long body, ref Scan scan)
    {
        body = 0;
        while (reader.Read() && reader.TokenType is not JsonTokenType.EndObject)
        {
            var values = reader.ValueTextEquals("values"u8);
            _ = reader.Read();
            if (!values || reader.TokenType is not JsonTokenType.StartArray)
            {
                reader.Skip();
                continue;
            }

            while (reader.Read() && reader.TokenType is not JsonTokenType.EndArray)
            {
                if (reader.TokenType is not JsonTokenType.StartObject)
                {
                    reader.Skip();
                    continue;
                }

                if (--left < 0) return false;
                long item;
                if (structure is Structure.Array
                        ? !WalkJsonValue(ref reader, json, nesting + 1, ref left, out item, ref scan)
                        : !WalkJsonMember(ref reader, json, nesting + 1, ref left, out item, ref scan))
                    return false;
                body += LengthDelimited(item);
            }
        }

        return true;
    }

    // The reader is on the opening brace of a KeyValue and ends on its closing brace.
    private static bool WalkJsonMember(ref Utf8JsonReader reader, Span<byte> json, int nesting, ref int left,
        out long size, ref Scan scan)
    {
        size = 0;
        while (reader.Read() && reader.TokenType is not JsonTokenType.EndObject)
        {
            var key = reader.ValueTextEquals("key"u8);
            var value = !key && reader.ValueTextEquals("value"u8);
            _ = reader.Read();
            if (key && reader.TokenType is JsonTokenType.String)
            {
                // A string that is not in a oneof is left out of the encoding when it is empty.
                var length = Utf8Length(ref reader);
                if (length > 0) size += LengthDelimited(length);
            }
            else if (value && reader.TokenType is JsonTokenType.StartObject)
            {
                if (!WalkJsonValue(ref reader, json, nesting, ref left, out var inner, ref scan)) return false;
                size += LengthDelimited(inner);
            }
            else
            {
                reader.Skip();
            }
        }

        return true;
    }

    // The reader is on the opening brace of an AnyValue and ends on its closing brace. size is the length of its
    // protobuf encoding; an AnyValue that holds a structure below the deepest level is emptied.
    private static bool WalkJsonValue(ref Utf8JsonReader reader, Span<byte> json, int nesting, ref int left,
        out long size, ref Scan scan)
    {
        size = 0;
        var start = (int)reader.TokenStartIndex;
        var tooDeep = false;
        while (reader.Read() && reader.TokenType is not JsonTokenType.EndObject)
        {
            var structure = StructureNamed(ref reader);
            if (structure is not Structure.None)
            {
                _ = reader.Read();
                if (reader.TokenType is not JsonTokenType.StartObject || nesting >= scan.MaxNesting)
                {
                    tooDeep |= reader.TokenType is JsonTokenType.StartObject;
                    reader.Skip();
                    continue;
                }

                if (!WalkJsonContainer(ref reader, json, structure, nesting, ref left, out var body, ref scan))
                    return false;
                size += LengthDelimited(body);
                continue;
            }

            var text = reader.ValueTextEquals("stringValue"u8) || reader.ValueTextEquals("string_value"u8);
            var bytes = !text && (reader.ValueTextEquals("bytesValue"u8) || reader.ValueTextEquals("bytes_value"u8));
            var integer = reader.ValueTextEquals("intValue"u8) || reader.ValueTextEquals("int_value"u8);
            var boolean = reader.ValueTextEquals("boolValue"u8) || reader.ValueTextEquals("bool_value"u8);
            var floating = reader.ValueTextEquals("doubleValue"u8) || reader.ValueTextEquals("double_value"u8);
            _ = reader.Read();
            switch (reader.TokenType)
            {
                case JsonTokenType.String when text:
                    size += LengthDelimited(Utf8Length(ref reader));
                    break;
                case JsonTokenType.String when bytes:
                    size += LengthDelimited(Base64Length(ref reader));
                    break;
                case JsonTokenType.String or JsonTokenType.Number when integer:
                    size += 1 + IntegerVarintLength(ref reader);
                    break;
                case JsonTokenType.String or JsonTokenType.Number when floating:
                    size += 1 + sizeof(double);
                    break;
                case JsonTokenType.True or JsonTokenType.False when boolean:
                    size += 2;
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (!tooDeep) return true;
        var region = json[start..(int)reader.BytesConsumed];
        region.Fill((byte)' ');
        region[0] = (byte)'{';
        region[1] = (byte)'}';
        scan.TooDeep++;
        size = 0;
        return true;
    }

    // A field of this many bytes of content: one byte of tag (every field here has a number below 16), the varint
    // of the length, the content.
    private static long LengthDelimited(long content) => 1 + VarintLength((ulong)content) + content;

    // The UTF-8 length of the string the reader is on, with its escapes resolved.
    private static long Utf8Length(ref Utf8JsonReader reader)
    {
        var raw = reader.ValueSpan;
        if (!reader.ValueIsEscaped) return raw.Length;
        long length = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            length++;
            if (raw[i] != (byte)'\\') continue;
            if (++i >= raw.Length || raw[i] != (byte)'u') continue;
            var unit = HexUnit(raw, i + 1);
            i += 4;
            if (unit is >= 0xD800 and < 0xDC00 && i + 6 < raw.Length && raw[i + 1] == (byte)'\\' &&
                raw[i + 2] == (byte)'u' && HexUnit(raw, i + 3) is >= 0xDC00 and < 0xE000)
            {
                // A surrogate pair is one code point of four bytes.
                length += 3;
                i += 6;
            }
            else
            {
                // A lone surrogate is read as U+FFFD, three bytes like every other unit from U+0800 up.
                length += unit < 0x80 ? 0 : unit < 0x800 ? 1 : 2;
            }
        }

        return length;
    }

    private static int HexUnit(ReadOnlySpan<byte> raw, int at) =>
        at + 4 <= raw.Length && Utf8Parser.TryParse(raw.Slice(at, 4), out ushort unit, out var consumed, 'X') &&
        consumed == 4
            ? unit
            : 0;

    private static long Base64Length(ref Utf8JsonReader reader)
    {
        var raw = reader.ValueSpan;
        var characters = raw.Length;
        while (characters > 0 && raw[characters - 1] == (byte)'=') characters--;
        return characters * 3L / 4;
    }

    private static int IntegerVarintLength(ref Utf8JsonReader reader) =>
        Utf8Parser.TryParse(reader.ValueSpan, out long value, out var consumed) && consumed == reader.ValueSpan.Length
            ? VarintLength((ulong)value)
            : 1;
}
