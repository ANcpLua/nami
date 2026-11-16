namespace Nami.Collector.Ingestion;

internal static class AttributeKeySets
{
    internal static string[] ProjectIdResourceKeyPrecedence => [CollectorSemanticAttributeCatalog.NamiProjectId];
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
