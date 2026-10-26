namespace Nami.IntegrationTests.Receiver;

/// <summary>Metric data kinds that select the fixture built by <see cref="MetricFixture.Of" />.</summary>
internal static class MetricShapes
{
    public const string Gauge = "gauge";
    public const string Sum = "sum";
    public const string Histogram = "histogram";
    public const string Exponential = "exponential";
    public const string Summary = "summary";
}
