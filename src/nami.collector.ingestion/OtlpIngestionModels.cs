using Nami.Collector.Ingestion.Models;
using Nami.Collector.Ingestion.Primitives;

namespace Nami.Collector.Ingestion;

internal sealed partial record LogIngestionRecord
{
    public ulong? TimeUnixNano => TimeOrigin.ProducerEventTime;

    public ulong? ObservedTimeUnixNano => TimeOrigin.ProducerObservedTime ?? TimeOrigin.CollectorReceivedTime;

    /// <summary>
    ///     The record's place in its export request, counted over every log record of the request. OTLP gives a log
    ///     record no identifier, so the stored row is keyed by the record's content and this place: two records of one
    ///     request that are equal in every field are two rows, as they were sent, and the same request sent again
    ///     finds the rows of the first time. A sender that sends a record again at another place stores it again.
    /// </summary>
    public int? RequestOrdinal { get; init; }

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
