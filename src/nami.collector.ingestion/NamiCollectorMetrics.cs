using System.Diagnostics.Metrics;

namespace Nami.Collector.Ingestion;

internal static class NamiCollectorMetrics
{
    private static readonly Meter s_meter = new(CollectorSemanticAttributeCatalog.InstrumentationScope);

    private static readonly Counter<long> s_dropped =
        s_meter.CreateCounter<long>(CollectorSemanticAttributeCatalog.AttributesDroppedMetric, "{attribute}");

    private static readonly Counter<long> s_coalesced =
        s_meter.CreateCounter<long>(CollectorSemanticAttributeCatalog.AttributesCoalescedMetric, "{attribute}");

    private static readonly Counter<long> s_failed =
        s_meter.CreateCounter<long>(CollectorSemanticAttributeCatalog.SanitizationFailedMetric, "{attribute}");

    private static readonly Counter<long> s_replaced =
        s_meter.CreateCounter<long>(CollectorSemanticAttributeCatalog.ValuesReplacedMetric, "{value}");

    private static readonly Counter<long> s_missingService =
        s_meter.CreateCounter<long>(CollectorSemanticAttributeCatalog.MissingServiceNameMetric, "{resource}");

    // An attribute a size bound removed. No tag: the key is the sender's text.
    internal static void AttributeDropped() => s_dropped.Add(1);

    internal static void AttributeCoalesced(string key) => s_coalesced.Add(1,
        new KeyValuePair<string, object?>(CollectorSemanticAttributeCatalog.NamiAttributeNamespace, Namespace(key)));

    internal static void AttributeSanitizationFailed(string key, string category) => s_failed.Add(1,
        new KeyValuePair<string, object?>(CollectorSemanticAttributeCatalog.NamiAttributeKey, key),
        new KeyValuePair<string, object?>(ErrorAttributes.Type, category));

    // A value ingest replaced without dropping its attribute: a lone surrogate (U+FFFD in its place) or a structure
    // nested deeper than the bound (an empty value in its place). Counted in nami.collector.values.replaced, by reason, until a record can
    // say so itself (ledger N45). No key: it is the sender's text.
    internal static void ValueReplaced(int count, string reason) => s_replaced.Add(count,
        new KeyValuePair<string, object?>(CollectorSemanticAttributeCatalog.NamiValueReplacementReason, reason));

    internal static void MissingServiceName() => s_missingService.Add(1);
    private static string Namespace(string key) => key.IndexOf('.', StringComparison.Ordinal) is var i && i >= 0 ? key[..i] : key;
}
