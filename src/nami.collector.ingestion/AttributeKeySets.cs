namespace Nami.Collector.Ingestion;

internal static class AttributeKeySets
{
    internal static FrozenSet<string> SessionCorrelation { get; } =
        new[] { CollectorSemanticAttributeCatalog.SessionId }.ToFrozenSet(StringComparer.Ordinal);

    internal static string[] ProjectIdResourceKeyPrecedence => [CollectorSemanticAttributeCatalog.NamiProjectId];
    internal static bool ShouldCaptureSpanAttribute(string key) => IsPersistable(key);

    internal static bool IsPersistable(string key) => !key.StartsWith("baggage.", StringComparison.OrdinalIgnoreCase) &&
                                                      AttributeStability.Of(key) != Stability.Deprecated &&
                                                      RegistryPolicy.IsPersisted(key);
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

    internal static bool IsAny(this string key, FrozenSet<string> candidates) => candidates.Contains(key);
}
