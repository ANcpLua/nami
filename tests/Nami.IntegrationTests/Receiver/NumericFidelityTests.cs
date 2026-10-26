using System.Text.Json;
using Google.Protobuf;
using JetBrains.Annotations;
using Nami.Tests.Evidence;
using Nami.Tests.Wire.OpenTelemetry.Proto.Metrics.V1;

namespace Nami.IntegrationTests.Receiver;

[TestClass]
public sealed class NumericFidelityTests
{
    [UsedImplicitly] public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(9007199254740993L)]
    [DataRow(long.MinValue)]
    [DataRow(long.MaxValue)]
    public async Task OtlpReceiverMetrics_SignedIntegerPointsAndExemplars_KeepAll64Bits(long expected)
    {
        var point = new NumberDataPoint { AsInt = expected, Exemplars = { new Exemplar { AsInt = expected } } };
        var (_, _, metrics, receipts) = await CaptureAsync(
            new Metric { Name = "integer.gauge", Gauge = new Gauge { DataPoints = { point } } },
            new Metric
            {
                Name = "integer.sum",
                Sum = new Sum
                    { DataPoints = { point.Clone() }, AggregationTemporality = AggregationTemporality.Cumulative }
            });
        var receipt = Assert.ContainsSingle(receipts);
        Assert.AreEqual(0L, receipt.Rejected);
        Assert.AreEqual(2L, receipt.Accepted);
        Assert.HasCount(2, metrics);
        Assert.AreEqual("integer.gauge", metrics[0].Name);
        Assert.AreEqual("integer.sum", metrics[1].Name);
        var gauge = Assert.ContainsSingle(Assert.IsInstanceOfType<GaugeData>(metrics[0].Data).Points);
        var sum = Assert.ContainsSingle(Assert.IsInstanceOfType<SumData>(metrics[1].Data).Points);
        Assert.AreEqual(expected, Assert.IsInstanceOfType<long>(gauge.Value));
        Assert.AreEqual(expected, Assert.IsInstanceOfType<long>(Assert.ContainsSingle(gauge.Exemplars).Value));
        Assert.AreEqual(expected, Assert.IsInstanceOfType<long>(sum.Value));
        Assert.AreEqual(expected, Assert.IsInstanceOfType<long>(Assert.ContainsSingle(sum.Exemplars).Value));
        using var gaugeArchive = JsonSerializer.SerializeToDocument(gauge, TelemetryJsonContext.Default.NumberPoint);
        Assert.AreEqual(expected, gaugeArchive.RootElement.GetProperty("Value").GetInt64());
        Assert.AreEqual(expected, gaugeArchive.RootElement.GetProperty("Exemplars")[0].GetProperty("Value").GetInt64());
        using var sumArchive = JsonSerializer.SerializeToDocument(sum, TelemetryJsonContext.Default.NumberPoint);
        Assert.AreEqual(expected, sumArchive.RootElement.GetProperty("Value").GetInt64());
        Assert.AreEqual(expected, sumArchive.RootElement.GetProperty("Exemplars")[0].GetProperty("Value").GetInt64());
    }

    [TestMethod]
    [DataRow("nan", double.NaN)]
    [DataRow("positive-infinity", double.PositiveInfinity)]
    [DataRow("negative-infinity", double.NegativeInfinity)]
    [DataRow("negative-zero", -0.0)]
    public async Task OtlpReceiverMetrics_NonFiniteAndSignedZeroDoubles_KeepTheirWireBits(string name, double expected)
    {
        var (_, _, metrics, receipts) = await CaptureAsync(new Metric
        {
            Name = name, Gauge = new Gauge
            {
                DataPoints =
                {
                    new NumberDataPoint { AsDouble = expected, Exemplars = { new Exemplar { AsDouble = expected } } }
                }
            }
        });
        var receipt = Assert.ContainsSingle(receipts);
        Assert.AreEqual(0L, receipt.Rejected);
        Assert.AreEqual(1L, receipt.Accepted);
        var metric = Assert.ContainsSingle(metrics);
        Assert.AreEqual(name, metric.Name);
        var point = Assert.ContainsSingle(Assert.IsInstanceOfType<GaugeData>(metric.Data).Points);
        var value = Assert.IsInstanceOfType<double>(point.Value);
        var exemplar = Assert.IsInstanceOfType<double>(Assert.ContainsSingle(point.Exemplars).Value);
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(value));
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(exemplar));
    }

    [TestMethod]
    public async Task OtlpReceiverMetrics_CountsAndBucketCounts_PreserveUInt64Maximum()
    {
        var (_, _, metrics, receipts) = await CaptureAsync(
            new Metric
            {
                Name = "histogram", Histogram = new Histogram
                {
                    DataPoints =
                        { new HistogramDataPoint { Count = ulong.MaxValue, BucketCounts = { ulong.MaxValue } } }
                }
            },
            new Metric
            {
                Name = "exponential-zero", ExponentialHistogram = new ExponentialHistogram
                {
                    DataPoints =
                        { new ExponentialHistogramDataPoint { Count = ulong.MaxValue, ZeroCount = ulong.MaxValue } }
                }
            },
            new Metric
            {
                Name = "exponential-positive", ExponentialHistogram = new ExponentialHistogram
                {
                    DataPoints =
                    {
                        new ExponentialHistogramDataPoint
                        {
                            Count = ulong.MaxValue,
                            Positive = new ExponentialHistogramDataPoint.Types.Buckets
                                { BucketCounts = { ulong.MaxValue } }
                        }
                    }
                }
            },
            new Metric
            {
                Name = "summary",
                Summary = new Summary { DataPoints = { new SummaryDataPoint { Count = ulong.MaxValue } } }
            });
        var receipt = Assert.ContainsSingle(receipts);
        Assert.AreEqual(0L, receipt.Rejected);
        Assert.AreEqual(4L, receipt.Accepted);
        Assert.AreSequenceEqual(["histogram", "exponential-zero", "exponential-positive", "summary"],
            metrics.Select(m => m.Name));
        var histogram = Assert.ContainsSingle(Assert.IsInstanceOfType<HistogramData>(metrics[0].Data).Points);
        Assert.AreEqual(ulong.MaxValue, histogram.Count);
        Assert.AreEqual(ulong.MaxValue, Assert.ContainsSingle(histogram.BucketCounts));
        var zero = Assert.ContainsSingle(Assert.IsInstanceOfType<ExponentialHistogramData>(metrics[1].Data).Points);
        Assert.AreEqual(ulong.MaxValue, zero.Count);
        Assert.AreEqual(ulong.MaxValue, zero.ZeroCount);
        var positive = Assert.ContainsSingle(Assert.IsInstanceOfType<ExponentialHistogramData>(metrics[2].Data).Points);
        Assert.AreEqual(ulong.MaxValue, positive.Count);
        Assert.IsNotNull(positive.Positive);
        Assert.AreEqual(ulong.MaxValue, Assert.ContainsSingle(positive.Positive.BucketCounts));
        Assert.AreEqual(ulong.MaxValue,
            Assert.ContainsSingle(Assert.IsInstanceOfType<SummaryData>(metrics[3].Data).Points).Count);
    }

    private async Task<TelemetrySnapshot> CaptureAsync(params Metric[] metrics)
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        var request = OtlpEvidence.Metrics(null, metrics);
        using var response = await OtlpHttp.PostAsync(receiver.Endpoint, request.ToByteArray(),
            TestContext.CancellationToken, Signals.Metrics);
        response.EnsureSuccessStatusCode();
        return receiver.Snapshot();
    }
}
