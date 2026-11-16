using System.Globalization;
using Google.Protobuf;
using Google.Protobuf.Collections;
using ProtoAnyValue = OpenTelemetry.Proto.Common.V1.AnyValue;
using ProtoKeyValue = OpenTelemetry.Proto.Common.V1.KeyValue;
using ProtoStatus = OpenTelemetry.Proto.Trace.V1.Status;

namespace Nami.Collector.Ingestion;

// Attributes and values, stored as they were sent (observer's decision, 16 Nov 2026): every key under its own name,
// every value in its own type, on every carrier. Ingest does two things to them and nothing else: the credential
// stage (CredentialRedaction) and the size bounds below. Sanitized first, bounded after: a credential where a cut
// falls is removed whole, not cut in half.
//
// What a value is comes from the data model of the OpenTelemetry Common Specification
// (https://opentelemetry.io/docs/specs/otel/common/#anyvalue): strings, numbers, booleans, bytes, arrays of mixed
// values, maps, nesting, empty values, keys that differ in case. Storing all of it as sent is nami's decision; the
// specification's rules there address APIs and SDKs, not a receiver.
//
// The bounds are nami's receiver policy (observer's rulings, 16 Nov 2026). The specification's attribute limits
// (https://opentelemetry.io/docs/specs/otel/common/#attribute-limits) are written for SDKs and are a reference point
// only. The defaults live in IngestLimits:
//   - one attribute value keeps at most 256 KiB of decoded size: the UTF-8 bytes of a string, the raw bytes of a
//     bytes value, and for an array or a map the sum of all nested keys and values, counted while it is read. A
//     string of up to 262,144 bytes stays whole; a longer one is cut at a code point boundary to 262,130 bytes and
//     ends in the 14 bytes of the marker, 262,144 exactly. An array or a map over the cap is dropped whole: half a
//     structure is no structure. A bytes value over the cap is dropped whole too, until a record can say in a
//     field of its own that a value was cut (ledger N45): a marker cannot sit inside binary data;
//   - a value nested deeper than 16 arrays and maps is replaced by an empty value;
//   - a span, span event, span link, log record and instrumentation scope keep 128 attributes at most; resource
//     and metric attributes have no count;
//   - one record keeps at most 1 MiB of attributes as nami stores them: the JSON of its attribute columns with
//     its escaping, Base64, keys and structure. The bytes are counted while the record is read (StoredString and
//     the charges in ConvertValue mirror the writer of PersistedAttributePolicy; a test holds them equal), so a
//     value within its own cap can still be too large for the record: 262,144 control characters are 1.5 MB of
//     JSON. This holds for resources and metric points too: a resource is copied into every row of its records.
// A record over a limit is reduced in an order that does not depend on the order its attributes were sent in: first
// the keys nami needs to say what a session cost (PriorityKeys), then the smallest attribute first, by its size on
// the wire, then by key. Smallest first keeps the largest number of attributes, and with it the identifiers and
// counters a reader joins on; what goes is the few largest values. Every attribute a bound removes is added to its
// carrier's dropped_attributes_count, on top of the sender's own count, and to nami's dropped-attributes counter.
// If the priority attributes alone do not fit, the record is refused through partial success: nami stores no
// record without them. One cap holds for every value: no key has a bound of its own. The span status message keeps
// its own 1024 bytes.
//
// Where the policy differs from the SDK limits: (1) UTF-8 bytes instead of characters; (2) depth 16 instead of 64;
// (3) the 1 MiB record budget also covers resource and metric attributes; (4) a deterministic reduction with
// priority keys first; (5) nami's own in-band marker on a cut value, which the specification does not define.
internal static partial class OtlpConverter
{
    // The defaults of IngestLimits, for tests and for a caller without configuration.
    internal const int MaxValueUtf8Bytes = IngestLimits.DefaultMaxValueUtf8Bytes;
    internal const int MaxAttributesPerRecord = IngestLimits.DefaultMaxAttributesPerRecord;
    internal const int MaxRecordAttributeBytes = IngestLimits.DefaultMaxRecordAttributeBytes;
    internal const int MaxValueNesting = IngestLimits.DefaultMaxValueNesting;

