using System.Text.Json.Nodes;

namespace Nami.Collector.Analysis;

internal static class SemanticMetadata
{
    private static readonly JsonObject s_metadata = Load();
    private static readonly JsonObject s_attributes = s_metadata["attributes"]!.AsObject();
    public static JsonArray SpanRequirements => s_metadata["span_requirements"]!.AsArray();
    private static JsonObject Load()
    {
        using var stream = typeof(SemanticMetadata).Assembly.GetManifestResourceStream("Nami.Collector.Analysis.Generated.semconv-metadata.json")
            ?? throw new InvalidOperationException("Generated semantic metadata is missing.");
        return JsonNode.Parse(stream)!.AsObject();
    }
    public static JsonNode? Find(string key) => s_attributes[key]?.DeepClone();
}
