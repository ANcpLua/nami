namespace Nami.Collector.Storage;

internal static class StorageAttributeProjection
{
    private static readonly FrozenSet<string> s_genAiOperationNames =
        CollectorSemanticAttributeCatalog.GenAiOperationNameValues.All.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> s_genAiOutputTypes =
        CollectorSemanticAttributeCatalog.GenAiOutputTypeValues.All.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    internal static SpanHotAttributeProjection ExtractSpanHotAttributes(SpanIngestionRecord span)
    {
        var attributes = span.Attributes;
        return new(
            SessionId: attributes.GetSessionId(span.ScopeName, span.ServiceName),
            GenAiProviderName: attributes.GetString(CollectorSemanticAttributeCatalog.GenAiProviderName),
            GenAiOperationName: Canonicalize(
                attributes.GetString(CollectorSemanticAttributeCatalog.GenAiOperationName),
                s_genAiOperationNames),
            GenAiOutputType: Canonicalize(
                attributes.GetString(CollectorSemanticAttributeCatalog.GenAiOutputType),
                s_genAiOutputTypes),
            GenAiRequestModel: attributes.GetString(CollectorSemanticAttributeCatalog.GenAiRequestModel),
            GenAiResponseModel: attributes.GetString(CollectorSemanticAttributeCatalog.GenAiResponseModel),
            GenAiInputTokens: attributes.GetInt64(CollectorSemanticAttributeCatalog.GenAiInputTokens),
            GenAiOutputTokens: attributes.GetInt64(CollectorSemanticAttributeCatalog.GenAiOutputTokens),
            GenAiCacheReadInputTokens: attributes.GetInt64(CollectorSemanticAttributeCatalog.GenAiCacheReadInputTokens),
            GenAiCacheWriteInputTokens: attributes.GetInt64(CollectorSemanticAttributeCatalog.GenAiCacheWriteInputTokens),
            GenAiReasoningTokens: attributes.GetInt64(CollectorSemanticAttributeCatalog.GenAiReasoningTokens));
    }

    private static string? GetString(this IReadOnlyDictionary<string, OtlpAttributeValue> attributes, string? key) =>
        key is null ? null : attributes.GetValueOrDefault(key)?.AsString();

    private static long? GetInt64(this IReadOnlyDictionary<string, OtlpAttributeValue> attributes, string? key) =>
        key is null ? null : attributes.GetValueOrDefault(key)?.AsInt64();

    private static string? Canonicalize(string? value, FrozenSet<string> allowedValues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return allowedValues.TryGetValue(value.Trim(), out var canonicalValue)
            ? canonicalValue
            : null;
    }
}

internal readonly record struct SpanHotAttributeProjection(
    string? SessionId,
    string? GenAiProviderName,
    string? GenAiOperationName,
    string? GenAiOutputType,
    string? GenAiRequestModel,
    string? GenAiResponseModel,
    long? GenAiInputTokens,
    long? GenAiOutputTokens,
    long? GenAiCacheReadInputTokens,
    long? GenAiCacheWriteInputTokens,
    long? GenAiReasoningTokens);
