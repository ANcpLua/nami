using System.Collections.Generic;

namespace Nami.Collector.Storage;

internal static class StorageAttributeProjection
{
    internal static SpanHotAttributeProjection ExtractSpanHotAttributes(SpanIngestionRecord span)
    {
        var attributes = span.Attributes;
        return new SpanHotAttributeProjection(
            attributes.GetSessionId(span.ScopeName, span.ServiceName),
            attributes.GetString(CollectorSemanticAttributeCatalog.GenAiProviderName),
            attributes.GetString(CollectorSemanticAttributeCatalog.GenAiOperationName),
            attributes.GetString(CollectorSemanticAttributeCatalog.GenAiOutputType),
            attributes.GetString(CollectorSemanticAttributeCatalog.GenAiRequestModel),
            attributes.GetString(CollectorSemanticAttributeCatalog.GenAiResponseModel),
            attributes.GetInt64(CollectorSemanticAttributeCatalog.GenAiInputTokens),
            attributes.GetInt64(CollectorSemanticAttributeCatalog.GenAiOutputTokens),
            attributes.GetInt64(CollectorSemanticAttributeCatalog.GenAiCacheReadInputTokens),
            attributes.GetInt64(CollectorSemanticAttributeCatalog.GenAiCacheWriteInputTokens),
            attributes.GetInt64(CollectorSemanticAttributeCatalog.GenAiReasoningTokens));
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
