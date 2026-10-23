using Microsoft.Extensions.Diagnostics.Enrichment;

namespace Nami.Collector.Telemetry;

internal sealed class NamiRequestEnricher(IHttpContextAccessor httpContextAccessor) : ILogEnricher
{
    public void Enrich(IEnrichmentTagCollector collector)
    {
        if (httpContextAccessor.HttpContext is not { } context) return;

        if (context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText.Length: > 0 } routeEndpoint)
        {
            collector.Add(CollectorSemanticAttributeCatalog.HttpRoute, routeEndpoint.RoutePattern.RawText);
        }

        collector.Add(CollectorSemanticAttributeCatalog.HttpRequestMethod,
            HttpTelemetryNames.NormalizeMethod(context.Request.Method));
    }
}
