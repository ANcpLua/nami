namespace Nami.Collector.Ingestion;

internal static class AttributeKeySets
{
    internal static string[] ProjectIdResourceKeyPrecedence => [CollectorSemanticAttributeCatalog.NamiProjectId];

    // Keys are stored as they were sent (observer's decision, 16 Nov 2026): a key the registry does not know, a
    // deprecated key, a baggage key and a sender's own key are all kept under their own name, on every scope. What
    // is left of the allowlist is the registry's explicit refusal, nami.persisted: false on a key that is not
    // deprecated, which names content and identity keys. Storage applies it again to every attribute it writes.
    internal static bool IsPersistable(string key) =>
        AttributeCatalog.Find(key) is not { IsPersisted: false, Stability: not Stability.Deprecated };
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
