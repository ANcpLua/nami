using System.Text.Json;
using Google.Protobuf;
using JetBrains.Annotations;
using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Metrics.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Metrics.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Resource.V1;
using static Nami.Tests.Evidence.OtlpEvidence;

namespace Nami.IntegrationTests.Receiver;

[TestClass]
public sealed class MetricWireTests
{
    [UsedImplicitly] public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(MetricShapes.Gauge)]
    [DataRow(MetricShapes.Sum)]
    [DataRow(MetricShapes.Histogram)]
    [DataRow(MetricShapes.Exponential)]
    [DataRow(MetricShapes.Summary)]
    public async Task OtlpReceiverMetrics_EveryShape_KeepsIdentityMetadataAndArchivedDistribution(string shape)
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        var metric = MetricFixture.Of(shape);
        var request = new ExportMetricsServiceRequest
        {
            ResourceMetrics =
            {
                new ResourceMetrics
                {
                    SchemaUrl = "resource-schema",
                    Resource = new Resource
                        { Attributes = { Pair("service.name", "metric-service") } },
                    ScopeMetrics =
                    {
                        new ScopeMetrics
                        {
                            SchemaUrl = "scope-schema",
                            Scope = new InstrumentationScope { Name = "metric-scope", Version = "4.2" },
                            Metrics = { metric }
                        }
                    }
                }
            }
        };
        using var response = await OtlpHttp.PostAsync(receiver.Endpoint, request.ToByteArray(),
            TestContext.CancellationToken, Signals.Metrics);
        response.EnsureSuccessStatusCode();
        var observed = Assert.ContainsSingle(receiver.Snapshot().Metrics);
        Assert.AreEqual("metric." + shape, observed.Name);
        Assert.AreEqual("Exact distribution", observed.Description);
        Assert.AreEqual("ms", observed.Unit);
        Assert.AreEqual("metric-service", observed.Resource.Attributes["service.name"]);
        Assert.AreEqual("resource-schema", observed.Resource.SchemaUrl);
        Assert.AreEqual("metric-scope", observed.Scope?.Name);
        Assert.AreEqual("4.2", observed.Scope?.Version);
        Assert.AreEqual("scope-schema", observed.Scope?.SchemaUrl);
        Assert.AreEqual("original", observed.Metadata["metadata"]);
        using var archive =
            JsonSerializer.SerializeToDocument(receiver.Snapshot(), TelemetryJsonContext.Default.TelemetrySnapshot);
        var archivedDistribution = archive.RootElement.GetProperty("Metrics")[0].GetProperty("Data");
        Assert.AreEqual(shape, archivedDistribution.GetProperty("kind").GetString());
        Assert.AreEqual(1, archivedDistribution.GetProperty("Points").GetArrayLength());
        var payload = archive.RootElement.GetProperty("Requests")[0];
        Assert.AreEqual(Signals.Metrics, payload.GetProperty("Signal").GetString());
        Assert.AreSequenceEqual(request.ToByteArray(), payload.GetProperty("Payload").GetBytesFromBase64());
        var partitioned = new TraceEvidence(UnrelatedTraceId, receiver.Snapshot(),
            new MetricPartition("service.name", "metric-service"));
        var attributed = Assert.ContainsSingle(partitioned.Metrics);
        Assert.AreEqual(MetricCorrelation.ResourcePartition, attributed.Correlation);
        Assert.AreSequenceEqual([0], attributed.PointIndexes);
        Assert.AreEqual("metric." + shape, attributed.Metric.Name);

        Assert.AreEqual(1L, Assert.ContainsSingle(receiver.Snapshot().Receipts).Accepted);
    }

    [TestMethod]
    public async Task MetricAttribution_ResourcePartitionOrExemplarPoint_MustMatch()
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        var metric = MetricFixture.Of(MetricShapes.Gauge);
        metric.Gauge.DataPoints.Add(new NumberDataPoint { AsInt = 99 });
        var request = Metrics(null, metric);
        using var response = await OtlpHttp.PostAsync(receiver.Endpoint, request.ToByteArray(),
            TestContext.CancellationToken, Signals.Metrics);
        response.EnsureSuccessStatusCode();
        var snapshot = receiver.Snapshot();
        var unrelated = new TraceEvidence(UnrelatedTraceId, snapshot);
        Assert.IsEmpty(unrelated.Metrics);
        Assert.HasCount(1, unrelated.Capture.Metrics);
        var related = new TraceEvidence(TraceId, snapshot);
        var evidence = Assert.ContainsSingle(related.Metrics);
        Assert.AreEqual("metric.gauge", evidence.Metric.Name);
        Assert.AreEqual(MetricCorrelation.Exemplar, evidence.Correlation);
        Assert.AreSequenceEqual([0], evidence.PointIndexes);
        Assert.HasCount(2, Assert.IsInstanceOfType<GaugeData>(evidence.Metric.Data).Points);
        var mismatch = new TraceEvidence(UnrelatedTraceId, snapshot,
            new MetricPartition(MetricPartition.TestIdAttribute, "wrong"));
        Assert.IsEmpty(mismatch.Metrics);
    }

    [TestMethod]
    public async Task OtlpReceiverMetrics_Gauge_KeepsTheNumberPointAndExemplar()
    {
        var data = Assert.IsInstanceOfType<GaugeData>(await ReadDataAsync(MetricShapes.Gauge));
        var point = Assert.ContainsSingle(data.Points);
        Assert.AreEqual(-9L, point.Value);
        Assert.AreEqual(101UL, point.StartTimeUnixNano);
        Assert.AreEqual(109UL, point.TimeUnixNano);
        Assert.AreEqual(0x80000001u, point.Flags);
        Assert.AreEqual("exact", point.Attributes["point"]);
        var exemplar = Assert.ContainsSingle(point.Exemplars);
        Assert.AreEqual(TraceId, exemplar.TraceId);
        Assert.AreEqual(SpanId, exemplar.SpanId);
        Assert.AreEqual(103UL, exemplar.TimeUnixNano);
        Assert.AreEqual(2.5, exemplar.Value);
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(exemplar.FilteredAttributes["exemplar"]));
    }

    [TestMethod]
    public async Task OtlpReceiverMetrics_Sum_KeepsTemporalityMonotonicityAndTheNumberPoint()
    {
        var data = Assert.IsInstanceOfType<SumData>(await ReadDataAsync(MetricShapes.Sum));
        Assert.AreEqual(1, data.AggregationTemporality);
        Assert.IsFalse(data.IsMonotonic);
        var point = Assert.ContainsSingle(data.Points);
        Assert.AreEqual(-9L, point.Value);
        Assert.AreEqual(101UL, point.StartTimeUnixNano);
        Assert.AreEqual(109UL, point.TimeUnixNano);
        Assert.AreEqual(0x80000001u, point.Flags);
        Assert.AreEqual("exact", point.Attributes["point"]);
        var exemplar = Assert.ContainsSingle(point.Exemplars);
        Assert.AreEqual(TraceId, exemplar.TraceId);
        Assert.AreEqual(SpanId, exemplar.SpanId);
        Assert.AreEqual(103UL, exemplar.TimeUnixNano);
        Assert.AreEqual(2.5, exemplar.Value);
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(exemplar.FilteredAttributes["exemplar"]));
    }

    [TestMethod]
    public async Task OtlpReceiverMetrics_Histogram_KeepsBucketsBoundsAndTheExemplar()
    {
        var histogram = Assert.IsInstanceOfType<HistogramData>(await ReadDataAsync(MetricShapes.Histogram));
        Assert.AreEqual(2, histogram.AggregationTemporality);
        var point = Assert.ContainsSingle(histogram.Points);
        Assert.AreEqual(3UL, point.Count);
        Assert.AreEqual(6.5, point.Sum);
        Assert.AreEqual(1d, point.Min);
        Assert.AreEqual(4d, point.Max);
        Assert.AreSequenceEqual(new ulong[] { 1, 2 }, point.BucketCounts);
        Assert.AreSequenceEqual([2d], point.ExplicitBounds);
        Assert.AreEqual(101UL, point.StartTimeUnixNano);
        Assert.AreEqual(109UL, point.TimeUnixNano);
        Assert.AreEqual(3u, point.Flags);
        var exemplar = Assert.ContainsSingle(point.Exemplars);
        Assert.AreEqual(TraceId, exemplar.TraceId);
        Assert.AreEqual(SpanId, exemplar.SpanId);
        Assert.AreEqual(103UL, exemplar.TimeUnixNano);
        Assert.AreEqual(2.5, exemplar.Value);
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(exemplar.FilteredAttributes["exemplar"]));
    }

    [TestMethod]
    public async Task OtlpReceiverMetrics_ExponentialHistogram_KeepsScaleBucketsAndTheExemplar()
    {
        var histogram =
            Assert.IsInstanceOfType<ExponentialHistogramData>(await ReadDataAsync(MetricShapes.Exponential));
        Assert.AreEqual(1, histogram.AggregationTemporality);
        var point = Assert.ContainsSingle(histogram.Points);
        Assert.AreEqual(3UL, point.Count);
        Assert.AreEqual(2d, point.Sum);
        Assert.AreEqual(-2d, point.Min);
        Assert.AreEqual(4d, point.Max);
        Assert.AreEqual(-1, point.Scale);
        Assert.AreEqual(1UL, point.ZeroCount);
        Assert.AreEqual(0.5, point.ZeroThreshold);
        Assert.IsNotNull(point.Positive);
        Assert.IsNotNull(point.Negative);
        Assert.AreEqual(-2, point.Positive.Offset);
        Assert.AreEqual(3, point.Negative.Offset);
        Assert.AreSequenceEqual(new ulong[] { 1 }, point.Positive.BucketCounts);
        Assert.AreSequenceEqual(new ulong[] { 1 }, point.Negative.BucketCounts);
        Assert.AreEqual(101UL, point.StartTimeUnixNano);
        Assert.AreEqual(109UL, point.TimeUnixNano);
        Assert.AreEqual(5u, point.Flags);
        var exemplar = Assert.ContainsSingle(point.Exemplars);
        Assert.AreEqual(TraceId, exemplar.TraceId);
        Assert.AreEqual(SpanId, exemplar.SpanId);
        Assert.AreEqual(103UL, exemplar.TimeUnixNano);
        Assert.AreEqual(2.5, exemplar.Value);
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(exemplar.FilteredAttributes["exemplar"]));
    }

    [TestMethod]
    public async Task OtlpReceiverMetrics_Summary_KeepsCountSumAndQuantiles()
    {
        var summary = Assert.IsInstanceOfType<SummaryData>(await ReadDataAsync(MetricShapes.Summary));
        var point = Assert.ContainsSingle(summary.Points);
        Assert.AreEqual(3UL, point.Count);
        Assert.AreEqual(6.5, point.Sum);
        Assert.AreEqual(101UL, point.StartTimeUnixNano);
        Assert.AreEqual(109UL, point.TimeUnixNano);
        Assert.AreEqual(7u, point.Flags);
        Assert.AreEqual(new QuantileValue(0.5, 2.5), Assert.ContainsSingle(point.Quantiles));
    }

    private async Task<MetricData> ReadDataAsync(string shape)
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        var request = Metrics(null, MetricFixture.Of(shape));
        using var response = await OtlpHttp.PostAsync(receiver.Endpoint, request.ToByteArray(),
            TestContext.CancellationToken, Signals.Metrics);
        response.EnsureSuccessStatusCode();
        return receiver.Snapshot().Metrics.Single().Data;
    }
}