    // The bounds of the conversion that runs on this thread: those its caller passed, or the defaults. Conversion
    // is synchronous and builds its whole result before it returns, so the scope ends with the call.
    [ThreadStatic] private static IngestLimits? s_scopedLimits;
    private static readonly IngestLimits s_defaultLimits = new();
    private static IngestLimits Limits => s_scopedLimits ?? s_defaultLimits;

    private static T WithLimits<T>(IngestLimits? limits, Func<T> convert)
    {
        var outer = s_scopedLimits;
        s_scopedLimits = limits;
        try
        {
            return convert();
        }
        finally
        {
            s_scopedLimits = outer;
        }
    }

    // The longest span status message nami keeps, in UTF-8 bytes, whoever sent the span (observer's ruling, 15 Nov
    // 2026, for every sender since 16 Nov).
    private const int MaxStatusMessageUtf8Bytes = 1024;

    // What a bound before materialization (the ingress pre-scan, builder-ingress's part) can leave in place of a
    // value that could never fit the cap: the attribute that carries it is dropped and counted here, like any
    // attribute over the cap, so both drops reach the same dropped_attributes_count.
    internal const string OversizedValueSentinel = OtlpValueBounds.OversizedValueSentinel;

    // Kept first when a record is over a limit (observer's ruling, 16 Nov 2026, 02:25): the session, the token usage
    // and the cost, under the names of the GenAI conventions and under the names Claude Code sends them.
    private static readonly FrozenSet<string> s_priorityKeys = FrozenSet.Create(StringComparer.Ordinal,
        "anthropic.session.id", "session.id", "ccr.session.id", "input_tokens", "output_tokens", "cache_read_tokens",
        "cache_creation_tokens", "cache_creation_5m_tokens", "cache_creation_1h_tokens", "cost_usd", "cost_usd_micros");

    internal static bool IsPriorityKey(string key) =>
        s_priorityKeys.Contains(key) || key.StartsWith("gen_ai.usage.", StringComparison.Ordinal);

    // What is left of one record's 1 MiB, in bytes of stored JSON, and of the current attribute value's 256 KiB, in
    // decoded bytes. Conversion charges both as it goes and stops reading a value the moment it no longer fits, so
    // a value of four million empty items (two bytes each on the wire) costs no more than the items that fit.
    private sealed class RecordBudget
    {
        internal int Remaining = Limits.MaxRecordAttributeBytes;
        internal int Value = Limits.MaxValueUtf8Bytes;

        internal bool Exhausted => Remaining < 0 || Value < 0;

        // Bytes of the stored representation, against the record.
        internal void Store(long bytes) => Remaining = Remaining < 0 || bytes > Remaining ? -1 : Remaining - (int)bytes;

        // Decoded bytes, against the value cap.
        internal void Measure(long bytes) => Value = Value < 0 || bytes > Value ? -1 : Value - (int)bytes;
    }

    // Characters the storage writer (Utf8JsonWriter with its default encoder) writes as they are, one byte each.
    private static readonly SearchValues<char> s_writtenAsIs = SearchValues.Create(
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 !#$%()*,-./:;=?@[]^_{|}~");

    // The bytes a string takes in stored JSON, quotes and escapes included.
    private static long StoredString(string text) =>
        2 + (text.AsSpan().IndexOfAnyExcept(s_writtenAsIs) < 0
            ? text.Length
            : JsonEncodedText.Encode(text).EncodedUtf8Bytes.Length);

    private static int StoredDigits(long value)
    {
        Span<char> digits = stackalloc char[20];
        return value.TryFormat(digits, out var written, default, CultureInfo.InvariantCulture) ? written : 20;
    }

    private static int StoredDouble(double value)
    {
        // NaN and the infinities are stored as named literals in quotes.
        if (!double.IsFinite(value)) return double.IsNaN(value) ? 5 : value > 0 ? 10 : 11;
        Span<char> text = stackalloc char[32];
        return value.TryFormat(text, out var written, default, CultureInfo.InvariantCulture) ? written : 32;
    }

    /// <summary>The bytes of stored JSON the converter charges for these attributes; a test compares it with the writer.</summary>
    internal static long StoredBytesOf(RepeatedField<ProtoKeyValue> attributes)
    {
        var budget = new RecordBudget { Remaining = int.MaxValue, Value = int.MaxValue };
        var before = budget.Remaining;
        _ = TryConvertInOrder(attributes, attributes.Count, budget, false, 0, new NormalizedAttributes(), null, out _);
        return before - budget.Remaining;
    }

    // The one judge of a structure over the value cap, asked for every top-level array and key-value list: true is
    // treated exactly like the sentinel, drop and count. It is the rule of the bound before materialization, so
    // the ingress pre-scan and the converter drop the same structures.
    private static bool IsOversizedStructure(ProtoAnyValue value) => OtlpValueBounds.IsOversizedStructure(value, Limits);

    // A lone surrogate is no character: UTF-8 cannot carry it, and the JSON writer would write U+FFFD in its place
    // without a word. Ingest replaces it here, before anything else reads the text, and counts the replacement.
    private static string WellFormed(string text)
    {
        if (text.AsSpan().IndexOfAnyInRange('\uD800', '\uDFFF') < 0) return text;
        var builder = new StringBuilder(text.Length);
        var replaced = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append(text[i]).Append(text[++i]);
            }
            else if (char.IsSurrogate(text[i]))
            {
                builder.Append('\uFFFD');
                replaced++;
            }
            else
            {
                builder.Append(text[i]);
            }
        }

