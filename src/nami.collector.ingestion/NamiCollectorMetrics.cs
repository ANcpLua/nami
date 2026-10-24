using System.Diagnostics.Metrics;
using Nami.SemanticConventions;

namespace Nami.Collector.Ingestion;

internal static class NamiCollectorMetrics
{
    private static readonly Meter s_meter = new(CollectorSemanticAttributeCatalog.InstrumentationScope);
    private static readonly Counter<long> s_dropped = s_meter.CreateCounter<long>(CollectorSemanticAttributeCatalog.AttributesDroppedMetric, "{attribute}");
    private static readonly Counter<long> s_coalesced = s_meter.CreateCounter<long>(CollectorSemanticAttributeCatalog.AttributesCoalescedMetric, "{attribute}");
    private static readonly Counter<long> s_failed = s_meter.CreateCounter<long>(CollectorSemanticAttributeCatalog.SanitizationFailedMetric, "{attribute}");
    private static readonly Counter<long> s_missingService = s_meter.CreateCounter<long>(CollectorSemanticAttributeCatalog.MissingServiceNameMetric, "{resource}");
    internal static void AttributeDropped(string key) => s_dropped.Add(1, new KeyValuePair<string, object?>(CollectorSemanticAttributeCatalog.NamiAttributeNamespace, Namespace(key)));
    internal static void AttributeCoalesced(string key) => s_coalesced.Add(1, new KeyValuePair<string, object?>(CollectorSemanticAttributeCatalog.NamiAttributeNamespace, Namespace(key)));
    internal static void AttributeSanitizationFailed(string key, string category) => s_failed.Add(1, new KeyValuePair<string, object?>(CollectorSemanticAttributeCatalog.NamiAttributeKey, key), new KeyValuePair<string, object?>(ErrorAttributes.Type, category));
    internal static void MissingServiceName() => s_missingService.Add(1);
    private static string Namespace(string key) => key.IndexOf('.') is var i && i >= 0 ? key[..i] : key;
}
