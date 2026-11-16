using ProtoAnyValue = OpenTelemetry.Proto.Common.V1.AnyValue;
using ProtoLogRecord = OpenTelemetry.Proto.Logs.V1.LogRecord;
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

    // Error text a producer sends under its own key. The registry asks for the sanitizer of exception.message before
    // the key's bound (registry/source/claude_code/attributes.yaml, error); the generated code has no table for that
    // yet, so the key is named here. Every member is a producer key, which the allowlist keeps to the producer's
    // records.
    private static readonly FrozenSet<string> s_producerErrorTextKeys =
        FrozenSet.Create(StringComparer.Ordinal, ClaudeCodeAttributes.Error);

    private static readonly FrozenDictionary<string, string> s_producerByScope = Producers.Scopes
        .SelectMany(static producer => producer.Value.Select(scope => KeyValuePair.Create(scope, producer.Key)))
        .ToFrozenDictionary(StringComparer.Ordinal);

    private static string? ProducerOfScope(string? scopeName) =>
        scopeName is not null && s_producerByScope.TryGetValue(scopeName, out var producer) ? producer : null;

    // The value nami keeps of an attribute, or null when it keeps none. One path for every carrier: resource, scope,
    // span, span event, span link, log record, metric point, exemplar and the members of a map value. A map member
    // passes its list's nesting on, so MaxValueNesting bounds a value however deep its maps go.
    private static OtlpAttributeValue? ConvertAttribute(string key, ProtoAnyValue? wire, string? producer,
        int nesting = 0)
    {
        if (!AttributeKeySets.IsPersistable(key)) return null;

        // Sanitized first, bounded after: a credential where the cut falls is removed whole, not cut in half. Error
        // text is text: a value of another type is no error text and stays as it was sent.
        var value = s_producerErrorTextKeys.Contains(key) && wire?.ValueCase is ProtoAnyValue.ValueOneofCase.StringValue
            ? OtlpAttributeValue.FromString(ExceptionTextSanitizer.Sanitize(key, wire))
            : ConvertProtoAnyValue(wire, key, producer, nesting);

        // A value keeps the type it was sent in (observer's decision, 16 Nov 2026): a string of digits stays a
        // string, and a reader that needs the number parses it when it reads. A key the registry bounds
        // (nami.max_utf8_bytes) is cut when its value is a string; a value of another type is kept as sent.
        return ValueLimits.MaxUtf8Bytes.TryGetValue(key, out var maxUtf8Bytes) && value.AsString() is { } text
            ? OtlpAttributeValue.FromString(BoundedText.Truncate(text, maxUtf8Bytes))
            : value;
    }

    // The record's EventName field wins. A producer that still names its events in the deprecated event.name
    // attribute sends the unqualified name there (Claude Code: tool_result) and the registry defines the event under
    // the producer's namespace (claude_code.tool_result), so the attribute's value gets that prefix unless it has it.
    private static string? EventNameOf(ProtoLogRecord log, string? producer)
    {
        if (NullIfEmpty(log.EventName) is { } field) return field;
        if (DeprecatedEventNameAttribute(log.Attributes) is not { } name) return null;
        return producer is null || name.StartsWith(producer + ".", StringComparison.Ordinal)
            ? name
            : producer + "." + name;
    }

    // A log body is outside the allowlist: no key names it, so no registry decision covers what a sender puts there.
    // A producer's event carries its name in the body (Claude Code: the string claude_code.<name>) and nothing else
    // belongs there, so on a producer's record the body is kept only when it is that name.
    private static OtlpAttributeValue ProducerBody(OtlpAttributeValue body, string? eventName, string? producer) =>
        producer is null || (body.AsString() is { } text && string.Equals(text, eventName, StringComparison.Ordinal))
            ? body
            : OtlpAttributeValue.Empty;

    // A span status message is free text outside the allowlist: no key names it. Claude Code copies the text of its
    // error attribute into it. That text says why a tool call or a request failed, so a producer's is kept, but as
    // error text is kept elsewhere: through the sanitizer exception text gets, which removes credentials in URLs,
    // assignments and authorization values, and then up to 1024 UTF-8 bytes. The failure metric names it by the
    // attribute OpenTelemetry gives the status description outside OTLP. Other senders' messages are stored as sent.
    private static string? StatusMessageOf(ProtoStatus? status, string? producer) =>
        NullIfEmpty(status?.Message) is not { } message ? null :
        producer is null ? message :
        BoundedText.Truncate(ExceptionTextSanitizer.Sanitize(OtelAttributes.StatusDescription, message),
            MaxProducerStatusMessageUtf8Bytes);
}
