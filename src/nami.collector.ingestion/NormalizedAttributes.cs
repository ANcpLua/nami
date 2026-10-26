using System.Collections;
using Nami.Collector.Ingestion.Models;
using Attribute = Nami.Collector.Ingestion.Models.Attribute;

namespace Nami.Collector.Ingestion;

internal sealed partial record NormalizedAttributes : IReadOnlyDictionary<string, OtlpAttributeValue>
{
    public NormalizedAttributes()
    {
        Values = new Dictionary<string, OtlpAttributeValue>(StringComparer.Ordinal);
        Renames = [];
    }

    public OtlpAttributeValue this[string key] { get => Values[key]; set => Values[key] = value; }
    public IEnumerable<string> Keys => Values.Keys;
    IEnumerable<OtlpAttributeValue> IReadOnlyDictionary<string, OtlpAttributeValue>.Values => Values.Values;
    public int Count => Values.Count;
    public bool ContainsKey(string key) => Values.ContainsKey(key);

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out OtlpAttributeValue value) =>
        Values.TryGetValue(key, out value);

    public IEnumerator<KeyValuePair<string, OtlpAttributeValue>> GetEnumerator() => Values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static implicit operator NormalizedAttributes(Dictionary<string, OtlpAttributeValue> values) =>
        new() { Values = values };

    internal static void RecordRename(NormalizedAttributes attributes, string original, string canonical)
    {
        if (!string.Equals(original, canonical, StringComparison.Ordinal))
            attributes.Renames.Add(new AttributeRename { OriginalKey = original, CanonicalKey = canonical });
        else if (AttributeStability.Of(original) == Stability.Deprecated)
            attributes.Renames.Add(new AttributeRename { OriginalKey = original, CanonicalKey = null });
    }

    internal static IReadOnlyList<AttributeRename> Collect(IEnumerable<NormalizedAttributes> attributes) =>
        attributes.SelectMany(static values => values.Renames).Distinct()
            .OrderBy(static rename => rename.OriginalKey, StringComparer.Ordinal)
            .ThenBy(static rename => rename.CanonicalKey, StringComparer.Ordinal).ToArray();
}

internal sealed partial record NormalizedContractAttributes : IReadOnlyList<Attribute>
{
    public NormalizedContractAttributes(IReadOnlyList<AttributeRename>? renames = null)
    {
        Values = [];
        Renames = [.. renames ?? []];
    }

    public Attribute this[int index] => Values[index];
    public int Count => Values.Count;
    public IEnumerator<Attribute> GetEnumerator() => Values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public void Add(Attribute value) => Values.Add(value);
    public static implicit operator NormalizedContractAttributes(Attribute[] values) => new() { Values = [.. values] };
}

internal sealed partial record NormalizedMetricExemplars : IReadOnlyList<MetricExemplar>
{
    public NormalizedMetricExemplars()
    {
        Values = [];
        Renames = [];
    }

    public MetricExemplar this[int index] => Values[index];
    public int Count => Values.Count;
    public IEnumerator<MetricExemplar> GetEnumerator() => Values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public void Add(MetricExemplar value) => Values.Add(value);

    public static implicit operator NormalizedMetricExemplars(MetricExemplar[] values) =>
        new() { Values = [.. values] };
}
