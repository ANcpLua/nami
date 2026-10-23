namespace Nami.Collector.Ingestion.Primitives;

/// <summary>The original OTLP metric point shapes, retained independently of query projections.</summary>
internal enum MetricKind : byte
{
    Gauge = 1,
    Sum = 2,
    Histogram = 3,
    ExponentialHistogram = 4,
    Summary = 5
}

internal enum MetricTemporality : byte
{
    Unspecified = 0,
    Delta = 1,
    Cumulative = 2
}
