namespace Nami.Collector.Storage;

/// <summary>Canonical attribute identity orders map keys ordinally; producer insertion order is not identity.</summary>
internal static class CanonicalAttributeOrder
{
    internal static IEnumerable<KeyValuePair<string, T>> Apply<T>(IReadOnlyDictionary<string, T> values) =>
        values.OrderBy(static item => item.Key, StringComparer.Ordinal);
}
