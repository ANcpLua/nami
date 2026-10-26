using Nami.Collector.Ingestion.Models;
using Nami.Collector.Ingestion.Primitives;

namespace Nami.Collector.Ingestion;

internal sealed partial record LogIngestionRecord
{
    public ulong? TimeUnixNano => TimeOrigin.ProducerEventTime;

    public ulong? ObservedTimeUnixNano => TimeOrigin.ProducerObservedTime ?? TimeOrigin.CollectorReceivedTime;

    public int? UntimedRequestOrdinal
    {
        get => TimeOrigin.UntimedRequestOrdinal;
        init => TimeOrigin = TimeOrigin with { UntimedRequestOrdinal = value };
    }

    public ulong? ReceivedTimeUnixNano => TimeOrigin.CollectorReceivedTime;
}

internal sealed partial record MetricPointIngestionRecord
{
    public MetricKind Kind => Shape switch
    {
        GaugeMetric => MetricKind.Gauge, SumMetric => MetricKind.Sum,
        HistogramMetric => MetricKind.Histogram, ExponentialHistogramMetric => MetricKind.ExponentialHistogram,
        SummaryMetric => MetricKind.Summary, null => throw new InvalidOperationException("A metric shape is required.")
    };

    public MetricTemporality Temporality => Shape switch
    {
        SumMetric value => value.Temporality, HistogramMetric value => value.Temporality,
        ExponentialHistogramMetric value => value.Temporality,
        GaugeMetric or SummaryMetric => MetricTemporality.Unspecified,
        null => throw new InvalidOperationException("A metric shape is required.")
    };

    public bool IsMonotonic => Shape is SumMetric { IsMonotonic: true };

    public MetricPointData Data => Shape switch
    {
        GaugeMetric value => value.Data, SumMetric value => value.Data, HistogramMetric value => value.Data,
        ExponentialHistogramMetric value => value.Data, SummaryMetric value => value.Data,
        null => throw new InvalidOperationException("A metric shape is required.")
    };
}
