namespace Nami.Collector.Ingestion;

internal static class AttributeKeySets
{
    internal static string[] ProjectIdResourceKeyPrecedence => [CollectorSemanticAttributeCatalog.NamiProjectId];

    // The allowlist by key alone: a key the registry defines, persists and has not deprecated. Storage applies it
    // again to every attribute it writes.
    internal static bool IsPersistable(string key) => !key.StartsWith("baggage.", StringComparison.OrdinalIgnoreCase) &&
                                                      AttributeStability.Of(key) != Stability.Deprecated &&
                                                      RegistryPolicy.IsPersisted(key);

    // The allowlist at ingest. A key the registry defines for a producer (Claude Code's model, type, success, source)
    // means what the registry says only on records from that producer's instrumentation scopes; from any other
    // sender, and in resource and scope attributes, it is an unknown key. producer is the producer of the record's
    // scope, or null.
    internal static bool IsPersistable(string key, string? producer) =>
        IsPersistable(key) && (RegistryPolicy.ProducerOf(key) is not { } owner ||
                               string.Equals(owner, producer, StringComparison.Ordinal));
}

internal static class AttributeLookupExtensions
{
    internal static string? GetSessionId(this IReadOnlyDictionary<string, OtlpAttributeValue> attributes)
    {
        return attributes.GetValueOrDefault(CollectorSemanticAttributeCatalog.SessionId)?.AsString() is
            { Length: > 0 } value
            ? value
            : null;
    }
}
