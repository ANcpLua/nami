using Nami.Telemetry.SemanticConventions.Incubating.Mapping;

namespace Nami.Collector.Ingestion;

internal static class DeprecatedAttributeNormalizer
{
    /// <summary>
    /// The live key for <paramref name="key"/>, resolved from the generated rename table first.
    /// Returns <see langword="true"/> when the key was renamed, so the caller keeps a value already
    /// written under the canonical spelling.
    /// </summary>
    internal static bool TryNormalize(string key, out string canonical)
    {
        if (AttributeMapping.TryGetRename(key, out var renamed))
        {
            canonical = renamed;
            return true;
        }

        // The four rows the pinned registries do not carry as renames. The GenAI registry is a
        // development snapshot and declares no rename for gen_ai.system or the prompt/completion
        // token counts, and agents.tool.call_id was never an OpenTelemetry key at all. They stay
        // here until the registry declares them, because instrumentation still emits them and the
        // alternative is dropping the values at ingest. Both spellings of db.query.text and
        // url.full remain denied, so normalizing here also applies that denial to pre-1.21 and
        // pre-1.26 producers, which would otherwise write credentials straight through.
        var mapped = key switch
        {
            "gen_ai.system" => CollectorSemanticAttributeCatalog.GenAiProviderName,
            "gen_ai.usage.prompt_tokens" => CollectorSemanticAttributeCatalog.GenAiInputTokens,
            "gen_ai.usage.completion_tokens" => CollectorSemanticAttributeCatalog.GenAiOutputTokens,
            "agents.tool.call_id" => CollectorSemanticAttributeCatalog.GenAiToolCallId,
            _ => null
        };

        canonical = mapped ?? key;
        return mapped is not null;
    }
}
