using Google.Protobuf;
using JetBrains.Annotations;
using static Nami.Tests.Evidence.OtlpEvidence;

namespace Nami.IntegrationTests.Receiver;

[TestClass]
public sealed class CaptureLifecycleTests
{
    [UsedImplicitly] public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task TelemetryCapture_ConcurrentInvocations_HaveDistinctRootsAndKeepUnrelatedRecordsVisible()
    {
        var previous = Activity.Current;
        var results = await Task.WhenAll(CaptureAsync(1), CaptureAsync(2));
        var traces = results.Select(result => result.Trace).ToArray();
        var spans = traces.Select(trace => Assert.ContainsSingle(trace.Spans)).ToArray();
        Assert.AreNotEqual(traces[0].TraceId, traces[1].TraceId);
        Assert.AreSame(previous, Activity.Current);
        Assert.AreSequenceEqual(["nami.test", "nami.test"], results.Select(result => result.AmbientOperation));
        Assert.AreSequenceEqual(traces.Select(trace => trace.TraceId), spans.Select(span => span.TraceId));
        Assert.AreSequenceEqual(["capture.1", "capture.2"], spans.Select(span => span.Name));
        Assert.AreSequenceEqual([SpanKind.Consumer, SpanKind.Consumer], spans.Select(span => span.Kind));
        Assert.AreSequenceEqual([ActivityStatusCode.Error, ActivityStatusCode.Error],
            spans.Select(span => span.Status));
        Assert.AreSequenceEqual(["rich-scope", "rich-scope"], spans.Select(span => span.Scope?.Name));
        Assert.AreSequenceEqual([2, 2], traces.Select(trace => trace.Capture.Spans.Count));
    }

    [TestMethod]
    public async Task TelemetryCapture_MultipleWaiters_AreNotifiedByOneRealExport()
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        var first = receiver.AwaitAsync(s => s.Spans.Count == 1, OtlpReceiver.DefaultEvidenceTimeout,
            TestContext.CancellationToken);
        var second = receiver.AwaitAsync(s => s.Receipts.Count == 1, OtlpReceiver.DefaultEvidenceTimeout,
            TestContext.CancellationToken);
        using var response = await OtlpHttp.PostAsync(receiver.Endpoint, RichSpan().ToByteArray(),
            TestContext.CancellationToken);
        response.EnsureSuccessStatusCode();
        var results = await Task.WhenAll(first, second);
        var spans = results.Select(result => Assert.ContainsSingle(result.Spans)).ToArray();
        Assert.AreSequenceEqual(["rich.operation", "rich.operation"], spans.Select(span => span.Name));
        Assert.AreSequenceEqual([TraceId, TraceId], spans.Select(span => span.TraceId));
        Assert.AreSequenceEqual([SpanKind.Consumer, SpanKind.Consumer], spans.Select(span => span.Kind));
        Assert.AreSequenceEqual([ActivityStatusCode.Error, ActivityStatusCode.Error],
            spans.Select(span => span.Status));
        Assert.AreSequenceEqual(["rich-scope", "rich-scope"], spans.Select(span => span.Scope?.Name));
        Assert.AreSequenceEqual([1L, 1L], results.Select(result => Assert.ContainsSingle(result.Receipts).Accepted));
    }

    [TestMethod]
    public async Task TelemetryCapture_StartupCanceled_ThrowsCancellationInsteadOfReturningAReceiver()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => OtlpReceiver.StartAsync(cancellation.Token));
    }

    [TestMethod]
    public async Task TelemetryCapture_EvidenceMissingAtTimeout_Fails()
    {
        var previous = Activity.Current;
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => TelemetryCapture.TraceAsync(_ => Task.CompletedTask,
            trace => trace.Spans.Count > 0, TimeSpan.Zero, TestContext.CancellationToken));
        Assert.AreSame(previous, Activity.Current);
    }

    private async Task<(TraceEvidence Trace, string? AmbientOperation)> CaptureAsync(int number)
    {
        string? ambientOperation = null;
        var captured = await TelemetryCapture.TraceAsync(async capture =>
            {
                ambientOperation = Activity.Current?.OperationName;
                var request = RichSpan();
                request.ResourceSpans[0].ScopeSpans[0].Spans[0].TraceId = Id(capture.TraceId);
                request.ResourceSpans[0].ScopeSpans[0].Spans[0].Name = "capture." + number;
                request.ResourceSpans.Add(RichSpan().ResourceSpans[0]);
                using var response = await OtlpHttp.PostAsync(capture.Endpoint, request.ToByteArray(),
                    TestContext.CancellationToken);
                response.EnsureSuccessStatusCode();
            }, trace => trace.Spans.Count == 1 && trace.Capture.Spans.Count == 2,
            cancellationToken: TestContext.CancellationToken);
        return (captured, ambientOperation);
    }
}
