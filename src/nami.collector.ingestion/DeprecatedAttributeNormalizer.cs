using Nami.SemanticConventions;

namespace Nami.Collector.Ingestion;

internal static class DeprecatedAttributeNormalizer
{
    internal static bool TryNormalize(string key, out string canonical)
    {
        var definition = AttributeCatalog.Find(key);
        var successor = DeprecatedAttributes.SuccessorOf(key);
        canonical = successor is null ? key : definition is not null && definition.Type.StartsWith("template[", StringComparison.Ordinal) && key.Length > definition.Key.Length
            ? successor + key[definition.Key.Length..]
            : successor;
        return !string.Equals(canonical, key, StringComparison.Ordinal);
    }
}
