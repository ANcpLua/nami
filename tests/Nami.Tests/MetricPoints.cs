namespace Nami.Tests;

/// <summary>The one switch over the decoded metric kinds.</summary>
internal static class MetricPoints
{
    /// <summary>Each data point's attributes and exemplars; summary points carry no exemplars.</summary>
    internal static IEnumerable<(IReadOnlyDictionary<string, object?> Attributes, IReadOnlyList<ExemplarRecord>
            Exemplars)>
        Points(this MetricData data)
    {
        return data switch
        {
            GaugeData gauge => gauge.Points.Select(p => (p.Attributes, p.Exemplars)),
            SumData sum => sum.Points.Select(p => (p.Attributes, p.Exemplars)),
            HistogramData histogram => histogram.Points.Select(p => (p.Attributes, p.Exemplars)),
            ExponentialHistogramData exponential => exponential.Points.Select(p => (p.Attributes, p.Exemplars)),
            SummaryData summary => summary.Points.Select(p => (p.Attributes, (IReadOnlyList<ExemplarRecord>)[])),
            _ => []
        };
    }
}
