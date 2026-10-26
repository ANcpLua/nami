using Nami.Tests.Wire.OpenTelemetry.Proto.Metrics.V1;
using static Nami.Tests.Evidence.OtlpEvidence;

namespace Nami.IntegrationTests.Receiver;

/// <summary>Builds the reference metric of each <see cref="MetricShapes" /> data kind.</summary>
internal static class MetricFixture
{
    /// <summary>
    ///     A metric of <paramref name="shape" /> whose identity, metadata and single point carry a distinct value in
    ///     every field.
    /// </summary>
    internal static Metric Of(string shape)
    {
        var metric = new Metric
        {
            Name = "metric." + shape, Description = "Exact distribution", Unit = "ms",
            Metadata = { Pair("metadata", "original") }
        };
        var exemplar = new Exemplar
        {
            AsDouble = 2.5, TimeUnixNano = 103, TraceId = Id(TraceId),
            SpanId = Id(SpanId),
            FilteredAttributes = { Pair("exemplar", true) }
        };
        var point = new NumberDataPoint
        {
            AsInt = -9, StartTimeUnixNano = 101, TimeUnixNano = 109, Flags = 0x80000001,
            Attributes = { Pair("point", "exact") }, Exemplars = { exemplar }
        };
        switch (shape)
        {
            case MetricShapes.Gauge: metric.Gauge = new Gauge { DataPoints = { point } }; break;
            case MetricShapes.Sum:
                metric.Sum = new Sum
                {
                    AggregationTemporality = AggregationTemporality.Delta, IsMonotonic = false, DataPoints = { point }
                }; break;
            case MetricShapes.Histogram:
                metric.Histogram = new Histogram
                {
                    AggregationTemporality = AggregationTemporality.Cumulative,
                    DataPoints =
                    {
                        new HistogramDataPoint
                        {
                            Count = 3, Sum = 6.5, Min = 1, Max = 4, StartTimeUnixNano = 101, TimeUnixNano = 109,
                            Flags = 3, BucketCounts = { 1, 2 }, ExplicitBounds = { 2 }, Exemplars = { exemplar }
                        }
                    }
                }; break;
            case MetricShapes.Exponential:
                metric.ExponentialHistogram = new ExponentialHistogram
                {
                    AggregationTemporality = AggregationTemporality.Delta,
                    DataPoints =
                    {
                        new ExponentialHistogramDataPoint
                        {
                            Count = 3, Sum = 2, Min = -2, Max = 4, Scale = -1, ZeroCount = 1, ZeroThreshold = 0.5,
                            StartTimeUnixNano = 101, TimeUnixNano = 109, Flags = 5,
                            Positive = new ExponentialHistogramDataPoint.Types.Buckets
                                { Offset = -2, BucketCounts = { 1 } },
                            Negative = new ExponentialHistogramDataPoint.Types.Buckets
                                { Offset = 3, BucketCounts = { 1 } },
                            Exemplars = { exemplar }
                        }
                    }
                }; break;
            case MetricShapes.Summary:
                metric.Summary = new Summary
                {
                    DataPoints =
                    {
                        new SummaryDataPoint
                        {
                            Count = 3, Sum = 6.5, StartTimeUnixNano = 101, TimeUnixNano = 109, Flags = 7,
                            QuantileValues =
                                { new SummaryDataPoint.Types.ValueAtQuantile { Quantile = 0.5, Value = 2.5 } }
                        }
                    }
                }; break;
            default: throw new ArgumentOutOfRangeException(nameof(shape));
        }

        return metric;
    }
}
