using System.Diagnostics.Metrics;
using Nami.Telemetry.SemanticConventions.Incubating.Mapping;
using Nami.Telemetry.SemanticConventions.Incubating.Metrics;
using Nami.Telemetry.SemanticConventions.Names;

namespace Nami.Collector.Ingestion;

/// <summary>
/// The collector's own instruments. One Meter, named by the same scope constant as the
/// ActivitySource, and every instrument built from its registry definition rather than from a
/// name typed here — the name, the unit and the attribute the definition declares are the ones
/// the collector's own ingest catalog recognises.
/// </summary>
internal static class NamiCollectorMetrics
{
    private static readonly Meter s_meter = new(NamiTelemetryNames.Scopes.NamiCollector, BuildVersion.InformationalVersion);

    private static readonly Counter<long> s_attributesDropped = s_meter.CreateCounter<long>(
        NamiIncubatingMetricDefinitions.NamiCollectorAttributesDropped.Name,
        NamiIncubatingMetricDefinitions.NamiCollectorAttributesDropped.Unit,
        NamiIncubatingMetricDefinitions.NamiCollectorAttributesDropped.Brief);

    private static readonly Counter<long> s_attributesCoalesced = s_meter.CreateCounter<long>(
        NamiIncubatingMetricDefinitions.NamiCollectorAttributesCoalesced.Name,
        NamiIncubatingMetricDefinitions.NamiCollectorAttributesCoalesced.Unit,
        NamiIncubatingMetricDefinitions.NamiCollectorAttributesCoalesced.Brief);

    private static readonly Counter<long> s_sanitizationFailed = s_meter.CreateCounter<long>(
        NamiIncubatingMetricDefinitions.NamiCollectorAttributesSanitizationFailed.Name,
        NamiIncubatingMetricDefinitions.NamiCollectorAttributesSanitizationFailed.Unit,
        NamiIncubatingMetricDefinitions.NamiCollectorAttributesSanitizationFailed.Brief);

    /// <summary>
    /// Records an explicit persistence-policy denial by a bounded attribute namespace.
    /// </summary>
    internal static void AttributeDropped(string key) =>
        s_attributesDropped.Add(
            1,
            new KeyValuePair<string, object?>(
                CollectorSemanticAttributeCatalog.NamiAttributeNamespace,
                AttributeMapping.NamespaceOf(key)));

    internal static void AttributeCoalesced(string key) =>
        s_attributesCoalesced.Add(1, new KeyValuePair<string, object?>(
            CollectorSemanticAttributeCatalog.NamiAttributeNamespace, AttributeMapping.NamespaceOf(key)));

    internal static void AttributeSanitizationFailed(string canonicalKey, string category) =>
        s_sanitizationFailed.Add(1,
            new KeyValuePair<string, object?>(CollectorSemanticAttributeCatalog.NamiAttributeKey, canonicalKey),
            new KeyValuePair<string, object?>(CollectorSemanticAttributeCatalog.ErrorType, category));
}
