using ProtoAnyValue = OpenTelemetry.Proto.Common.V1.AnyValue;
using ProtoStatus = OpenTelemetry.Proto.Trace.V1.Status;

namespace Nami.Collector.Ingestion;

// What the registry declares for a producer, a sender nami describes under its own wire names (Claude Code), applied
// to the records that arrive under one of that producer's instrumentation scopes (Producers.Scopes). The scope name is
// the sender's own word: it says which meaning a key has, it does not authenticate the sender, so every rule here
// narrows what is kept and none widens it beyond the allowlist.
internal static partial class OtlpConverter
{
    // The longest span status message nami keeps of a producer's span, in UTF-8 bytes (observer's ruling, 15 Nov 2026).
    private const int MaxProducerStatusMessageUtf8Bytes = 1024;

    private static readonly FrozenDictionary<string, string> s_producerByScope = Producers.Scopes
        .SelectMany(static producer => producer.Value.Select(scope => KeyValuePair.Create(scope, producer.Key)))
        .ToFrozenDictionary(StringComparer.Ordinal);

    private static string? ProducerOfScope(string? scopeName) =>
        scopeName is not null && s_producerByScope.TryGetValue(scopeName, out var producer) ? producer : null;

    // The value nami keeps of an attribute. Every key is kept (observer's decision, 16 Nov 2026: telemetry is stored
    // as sent; what a sender captures is decided at the source). One path for every carrier: resource, scope, span,
    // span event, span link, log record, metric point, exemplar and the members of a map value. A map member passes
    // its list's nesting on, so MaxValueNesting bounds a value however deep its maps go.
    private static OtlpAttributeValue ConvertAttribute(string key, ProtoAnyValue? wire, string? producer,
        int nesting = 0)
    {
        // Sanitized first, bounded after: every string passes the credential stage in ConvertProtoAnyValue, so a
        // credential where the cut falls is removed whole, not cut in half. No key names a sanitizer of its own.
        // Where the key itself is a credential name (http.request.header.authorization as a list of strings), the
        // strings of a list go whole as a single string does.
        var value = ConvertProtoAnyValue(wire, key, producer, nesting);
        if (value.Kind is OtlpAttributeValueKind.Array && CredentialRedaction.NamesACredential(key))
            value = WithoutStrings(value);

        // A value keeps the type it was sent in (observer's decision, 16 Nov 2026): a string of digits stays a
        // string, and a reader that needs the number parses it when it reads. A key the registry bounds
        // (nami.max_utf8_bytes) is cut when its value is a string; a value of another type is kept as sent.
        return ValueLimits.MaxUtf8Bytes.TryGetValue(key, out var maxUtf8Bytes) && value.AsString() is { } text
            ? OtlpAttributeValue.FromString(BoundedText.Truncate(text, maxUtf8Bytes))
            : value;
    }

    private static OtlpAttributeValue WithoutStrings(OtlpAttributeValue list) => OtlpAttributeValue.FromArray(
        list.AsArray()!.Select(static item => item.AsString() is { Length: > 0 }
            ? OtlpAttributeValue.FromString(CredentialRedaction.Replacement)
            : item).ToList());

    // A span status message is free text that no key names. Claude Code copies the text of its error attribute into
    // it. It is kept, through the credential stage like every other string, whoever sent it. A producer's is then cut
    // at 1024 UTF-8 bytes. The failure metric names it by the attribute OpenTelemetry gives the status description
    // outside OTLP.
    private static string? StatusMessageOf(ProtoStatus? status, string? producer)
    {
        if (string.IsNullOrEmpty(status?.Message)) return null;
        var message = CredentialRedaction.Redact(status.Message, OtelAttributes.StatusDescription);
        return producer is null ? message : BoundedText.Truncate(message, MaxProducerStatusMessageUtf8Bytes);
    }
}
