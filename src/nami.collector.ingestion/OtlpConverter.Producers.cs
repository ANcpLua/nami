using Google.Protobuf.Collections;
using ProtoAnyValue = OpenTelemetry.Proto.Common.V1.AnyValue;
using ProtoKeyValue = OpenTelemetry.Proto.Common.V1.KeyValue;
using ProtoLogRecord = OpenTelemetry.Proto.Logs.V1.LogRecord;
using ProtoStatus = OpenTelemetry.Proto.Trace.V1.Status;

namespace Nami.Collector.Ingestion;

// What the registry declares for a producer, a sender nami describes under its own wire names (Claude Code), applied
// to the records that arrive under one of that producer's instrumentation scopes (Producers.Scopes). The scope name is
// the sender's own word: it says which meaning a key has, it does not authenticate the sender, so every rule here
// narrows what is kept and none widens it beyond the allowlist.
internal static partial class OtlpConverter
{
    private static readonly FrozenDictionary<string, string> s_producerByScope = Producers.Scopes
        .SelectMany(static producer => producer.Value.Select(scope => KeyValuePair.Create(scope, producer.Key)))
        .ToFrozenDictionary(StringComparer.Ordinal);

    private static string? ProducerOfScope(string? scopeName) =>
        scopeName is not null && s_producerByScope.TryGetValue(scopeName, out var producer) ? producer : null;

    // The value nami keeps of an attribute, or null when it keeps none. One path for every carrier: resource, scope,
    // span, span event, span link, log record, metric point, exemplar and the members of a map value.
    private static OtlpAttributeValue? ConvertAttribute(string key, ProtoAnyValue? wire, string? producer)
    {
        if (!AttributeKeySets.IsPersistable(key, producer)) return null;

        var value = ConvertProtoAnyValue(wire, key, producer);

        // A key the registry bounds (nami.max_utf8_bytes) is a string by the registry's own policy. Any other
        // value, such as a list of strings, would pass the bound unmeasured, so it is not kept.
        if (ValueLimits.MaxUtf8Bytes.TryGetValue(key, out var maxUtf8Bytes))
        {
            return value.AsString() is { } text
                ? OtlpAttributeValue.FromString(BoundedText.Truncate(text, maxUtf8Bytes))
                : null;
        }

        return producer is not null && string.Equals(RegistryPolicy.ProducerOf(key), producer, StringComparison.Ordinal)
            ? ToDeclaredType(key, value)
            : value;
    }

    // The registry gives each producer key the type nami stores; where the producer sends that value as a string
    // (nami.wire_type: string, Claude Code's success, duration_ms and sizes on tool_result, prompt_length, safe_mode)
    // it is converted here. The generated code carries no table of wire types, so the rule follows the stored type:
    // a string that reads as the declared int, double or boolean becomes one, and any other string stays as sent.
    private static OtlpAttributeValue ToDeclaredType(string key, OtlpAttributeValue value)
    {
        if (value.AsString() is not { } text) return value;

        const NumberStyles integer = NumberStyles.AllowLeadingSign;
        const NumberStyles real = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;
        return AttributeCatalog.Find(key)?.Type switch
        {
            "int" when long.TryParse(text, integer, CultureInfo.InvariantCulture, out var number) =>
                OtlpAttributeValue.FromInt(number),
            "double" when double.TryParse(text, real, CultureInfo.InvariantCulture, out var number) &&
                          double.IsFinite(number) => OtlpAttributeValue.FromDouble(number),
            "boolean" when text is "true" or "false" => OtlpAttributeValue.FromBool(text is "true"),
            _ => value
        };
    }

    // A producer nests some keys in a JSON string attribute (nami.wire_container; Claude Code's tool_parameters holds
    // bash_command, full_command and subagent_type next to the model-written description). The members the registry
    // names (WireContainers.Members) become attributes of the record itself, ahead of the allowlist, which then treats
    // them like any other key; the container and every other member are dropped. An attribute the record carries
    // itself wins over a member of the same name. A container that is not a JSON object yields nothing.
    private static IReadOnlyList<ProtoKeyValue> LiftWireContainers(RepeatedField<ProtoKeyValue> attributes,
        string? producer)
    {
        if (producer is null || !attributes.Any(attribute => IsWireContainer(attribute.Key, producer)))
            return attributes;

        var keys = attributes.Select(static attribute => attribute.Key).ToHashSet(StringComparer.Ordinal);
        var lifted = new List<ProtoKeyValue>(attributes.Count);
        foreach (var attribute in attributes)
        {
            if (!IsWireContainer(attribute.Key, producer))
            {
                lifted.Add(attribute);
                continue;
            }

            NamiCollectorMetrics.AttributeDropped(attribute.Key);
            lifted.AddRange(WireContainerMembers(attribute).Where(member => keys.Add(member.Key)));
        }

        return lifted;
    }

    private static bool IsWireContainer(string key, string producer) =>
        WireContainers.Members.ContainsKey(key) &&
        string.Equals(RegistryPolicy.ProducerOf(key), producer, StringComparison.Ordinal);

    private static List<ProtoKeyValue> WireContainerMembers(ProtoKeyValue container)
    {
        var members = new List<ProtoKeyValue>();
        if (container.Value is not { ValueCase: ProtoAnyValue.ValueOneofCase.StringValue } text) return members;

        try
        {
            using var document = JsonDocument.Parse(text.StringValue);
            if (document.RootElement.ValueKind is not JsonValueKind.Object) return members;
            foreach (var name in WireContainers.Members[container.Key])
            {
                if (!document.RootElement.TryGetProperty(name, out var member)) continue;
                var value = member.ValueKind switch
                {
                    JsonValueKind.String => new ProtoAnyValue { StringValue = member.GetString() },
                    JsonValueKind.True or JsonValueKind.False => new ProtoAnyValue { BoolValue = member.GetBoolean() },
                    JsonValueKind.Number when member.TryGetInt64(out var number) =>
                        new ProtoAnyValue { IntValue = number },
                    JsonValueKind.Number when member.TryGetDouble(out var number) && double.IsFinite(number) =>
                        new ProtoAnyValue { DoubleValue = number },
                    _ => null
                };
                if (value is not null) members.Add(new ProtoKeyValue { Key = name, Value = value });
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            // Not JSON, or a string with a broken escape (GetString throws InvalidOperationException for an unpaired
            // surrogate): nothing is lifted. The text itself is never kept or logged.
            members.Clear();
        }

        return members;
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
    // error attribute into it, which the registry does not persist because it can quote content, so a producer's
    // span keeps its status code and not its message. Other senders' messages are stored as sent.
    private static string? StatusMessageOf(ProtoStatus? status, string? producer) =>
        producer is null ? NullIfEmpty(status?.Message) : null;
}