        NamiCollectorMetrics.ValueReplaced(replaced, "invalid_utf16");
        return builder.ToString();
    }

    // Text nami keeps that is no attribute value: a key, a span, event or metric name, severity text, scope and
    // schema text, trace state. Through the credential stage, then the value cap.
    private static string Kept(string value) => Bounded(CredentialRedaction.Redact(WellFormed(value)));

    private static string? KeptOrNull(string? value) => string.IsNullOrEmpty(value) ? null : Kept(value);

    // A string of n UTF-16 units is at most 3n UTF-8 bytes, so most strings need no count.
    private static string Bounded(string text) =>
        text.Length <= Limits.MaxValueUtf8Bytes / 3 ? text : BoundedText.Truncate(text, Limits.MaxValueUtf8Bytes);

    // A span status message is free text that no key names. Claude Code copies the text of its error attribute into
    // it. It is kept, whoever sent it: through the credential stage like every other string, then cut at 1024 UTF-8
    // bytes with the marker. The failure metric names it by the attribute OpenTelemetry gives the status description
    // outside OTLP.
    private static string? StatusMessageOf(ProtoStatus? status) =>
        string.IsNullOrEmpty(status?.Message)
            ? null
            : BoundedText.Truncate(CredentialRedaction.Redact(WellFormed(status.Message), OtelAttributes.StatusDescription),
                MaxStatusMessageUtf8Bytes);

    // The attributes of one carrier. counted says whether the carrier keeps 128 attributes at most (records and
    // scopes) or is exempt (resources, metric points, exemplars, metric metadata). dropped is what the bounds
    // removed, for the carrier's dropped_attributes_count. keys, when given, receives the stored key of each kept
    // attribute by the key it was sent under.
    private static NormalizedAttributes ConvertAttributes(RepeatedField<ProtoKeyValue> source, RecordBudget budget,
        bool counted, out uint dropped, NormalizedAttributes? into = null, Dictionary<string, string>? keys = null,
        int nesting = 0)
    {
        // Nearly every record is within every limit: read in the order sent, nothing is dropped, and the order
        // does not matter. The first attribute a bound would remove ends this reading; the record is then read
        // again in the order of the reduction, so what is kept never depends on the order it was sent in.
        var remaining = budget.Remaining;
        var attributes = new NormalizedAttributes();
        var stored = keys is null ? null : new Dictionary<string, string>(StringComparer.Ordinal);
        if (!TryConvertInOrder(source, source.Count, budget, counted, nesting, attributes, stored, out dropped))
        {
            budget.Remaining = remaining;
            attributes = new NormalizedAttributes();
            stored?.Clear();
            var reduced = source.Where(static attribute => !IsUnresolvedProfilingKey(attribute))
                .Select(static attribute => (Attribute: attribute, Size: attribute.CalculateSize()))
                .OrderBy(static item => IsPriorityKey(item.Attribute.Key) ? 0 : 1).ThenBy(static item => item.Size)
                .ThenBy(static item => item.Attribute.Key, StringComparer.Ordinal).Select(static item => item.Attribute);
            _ = TryConvertInOrder(reduced, source.Count, budget, counted, nesting, attributes, stored, out dropped, true);
        }

        if (stored is not null)
            foreach (var (sent, key) in stored) keys!.TryAdd(sent, key);
        if (into is null) return attributes;
        foreach (var (key, value) in attributes) into[key] = value;
        return into;
    }

    // Reads the attributes in the order given. Without reduce it stops and answers false at the first attribute a
    // bound would remove. With reduce it drops and counts that attribute and goes on, and refuses the record when the
    // attribute is one of the priority keys.
    private static bool TryConvertInOrder(IEnumerable<ProtoKeyValue> source, int count, RecordBudget budget, bool counted,
        int nesting, NormalizedAttributes attributes, Dictionary<string, string>? keys, out uint dropped,
        bool reduce = false)
    {
        dropped = 0;
        // The braces of the stored object; each attribute brings its own colon and comma.
        budget.Store(1);
        var sent = new Dictionary<string, ProtoAnyValue?>(Math.Min(count, Limits.MaxAttributesPerRecord), StringComparer.Ordinal);
        var kept = 0;
        foreach (var attribute in source)
        {
            if (IsUnresolvedProfilingKey(attribute)) continue;
            if (string.IsNullOrWhiteSpace(attribute.Key))
                throw new InvalidDataException("OTLP attributes require nonempty, unique keys.");
            if (!sent.TryAdd(attribute.Key, attribute.Value))
            {
                // The SDK exporter flattens ILogger state and scopes, which can repeat the same
                // typed value (for example TraceId). Coalesce identical entries, never distinct values.
                // https://github.com/open-telemetry/opentelemetry-specification/blob/v1.61.0/specification/common/README.md#mapstring-anyvalue
                if (!Equals(sent[attribute.Key], attribute.Value))
                    throw new InvalidDataException("OTLP attributes require unique keys; duplicate values conflict.");
                NamiCollectorMetrics.AttributeCoalesced(attribute.Key);
                continue;
            }

            var fits = !(counted && kept >= Limits.MaxAttributesPerRecord) && budget.Remaining > 0;
            var before = budget.Remaining;
            if (fits)
            {
                // Stored under the key it was sent under. The key is text the sender chose, so it passes the
                // credential stage like a value.
                var key = Kept(attribute.Key);
                budget.Store(StoredString(key) + 2);
                budget.Value = Limits.MaxValueUtf8Bytes;
                var value = ConvertAttribute(key, attribute.Value, budget, nesting);
                fits = !budget.Exhausted;
                if (fits)
                {
                    attributes[key] = value;
                    keys?.TryAdd(attribute.Key, key);
                    kept++;
                    continue;
                }
            }

            // Over the count, over the record's bytes, or a structure over the value cap.
            budget.Remaining = before;
            budget.Value = Limits.MaxValueUtf8Bytes;
            if (!reduce) return false;
            if (nesting == 0 && IsPriorityKey(attribute.Key))
            {
                throw new InvalidDataException(
                    "OTLP record does not fit nami's limits even with its session, usage and cost attributes alone " +
                    $"({Limits.MaxAttributesPerRecord} attributes, {Limits.MaxValueUtf8Bytes} bytes per value, {Limits.MaxRecordAttributeBytes} bytes per record).");
            }

            dropped++;
            NamiCollectorMetrics.AttributeDropped();
        }

        return true;
    }

    // A profiling string-table key has no meaning in these signals. OTLP requires its
    // presence to be non-fatal; do not mistake it for a malformed ordinary empty key.
    // https://github.com/open-telemetry/opentelemetry-proto/blob/v1.11.1/opentelemetry/proto/common/v1/common.proto
    private static bool IsUnresolvedProfilingKey(ProtoKeyValue attribute) =>
        attribute.Key.Length is 0 && attribute.KeyStrindex is not 0;

    // The value of one attribute: every key is kept, in the type it was sent in.
    private static OtlpAttributeValue ConvertAttribute(string key, ProtoAnyValue? wire, RecordBudget budget, int nesting) =>
        ConvertValue(wire, key, budget, nesting);

    // The members of a map value, read in the order sent. False when one does not fit.
    private static bool TryConvertMembers(RepeatedField<ProtoKeyValue> source, RecordBudget budget, int nesting,
        NormalizedAttributes members)
    {
        var sent = new Dictionary<string, ProtoAnyValue?>(StringComparer.Ordinal);
        foreach (var member in source)
        {
            if (IsUnresolvedProfilingKey(member)) continue;
            if (string.IsNullOrWhiteSpace(member.Key))
                throw new InvalidDataException("OTLP attributes require nonempty, unique keys.");
            if (!sent.TryAdd(member.Key, member.Value))
            {
                if (!Equals(sent[member.Key], member.Value))
                    throw new InvalidDataException("OTLP attributes require unique keys; duplicate values conflict.");
                NamiCollectorMetrics.AttributeCoalesced(member.Key);
                continue;
            }

            var key = Kept(member.Key);
            budget.Measure(Utf8Bytes(key));
            // The key, its colon, and a comma for every member but the first.
            budget.Store(StoredString(key) + (members.Count > 0 ? 2 : 1));
            members[key] = ConvertAttribute(key, member.Value, budget, nesting);
            if (budget.Exhausted) return false;
        }

        return true;
    }

    private static int Utf8Bytes(string text) => Encoding.UTF8.GetByteCount(text);

    // All carriers converge here: attribute values, the items of arrays, the members of maps and log bodies. Every
    // string passes the credential stage and then the value cap; no key is exempt and none has a rule of its own.
    // Each value is measured twice: its decoded size against the value cap, and the bytes of JSON nami stores for
    // it against the record. Where the key of the attribute is itself a credential name
    // (http.request.header.authorization as a list of strings), the strings of a list go whole, as a single string
    // does.
    private static OtlpAttributeValue ConvertValue(ProtoAnyValue? value, string? attributeKey, RecordBudget budget,
        int nesting)
    {
        if (budget.Exhausted) return OtlpAttributeValue.Empty;
        switch (value?.ValueCase)
        {
            case ProtoAnyValue.ValueOneofCase.StringValue:
                // What the bound before materialization left of a structure over the cap, wherever it sits: the
                // attribute or log body that holds it is dropped and counted, like one this converter measured.
                if (string.Equals(value.StringValue, OversizedValueSentinel, StringComparison.Ordinal))
                {
                    budget.Value = -1;
                    return OtlpAttributeValue.Empty;
                }

                var text = Bounded(CredentialRedaction.RedactValue(attributeKey, WellFormed(value.StringValue)));
                budget.Measure(Utf8Bytes(text));
                budget.Store(StoredString(text));
                return OtlpAttributeValue.FromString(text);
            case ProtoAnyValue.ValueOneofCase.IntValue or ProtoAnyValue.ValueOneofCase.DoubleValue
                when attributeKey is not null && CredentialRedaction.NamesACredential(attributeKey):
                // A number under a credential name (a PIN, a numeric password) goes like a string would.
                budget.Measure(CredentialRedaction.Replacement.Length);
                budget.Store(StoredString(CredentialRedaction.Replacement));
                return OtlpAttributeValue.FromString(CredentialRedaction.Replacement);
            case ProtoAnyValue.ValueOneofCase.IntValue:
                budget.Measure(8);
                budget.Store(25 + StoredDigits(value.IntValue));
                return OtlpAttributeValue.FromInt(value.IntValue);
            case ProtoAnyValue.ValueOneofCase.DoubleValue:
                budget.Measure(8);
                budget.Store(26 + StoredDouble(value.DoubleValue));
                return OtlpAttributeValue.FromDouble(value.DoubleValue);
            case ProtoAnyValue.ValueOneofCase.BoolValue:
                budget.Measure(1);
                budget.Store(value.BoolValue ? 4 : 5);
                return OtlpAttributeValue.FromBool(value.BoolValue);
            case ProtoAnyValue.ValueOneofCase.BytesValue:
                // Bytes over the cap are dropped whole and counted, like a structure. A cut needs a field of the
                // record that says so (ledger N45): a marker cannot sit inside binary data, and a cut value must
                // never look complete.
                // Bytes under a credential name cannot be read for what they hold: the attribute is dropped and
                // counted as well.
                if (value.BytesValue.Length > Limits.MaxValueUtf8Bytes ||
                    (attributeKey is not null && value.BytesValue.Length > 0 &&
                     CredentialRedaction.NamesACredential(attributeKey)))
                {
                    budget.Value = -1;
                    return OtlpAttributeValue.Empty;
                }

                budget.Measure(value.BytesValue.Length);
                budget.Store(28 + 4L * ((value.BytesValue.Length + 2) / 3));
                return OtlpAttributeValue.FromBytes(value.BytesValue.ToByteArray());
            case ProtoAnyValue.ValueOneofCase.ArrayValue:
                {
                    if (nesting >= Limits.MaxValueNesting)
                    {
                        NamiCollectorMetrics.ValueReplaced(1, "depth");
                        break;
                    }

                    if (nesting == 0 && IsOversizedStructure(value))
                    {
                        budget.Value = -1;
                        return OtlpAttributeValue.Empty;
                    }

                    // An item is three stored bytes at least (an empty string and its comma), so a list that
                    // cannot fit the record is not read at all.
                    if (1 + value.ArrayValue.Values.Count * 3L > budget.Remaining)
                    {
                        budget.Remaining = -1;
                        return OtlpAttributeValue.Empty;
                    }

                    budget.Store(2);
                    var ofACredential = attributeKey is not null && CredentialRedaction.NamesACredential(attributeKey);
                    var items = new List<OtlpAttributeValue>(value.ArrayValue.Values.Count);
                    var afterFlag = false;
                    foreach (var item in value.ArrayValue.Values)
                    {
                        // An empty value and a null inside an array are kept as they are. Every item but the
                        // first brings a comma.
                        if (items.Count > 0) budget.Store(1);
                        // In a list of arguments (process.command_args) the value of a credential flag is the
                        // next item: it goes whole, as under a credential name.
                        items.Add(ConvertValue(item, ofACredential ? attributeKey : afterFlag ? "password" : null, budget,
                            nesting + 1));
                        if (budget.Exhausted) return OtlpAttributeValue.Empty;
                        afterFlag = item?.ValueCase is ProtoAnyValue.ValueOneofCase.StringValue &&
                                    CredentialRedaction.IsCredentialFlag(item.StringValue);
                    }

                    return OtlpAttributeValue.FromArray(items);
                }
            case ProtoAnyValue.ValueOneofCase.KvlistValue:
                {
                    if (nesting >= Limits.MaxValueNesting)
                    {
                        NamiCollectorMetrics.ValueReplaced(1, "depth");
                        break;
                    }

                    if (nesting == 0 && IsOversizedStructure(value))
                    {
                        budget.Value = -1;
                        return OtlpAttributeValue.Empty;
                    }

                    // A member is seven stored bytes at least, inside 29 of wrapper.
                    if (28 + value.KvlistValue.Values.Count * 7L > budget.Remaining)
                    {
                        budget.Remaining = -1;
                        return OtlpAttributeValue.Empty;
                    }

                    // {"type":"kvlist","values":{ and }}.
                    budget.Store(29);
                    // A map inside a value is bounded by the bytes, not by the count of top-level attributes, and
                    // it is kept whole or not at all: a member that does not fit means the map does not.
                    var members = new NormalizedAttributes();
                    var whole = TryConvertMembers(value.KvlistValue.Values, budget, nesting + 1, members);
                    if (!whole) budget.Value = -1;
                    return whole ? OtlpAttributeValue.FromKeyValueList(members) : OtlpAttributeValue.Empty;
                }
            case null:
            case ProtoAnyValue.ValueOneofCase.None:
            // Alpha, profiles-only (opentelemetry-proto 1.10+): an index into a string table no other
            // signal carries. The proto asks non-profiling receivers to treat it as absent, which is what
            // it was while the field was unknown.
            case ProtoAnyValue.ValueOneofCase.StringValueStrindex:
                break;
            default:
                throw new InvalidDataException("OTLP AnyValue contains an unknown value kind.");
        }

        // An empty value, and a value nested deeper than the bound, which becomes one.
        budget.Store(4);
        return OtlpAttributeValue.Empty;
    }
}
