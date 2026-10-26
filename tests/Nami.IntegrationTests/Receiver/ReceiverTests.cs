using System.Net;
using Google.Protobuf;
using Grpc.Core;
using JetBrains.Annotations;
using Nami.Tests.Evidence;
using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Metrics.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Trace.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Trace.V1;

namespace Nami.IntegrationTests.Receiver;

[TestClass]
public sealed class ReceiverTests
{
    [UsedImplicitly] public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(Signals.Traces)]
    [DataRow(Signals.Logs)]
    [DataRow(Signals.Metrics)]
    public async Task HttpReceiver_MalformedProtobuf_ReturnsBadRequestAndRecordsTheError(string signal)
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        using var response = await OtlpHttp.PostAsync(receiver.Endpoint, [0x80], TestContext.CancellationToken, signal);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var receipt = Assert.ContainsSingle(receiver.Snapshot().Receipts);
        Assert.AreEqual(signal, receipt.Signal);
        Assert.AreEqual(0L, receipt.Accepted);
        Assert.IsFalse(string.IsNullOrEmpty(receipt.Error));
        var original = Assert.ContainsSingle(receiver.Snapshot().Requests);
        Assert.AreEqual(signal, original.Signal);
        Assert.AreSequenceEqual(new byte[] { 0x80 }, original.Payload.ToArray());
        var error = await Assert.That.HasOtlpErrorAsync(response, TestContext.CancellationToken);
        Assert.AreEqual((int)StatusCode.InvalidArgument, error.Code);
        Assert.AreEqual(receipt.Error, error.Message);
    }

    [TestMethod]
    public async Task HttpReceiver_PartiallyInvalidTraces_KeepsValidSpansAndReportsTheExactRejection()
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        var request = OtlpEvidence.Traces(null,
            new Span
            {
                TraceId = OtlpEvidence.Id(OtlpEvidence.TraceId),
                SpanId = OtlpEvidence.Id(OtlpEvidence.SpanId), Name = "valid"
            },
            new Span { Name = "invalid" });
        using var response =
            await OtlpHttp.PostAsync(receiver.Endpoint, request.ToByteArray(), TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var result =
            ExportTraceServiceResponse.Parser.ParseFrom(
                await response.Content.ReadAsByteArrayAsync(TestContext.CancellationToken));
        Assert.AreEqual(1L, result.PartialSuccess.RejectedSpans);
        Assert.Contains("16", result.PartialSuccess.ErrorMessage);
        var span = Assert.ContainsSingle(receiver.Snapshot().Spans);
        Assert.AreEqual(OtlpEvidence.TraceId, span.TraceId);
        Assert.AreEqual(OtlpEvidence.SpanId, span.SpanId);
        Assert.AreEqual("valid", span.Name);
        var receipt = Assert.ContainsSingle(receiver.Snapshot().Receipts);
        Assert.AreEqual(1L, receipt.Accepted);
        Assert.AreEqual(1L, receipt.Rejected);
    }

    [TestMethod]
    public async Task OtlpReceiverWait_EvidenceMissing_TimesOutInsteadOfPassingOnEmptyCapture()
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
            receiver.AwaitAsync(s => s.Spans.Count > 0, TimeSpan.Zero, TestContext.CancellationToken));
        Assert.IsEmpty(receiver.Snapshot().Spans);
    }

    [TestMethod]
    public async Task OtlpReceiverWait_Cancelled_InterruptsThePendingCondition()
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        var pending = receiver.AwaitAsync(s => s.Spans.Count > 0, OtlpReceiver.DefaultEvidenceTimeout,
            cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => pending);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OtlpReceiverWait_Precancelled_DoesNotEvaluateTheCondition(bool ready)
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var evaluated = false;
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            receiver.AwaitAsync(_ =>
            {
                evaluated = true;
                return ready;
            }, OtlpReceiver.DefaultEvidenceTimeout, cancellation.Token));
        Assert.AreEqual(cancellation.Token, failure.CancellationToken);
        Assert.IsFalse(evaluated);
    }

    [TestMethod]
    public async Task OtlpReceiver_DisposedRepeatedly_WakesAPendingCondition()
    {
        var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        var pending = receiver.AwaitAsync(s => s.Spans.Count > 0, OtlpReceiver.DefaultEvidenceTimeout,
            TestContext.CancellationToken);
        await receiver.DisposeAsync();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => pending);
        await receiver.DisposeAsync();
    }

    [TestMethod]
    public async Task HttpReceiver_InvalidMetricBatch_ReportsRejectedDataPointsNotMetricCount()
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        var metric = MetricFixture.Of(MetricShapes.Gauge);
        metric.Gauge.DataPoints.Add(metric.Gauge.DataPoints[0].Clone());
        var conflicting = metric.Gauge.DataPoints[0].Attributes[0].Clone();
        conflicting.Value = new AnyValue { StringValue = "conflicting" };
        metric.Gauge.DataPoints[0].Attributes.Add(conflicting);
        var request = OtlpEvidence.Metrics(null, metric);
        using var response = await OtlpHttp.PostAsync(receiver.Endpoint, request.ToByteArray(),
            TestContext.CancellationToken,
            Signals.Metrics);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var result = ExportMetricsServiceResponse.Parser.ParseFrom(
            await response.Content.ReadAsByteArrayAsync(TestContext.CancellationToken));
        Assert.AreEqual(2L, result.PartialSuccess.RejectedDataPoints);
        Assert.Contains("Duplicate OTLP attribute: point.", result.PartialSuccess.ErrorMessage);
        Assert.IsEmpty(receiver.Snapshot().Metrics);
        var receipt = Assert.ContainsSingle(receiver.Snapshot().Receipts);
        Assert.AreEqual(0L, receipt.Accepted);
        Assert.AreEqual(2L, receipt.Rejected);
    }

    [TestMethod]
    public async Task HttpReceiver_GzipRequest_IsDecompressedBeforeProtobufParsing()
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        using var response = await OtlpHttp.PostAsync(receiver.Endpoint,
            OtlpHttp.Compress(OtlpEvidence.RichSpan().ToByteArray()),
            TestContext.CancellationToken, Signals.Traces, "gzip");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var span = Assert.ContainsSingle(receiver.Snapshot().Spans);
        Assert.AreEqual("rich.operation", span.Name);
        Assert.AreEqual(OtlpEvidence.TraceId, span.TraceId);
        Assert.AreEqual(SpanKind.Consumer, span.Kind);
        Assert.AreEqual(ActivityStatusCode.Error, span.Status);
        Assert.AreEqual("rich-scope", span.Scope?.Name);
        Assert.AreSequenceEqual(OtlpEvidence.RichSpan().ToByteArray(),
            Assert.ContainsSingle(receiver.Snapshot().Requests).Payload.ToArray());
    }

    [TestMethod]
    public async Task HttpReceiver_UnknownField127Export_IsKeptAsOriginalButAbsentFromEveryRecord()
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        var empty = receiver.Snapshot();
        byte[] unknownField127Varint1 = [0xf8, 0x07, 0x01];
        using var response =
            await OtlpHttp.PostAsync(receiver.Endpoint, unknownField127Varint1, TestContext.CancellationToken);
        response.EnsureSuccessStatusCode();
        var snapshot = receiver.Snapshot();
        Assert.IsEmpty(empty.Requests);
        Assert.IsEmpty(snapshot.Spans);
        Assert.AreEqual(0L, Assert.ContainsSingle(snapshot.Receipts).Accepted);
        var request = Assert.ContainsSingle(snapshot.Requests);
        Assert.AreEqual(Signals.Traces, request.Signal);
        Assert.AreSequenceEqual(unknownField127Varint1, request.Payload.ToArray());
        using var second = await OtlpHttp.PostAsync(receiver.Endpoint, [], TestContext.CancellationToken);
        second.EnsureSuccessStatusCode();
        Assert.HasCount(1, snapshot.Requests);
        Assert.HasCount(2, receiver.Snapshot().Requests);
    }
}
