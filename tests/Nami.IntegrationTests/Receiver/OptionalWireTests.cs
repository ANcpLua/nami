using Google.Protobuf;
using JetBrains.Annotations;
using Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Logs.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Metrics.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Resource.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Trace.V1;
using static Nami.Tests.Evidence.OtlpEvidence;
using LogRecord = Nami.Tests.Wire.OpenTelemetry.Proto.Logs.V1.LogRecord;

namespace Nami.IntegrationTests.Receiver;

[TestClass]
public sealed class OptionalWireTests
{
    [UsedImplicitly] public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task OtlpReceiver_AbsentResourceAndScopeMetadata_StayAbsent()
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        var request = Traces(null,
            new Span { Name = "minimal", TraceId = Id(TraceId), SpanId = Id(SpanId) });
        using var response =
            await OtlpHttp.PostAsync(receiver.Endpoint, request.ToByteArray(), TestContext.CancellationToken);
        response.EnsureSuccessStatusCode();
        var span = Assert.ContainsSingle(receiver.Snapshot().Spans);
        Assert.AreEqual("minimal", span.Name);
        Assert.AreEqual(TraceId, span.TraceId);
        Assert.AreEqual(SpanKind.Unspecified, span.Kind);
        Assert.AreEqual(ActivityStatusCode.Unset, span.Status);
        Assert.IsNull(span.Scope);
        Assert.IsNull(span.Resource.SchemaUrl);
        Assert.IsEmpty(span.Resource.Attributes);
        Assert.IsEmpty(span.Resource.EntityReferences);
        Assert.IsNull(span.ParentSpanId);
        Assert.IsNull(span.StatusDescription);
        Assert.IsNull(span.TraceState);
    }

    [TestMethod]
    public async Task OtlpReceiver_ScopeSchemaWithoutScopeMessage_IsKept()
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        var request = RichSpan();
        request.ResourceSpans[0].ScopeSpans[0].Scope = null;
        using var response =
            await OtlpHttp.PostAsync(receiver.Endpoint, request.ToByteArray(), TestContext.CancellationToken);
        response.EnsureSuccessStatusCode();
        var span = Assert.ContainsSingle(receiver.Snapshot().Spans);
        Assert.AreEqual("rich.operation", span.Name);
        Assert.AreEqual(TraceId, span.TraceId);
        Assert.IsNotNull(span.Scope);
        Assert.AreEqual("scope-schema", span.Scope.SchemaUrl);
        Assert.IsNull(span.Scope.Name);
        Assert.IsNull(span.Scope.Version);
        Assert.IsEmpty(span.Scope.Attributes);
        Assert.AreEqual(0u, span.Scope.DroppedAttributesCount);
    }

    [TestMethod]
    public async Task OtlpReceiverMetrics_OptionalHistogramFieldsAbsent_StayNullInsteadOfZero()
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        var metric = MetricFixture.Of(MetricShapes.Histogram);
        var point = metric.Histogram.DataPoints[0];
        point.ClearSum();
        point.ClearMin();
        point.ClearMax();
        var request = Metrics(null, metric);
        using var response = await OtlpHttp.PostAsync(receiver.Endpoint, request.ToByteArray(),
            TestContext.CancellationToken, Signals.Metrics);
        response.EnsureSuccessStatusCode();
        var observed = Assert.ContainsSingle(receiver.Snapshot().Metrics);
        Assert.AreEqual("metric." + MetricShapes.Histogram, observed.Name);
        Assert.IsNull(observed.Resource.SchemaUrl);
        Assert.IsNull(observed.Scope);
        var captured = Assert.ContainsSingle(Assert.IsInstanceOfType<HistogramData>(observed.Data).Points);
        Assert.IsNull(captured.Sum);
        Assert.IsNull(captured.Min);
        Assert.IsNull(captured.Max);
    }

    [TestMethod]
    public async Task OtlpReceiverMetrics_OptionalExponentialHistogramFieldsAbsent_StayNullInsteadOfZero()
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        var metric = MetricFixture.Of(MetricShapes.Exponential);
        var point = metric.ExponentialHistogram.DataPoints[0];
        point.ClearSum();
        point.ClearMin();
        point.ClearMax();
        point.Positive = null;
        point.Negative = null;
        var request = Metrics(null, metric);
        using var response = await OtlpHttp.PostAsync(receiver.Endpoint, request.ToByteArray(),
            TestContext.CancellationToken, Signals.Metrics);
        response.EnsureSuccessStatusCode();
        var observed = Assert.ContainsSingle(receiver.Snapshot().Metrics);
        Assert.AreEqual("metric." + MetricShapes.Exponential, observed.Name);
        Assert.IsNull(observed.Resource.SchemaUrl);
        Assert.IsNull(observed.Scope);
        var captured = Assert.ContainsSingle(Assert.IsInstanceOfType<ExponentialHistogramData>(observed.Data).Points);
        Assert.IsNull(captured.Sum);
        Assert.IsNull(captured.Min);
        Assert.IsNull(captured.Max);
        Assert.IsNull(captured.Positive);
        Assert.IsNull(captured.Negative);
    }

    [TestMethod]
    public async Task OtlpReceiverLogs_UncorrelatedWithoutBodyOrContext_IsKeptInTheCapture()
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        var request = Logs(null, new InstrumentationScope { Name = "background" },
            new LogRecord { EventName = "BackgroundError", SeverityNumber = SeverityNumber.Error });
        using var response = await OtlpHttp.PostAsync(receiver.Endpoint, request.ToByteArray(),
            TestContext.CancellationToken, Signals.Logs);
        response.EnsureSuccessStatusCode();
        var snapshot = receiver.Snapshot();
        var log = Assert.ContainsSingle(snapshot.Logs);
        Assert.AreEqual("BackgroundError", log.EventName);
        Assert.AreEqual(17, log.SeverityNumber);
        Assert.AreEqual("background", log.Scope?.Name);
        Assert.IsNull(log.TraceId);
        Assert.IsNull(log.SpanId);
        Assert.IsNull(log.Body);
        Assert.IsNull(log.Resource.SchemaUrl);
        var trace = new TraceEvidence(TraceId, snapshot);
        Assert.IsEmpty(trace.Logs);
        Assert.HasCount(1, trace.Capture.Logs);
        Assert.IsNull(trace.SessionId);
    }

    [TestMethod]
    public async Task OtlpReceiverMetrics_UnspecifiedData_StaysVisibleButIsNeverAMeasurement()
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        var request = Metrics(new Resource { Attributes = { Pair(MetricPartition.TestIdAttribute, "partition") } },
            new Metric { Name = "unspecified" });
        using var response = await OtlpHttp.PostAsync(receiver.Endpoint, request.ToByteArray(),
            TestContext.CancellationToken, Signals.Metrics);
        response.EnsureSuccessStatusCode();
        var snapshot = receiver.Snapshot();
        var metric = Assert.ContainsSingle(snapshot.Metrics);
        Assert.AreEqual("unspecified", metric.Name);
        Assert.IsInstanceOfType<UnspecifiedMetricData>(metric.Data);
        Assert.IsEmpty(new TraceEvidence(TraceId, snapshot,
            new MetricPartition(MetricPartition.TestIdAttribute, "partition")).Metrics);
        Assert.AreEqual(0L, Assert.ContainsSingle(snapshot.Receipts).Accepted);
    }
}
