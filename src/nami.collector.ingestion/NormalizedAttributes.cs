namespace Nami.Collector.Ingestion;

/// <summary>Normalization provenance, separate from producer attributes and their values.</summary>
internal sealed record AttributeRename(string OriginalKey, string? CanonicalKey);

internal sealed class NormalizedAttributes() : Dictionary<string, OtlpAttributeValue>(StringComparer.Ordinal)
{
    private readonly HashSet<AttributeRename> _renames = [];

    internal static void RecordRename(IReadOnlyDictionary<string, OtlpAttributeValue> attributes,
        string original, string canonical)
    {
        if (attributes is not NormalizedAttributes target) return;
        if (!string.Equals(original, canonical, StringComparison.Ordinal))
            target._renames.Add(new AttributeRename(original, canonical));
        else if (AttributeStability.Of(original) == Stability.Deprecated)
            target._renames.Add(new AttributeRename(original, null));
    }

    internal static IReadOnlyList<AttributeRename> Collect(IEnumerable<IReadOnlyDictionary<string, OtlpAttributeValue>> attributes) =>
        attributes.OfType<NormalizedAttributes>().SelectMany(static values => values._renames)
            .Distinct().OrderBy(static rename => rename.OriginalKey, StringComparer.Ordinal)
            .ThenBy(static rename => rename.CanonicalKey, StringComparer.Ordinal).ToArray();
}

internal sealed class NormalizedContractAttributes(IReadOnlyList<AttributeRename> renames)
    : List<Nami.Api.Contracts.Common.Attribute>
{
    internal IReadOnlyList<AttributeRename> Renames { get; } = renames;
}

internal sealed class NormalizedMetricExemplars : List<Nami.Api.Contracts.OTel.Metrics.MetricExemplar>
{
    internal List<AttributeRename> Renames { get; } = [];
}
