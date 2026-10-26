using JetBrains.Annotations;

namespace Nami.Tests;

[PublicAPI]
public sealed record SpanCall(SpanRecord Client, SpanRecord Server)
{
    public string? From => Client.Service;
    public string? To => Server.Service;
}

[PublicAPI]
public sealed record MetricPartition(string ResourceAttribute, string Value)
{
    /// <summary>The resource attribute that carries a capture's trace id as its metric partition.</summary>
    internal const string TestIdAttribute = "nami.test.id";
}

[PublicAPI]
public enum MetricCorrelation
{
    ResourcePartition,
    Exemplar
}

[PublicAPI]
public sealed record MetricEvidence(
    MetricRecord Metric,
    MetricCorrelation Correlation,
    IReadOnlyList<int> PointIndexes);

[PublicAPI]
public sealed class TraceEvidence
{
    public TraceEvidence(string traceId, TelemetrySnapshot capture, MetricPartition? partition = null)
    {
        ArgumentNullException.ThrowIfNull(traceId);
        ArgumentNullException.ThrowIfNull(capture);
        TraceId = traceId;
        Capture = capture;
        Spans = capture.Spans.Where(s => s.TraceId == traceId).AsReadOnlySnapshot();
        Logs = capture.Logs.Where(l => l.TraceId == traceId).AsReadOnlySnapshot();
        Services = Spans.Select(s => s.Service).OfType<string>().Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).AsReadOnlySnapshot();
        SessionIds = Spans.SelectMany(s => new[]
            {
                s.GetString("session.id"),
                s.Resource.Attributes.TryGetValue("session.id", out var session) ? session as string : null
            })
            .Concat(Logs.SelectMany(l => new[]
            {
                l.Attributes.TryGetValue("session.id", out var session) ? session as string : null,
                l.Resource.Attributes.TryGetValue("session.id", out var resourceSession)
                    ? resourceSession as string
                    : null
            }))
            .OfType<string>().Distinct(StringComparer.Ordinal).AsReadOnlySnapshot();
        var byId = Spans.DistinctBy(s => s.SpanId, StringComparer.Ordinal)
            .ToDictionary(s => s.SpanId, StringComparer.Ordinal);
        // One server span is one call; repeated exports remain in Spans and Capture.
        Calls = byId.Values.Where(s =>
                s is { Kind: SpanKind.Server, ParentSpanId: { } parent } && byId.TryGetValue(parent, out var client)
                                                                         && client.Kind == SpanKind.Client &&
                                                                         client.Service != s.Service)
            .Select(s => new SpanCall(byId[s.ParentSpanId!], s)).AsReadOnlySnapshot();
        Metrics = capture.Metrics.Select(m => Correlate(m, traceId, partition))
            .OfType<MetricEvidence>().AsReadOnlySnapshot();
    }

    public string TraceId { get; }
    public string? SessionId => SessionIds.Count == 1 ? SessionIds[0] : null;
    public IReadOnlyList<string> SessionIds { get; }
    public TelemetrySnapshot Capture { get; }
    public IReadOnlyList<SpanRecord> Spans { get; }
    public IReadOnlyList<LogRecord> Logs { get; }
    public IReadOnlyList<MetricEvidence> Metrics { get; }
    public IReadOnlyList<string> Services { get; }
    public IReadOnlyList<SpanCall> Calls { get; }

    public IReadOnlyList<StoreIdentity> StoresOf(string service)
    {
        return Spans
            .Where(s => s.Service == service && s.GetString("db.system.name") is not null)
            .Select(s => new StoreIdentity(s.GetString("db.system.name"), s.GetString("server.address"),
                s.GetInt64("server.port"), s.GetString("db.namespace")))
            .Distinct().AsReadOnlySnapshot();
    }

    private static MetricEvidence? Correlate(MetricRecord metric, string traceId, MetricPartition? partition)
    {
        var points = metric.Data.Points().Select(point => point.Exemplars).ToArray();
        if (partition is not null &&
            metric.Resource.Attributes.TryGetValue(partition.ResourceAttribute, out var value) &&
            value is string text && text == partition.Value)
        {
            return points.Length == 0
                ? null
                : new MetricEvidence(metric, MetricCorrelation.ResourcePartition,
                    Enumerable.Range(0, points.Length).AsReadOnlySnapshot());
        }

        var matching = points.Index().Where(p => p.Item.Any(e => e.TraceId == traceId)).Select(p => p.Index).ToArray();
        return matching.Length == 0
            ? null
            : new MetricEvidence(metric, MetricCorrelation.Exemplar, matching.AsReadOnlySnapshot());
    }
}
