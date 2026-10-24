namespace Nami.Collector.Storage;

internal static class StorageAttributeProjection
{
    internal static SpanHotAttributeProjection ExtractSpanHotAttributes(SpanIngestionRecord span)
    {
        var attributes = span.Attributes;
        return new(
            SessionId: attributes.GetSessionId(span.ScopeName, span.ServiceName),
            GenAiProviderName: attributes.GetString(CollectorSemanticAttributeCatalog.GenAiProviderName),
            GenAiOperationName: attributes.GetString(CollectorSemanticAttributeCatalog.GenAiOperationName),
            GenAiOutputType: attributes.GetString(CollectorSemanticAttributeCatalog.GenAiOutputType),
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
