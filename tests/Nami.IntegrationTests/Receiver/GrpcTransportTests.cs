using System.Net;
using System.Net.Http.Headers;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using JetBrains.Annotations;
using Nami.Tests.Evidence;
using Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Metrics.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Resource.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Trace.V1;
using ProtoLog = Nami.Tests.Wire.OpenTelemetry.Proto.Logs.V1.LogRecord;

namespace Nami.IntegrationTests.Receiver;

[TestClass]
public sealed class GrpcTransportTests : IAsyncDisposable
{
    private const string Gzip = "gzip";
    private const string EncodingRequestHeader = "grpc-internal-encoding-request";
    private OtlpReceiver? _ownedReceiver;

    [UsedImplicitly] public TestContext TestContext { get; set; } = null!;

    public ValueTask DisposeAsync()
    {
        return _ownedReceiver is { } receiver ? receiver.DisposeAsync() : ValueTask.CompletedTask;
    }

    [TestMethod]
    [DataRow(Signals.Traces, null)]
    [DataRow(Signals.Logs, null)]
    [DataRow(Signals.Metrics, null)]
    [DataRow(Signals.Traces, Gzip)]
    [DataRow(Signals.Logs, Gzip)]
    [DataRow(Signals.Metrics, Gzip)]
    public async Task GrpcReceiver_UnknownFieldsNoncanonicalVarintsAndGzip_KeepTheOriginalMessage(string signal,
        string? encoding)
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        using var wire = new WireHeaders();
        using var channel =
            GrpcChannel.ForAddress(receiver.GrpcEndpoint, new GrpcChannelOptions { HttpHandler = wire });
        byte[] unknownFieldsDescendingWithNonMinimalVarint1 = [0xf8, 0x07, 0x81, 0x00, 0xf0, 0x07, 0x02];
        using var call = OtlpGrpc.ExportAsync(channel, signal, unknownFieldsDescendingWithNonMinimalVarint1,
            new CallOptions(CompressionHeaders(encoding), cancellationToken: TestContext.CancellationToken));
        Assert.IsEmpty(await call.ResponseAsync);
        Assert.AreEqual(encoding, wire.Encoding);
        var snapshot = receiver.Snapshot();
        Assert.IsEmpty(snapshot.Spans);
        Assert.IsEmpty(snapshot.Logs);
        Assert.IsEmpty(snapshot.Metrics);
        var receipt = Assert.ContainsSingle(snapshot.Receipts);
        Assert.AreEqual(OtlpTransport.Grpc, receipt.Transport);
        Assert.AreEqual(signal, receipt.Signal);
        Assert.AreEqual(0L, receipt.Accepted);
        Assert.AreEqual(0L, receipt.Rejected);
        Assert.IsNull(receipt.Error);
        var original = Assert.ContainsSingle(snapshot.Requests);
        Assert.AreEqual(OtlpTransport.Grpc, original.Transport);
        Assert.AreEqual(signal, original.Signal);
        Assert.AreSequenceEqual(unknownFieldsDescendingWithNonMinimalVarint1, original.Payload.ToArray());
    }

    [TestMethod]
    [DataRow(Signals.Traces)]
    [DataRow(Signals.Logs)]
    [DataRow(Signals.Metrics)]
    public async Task GrpcReceiver_MalformedProtobuf_ReturnsInvalidArgumentAndKeepsTheOriginal(string signal)
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        using var channel = GrpcChannel.ForAddress(receiver.GrpcEndpoint);
        using var call = OtlpGrpc.ExportAsync(channel, signal, [0x80],
            new CallOptions(cancellationToken: TestContext.CancellationToken));
        var failure = await Assert.ThrowsExactlyAsync<RpcException>(() => call.ResponseAsync);
        Assert.AreEqual(StatusCode.InvalidArgument, failure.StatusCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(failure.Status.Detail));
        var snapshot = receiver.Snapshot();
        var receipt = Assert.ContainsSingle(snapshot.Receipts);
        Assert.AreEqual(failure.Status.Detail, receipt.Error);
        Assert.AreEqual(OtlpTransport.Grpc, receipt.Transport);
        Assert.AreEqual(0L, receipt.Accepted);
        Assert.AreEqual(0L, receipt.Rejected);
        Assert.AreSequenceEqual(new byte[] { 0x80 }, Assert.ContainsSingle(snapshot.Requests).Payload.ToArray());
    }

    [TestMethod]
    [DataRow(Signals.Traces)]
    [DataRow(Signals.Logs)]
    [DataRow(Signals.Metrics)]
    public async Task GrpcReceiver_PartiallyInvalidExport_KeepsValidRecordsReportsExactRejectionsAndRecovers(
        string signal)
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        using var channel = GrpcChannel.ForAddress(receiver.GrpcEndpoint);
        var bytes = Request(signal, true, false).ToByteArray();
        using var call = OtlpGrpc.ExportAsync(channel, signal, bytes,
            new CallOptions(cancellationToken: TestContext.CancellationToken));
        var acknowledgement = Signals.PartialSuccess(signal, await call.ResponseAsync);
        Assert.IsNotNull(acknowledgement);
        var (rejected, message) = acknowledgement.Value;
        Assert.AreEqual(1L, rejected);
        Assert.IsFalse(string.IsNullOrWhiteSpace(message));
        var snapshot = receiver.Snapshot();
        var receipt = Assert.ContainsSingle(snapshot.Receipts);
        Assert.AreEqual(1L, receipt.Accepted);
        Assert.AreEqual(1L, receipt.Rejected);
        Assert.AreEqual(message, receipt.Error);
        Assert.AreEqual(OtlpTransport.Grpc, receipt.Transport);
        Assert.AreEqual(1, snapshot.Spans.Count + snapshot.Logs.Count + snapshot.Metrics.Count);
        Assert.AreSequenceEqual(bytes, Assert.ContainsSingle(snapshot.Requests).Payload.ToArray());
        using var callAfterRejection = OtlpGrpc.ExportAsync(channel, signal, [],
            new CallOptions(cancellationToken: TestContext.CancellationToken));
        Assert.IsEmpty(await callAfterRejection.ResponseAsync);
        Assert.IsNull(receiver.Snapshot().Receipts[1].Error);
        Assert.HasCount(1, snapshot.Receipts);
    }

    [TestMethod]
    [DataRow(Signals.Traces)]
    [DataRow(Signals.Logs)]
    [DataRow(Signals.Metrics)]
    public async Task GrpcReceiver_ValidRecordWithWarning_IsAcknowledgedWithoutARejection(string signal)
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        using var channel = GrpcChannel.ForAddress(receiver.GrpcEndpoint);
        using var call = OtlpGrpc.ExportAsync(channel, signal, Request(signal, false, true).ToByteArray(),
            new CallOptions(cancellationToken: TestContext.CancellationToken));
        var acknowledgement = Signals.PartialSuccess(signal, await call.ResponseAsync);
        Assert.IsNotNull(acknowledgement);
        var (rejected, message) = acknowledgement.Value;
        Assert.AreEqual(0L, rejected);
        Assert.Contains("StringValueStrindex 7", message);
        var receipt = Assert.ContainsSingle(receiver.Snapshot().Receipts);
        Assert.AreEqual(1L, receipt.Accepted);
        Assert.IsNull(receipt.Error);
        Assert.AreEqual(message, Assert.ContainsSingle(receipt.Warnings));
    }

    [TestMethod]
    [DataRow(1024, null)]
    [DataRow(1024, Gzip)]
    [DataRow(OtlpReceiver.DefaultMaxPayloadBytes, null)]
    [DataRow(OtlpReceiver.DefaultMaxPayloadBytes, Gzip)]
    public async Task GrpcReceiver_DecompressedMessageOverLimit_IsRejectedBeforeDecoding(int limit, string? encoding)
    {
        await using var receiver = await StartReceiverAsync(limit);
        using var channel = GrpcChannel.ForAddress(receiver.GrpcEndpoint);
        using var call = OtlpGrpc.ExportAsync(channel, Signals.Traces, new byte[limit + 1],
            new CallOptions(CompressionHeaders(encoding), cancellationToken: TestContext.CancellationToken));
        var failure = await Assert.ThrowsExactlyAsync<RpcException>(() => call.ResponseAsync);
        Assert.AreEqual(StatusCode.ResourceExhausted, failure.StatusCode, failure.Status.Detail);
        Assert.Contains($"{limit}-byte", failure.Status.Detail);
        Assert.IsEmpty(receiver.Snapshot().Requests);
        Assert.IsEmpty(receiver.Snapshot().Spans);
    }

    [TestMethod]
    [DataRow(1024, null)]
    [DataRow(1024, Gzip)]
    [DataRow(OtlpReceiver.DefaultMaxPayloadBytes, null)]
    [DataRow(OtlpReceiver.DefaultMaxPayloadBytes, Gzip)]
    public async Task GrpcReceiver_MessageExactlyAtDecompressedLimit_IsAccepted(int limit, string? encoding)
    {
        await using var receiver = await StartReceiverAsync(limit);
        using var channel = GrpcChannel.ForAddress(receiver.GrpcEndpoint);
        var bytes = UnknownFieldPayload(limit);
        using var call = OtlpGrpc.ExportAsync(channel, Signals.Traces, bytes,
            new CallOptions(CompressionHeaders(encoding), cancellationToken: TestContext.CancellationToken));
        Assert.IsEmpty(await call.ResponseAsync);
        Assert.AreSequenceEqual(bytes, Assert.ContainsSingle(receiver.Snapshot().Requests).Payload.ToArray());
        Assert.IsNull(Assert.ContainsSingle(receiver.Snapshot().Receipts).Error);
    }

    [TestMethod]
    public async Task GrpcReceiver_IncompleteUploadCancelled_KeepsNoPartialCaptureAndStaysUsable()
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(10));
        using var content = new PausedGrpcContent(cancellation.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(receiver.GrpcEndpoint, OtlpGrpc.Export(Signals.Traces).FullName));
        request.Version = HttpVersion.Version20;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
        request.Content = content;
        request.Headers.TE.Add(new TransferCodingWithQualityHeaderValue("trailers"));
        var sending = TelemetryLifecycle.Http.SendAsync(request, cancellation.Token);
        var first = await Task.WhenAny(content._started.Task, sending);
        var wasSuspended = first == content._started.Task && !sending.IsCompleted;
        await cancellation.CancelAsync();
        content._resume.TrySetResult();
        await Assert.ThrowsAsync<OperationCanceledException>(() => sending);
        Assert.IsTrue(wasSuspended);
        using var channel = GrpcChannel.ForAddress(receiver.GrpcEndpoint);
        using var next = OtlpGrpc.ExportAsync(channel, Signals.Traces, [],
            new CallOptions(cancellationToken: TestContext.CancellationToken));
        Assert.IsEmpty(await next.ResponseAsync);
        Assert.IsEmpty(Assert.ContainsSingle(receiver.Snapshot().Requests).Payload.ToArray());
        Assert.IsNull(Assert.ContainsSingle(receiver.Snapshot().Receipts).Error);
    }

    [TestMethod]
    public async Task GrpcReceiver_DeadlineExpired_CreatesNoExportEvidence()
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        using var channel = GrpcChannel.ForAddress(receiver.GrpcEndpoint);
        using var call = OtlpGrpc.ExportAsync(channel, Signals.Traces, [],
            new CallOptions(deadline: TimeProvider.System.GetUtcNow().AddSeconds(-1).UtcDateTime,
                cancellationToken: TestContext.CancellationToken));
        var failure = await Assert.ThrowsExactlyAsync<RpcException>(() => call.ResponseAsync);
        Assert.AreEqual(StatusCode.DeadlineExceeded, failure.StatusCode);
        Assert.IsEmpty(receiver.Snapshot().Requests);
    }

    [TestMethod]
    public async Task OtlpReceiver_Disposed_ClosesBothListenersToFreshConnectionsAndWakesAPendingWait()
    {
        var receiver = _ownedReceiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        using var channel = GrpcChannel.ForAddress(receiver.GrpcEndpoint);
        using (var live = OtlpGrpc.ExportAsync(channel, Signals.Traces, [],
                   new CallOptions(cancellationToken: TestContext.CancellationToken)))
            Assert.IsEmpty(await live.ResponseAsync);
        var waiting = receiver.AwaitAsync(snapshot => snapshot.Spans.Count > 0, OtlpReceiver.DefaultEvidenceTimeout,
            TestContext.CancellationToken);
        await receiver.DisposeAsync();
        _ownedReceiver = null;
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => waiting);
        using var freshChannelToClosedListener = GrpcChannel.ForAddress(receiver.GrpcEndpoint);
        using var closed = OtlpGrpc.ExportAsync(freshChannelToClosedListener, Signals.Traces, [],
            new CallOptions(deadline: TimeProvider.System.GetUtcNow().AddSeconds(5).UtcDateTime,
                cancellationToken: TestContext.CancellationToken));
        var failure = await Assert.ThrowsExactlyAsync<RpcException>(() => closed.ResponseAsync);
        Assert.AreEqual(StatusCode.Unavailable, failure.StatusCode, failure.Status.Detail);
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            TelemetryLifecycle.Http.GetAsync(receiver.Endpoint, TestContext.CancellationToken));
    }

    private Task<OtlpReceiver> StartReceiverAsync(int limit)
    {
        return limit == OtlpReceiver.DefaultMaxPayloadBytes
            ? OtlpReceiver.StartAsync(TestContext.CancellationToken)
            : OtlpReceiver.StartAsync(limit, TestContext.CancellationToken);
    }

    private static Metadata CompressionHeaders(string? encoding)
    {
        var headers = new Metadata();
        if (encoding is not null) headers.Add(EncodingRequestHeader, encoding);
        return headers;
    }

    private static byte[] UnknownFieldPayload(int size)
    {
        var bytes = new byte[size];
        byte[] unknownLengthDelimitedField127Tag = [0xfa, 0x07];
        const int FourByteLengthVarint = 4;
        unknownLengthDelimitedField127Tag.CopyTo(bytes, 0);
        var length = bytes.Length - unknownLengthDelimitedField127Tag.Length - FourByteLengthVarint;
        for (var i = 0; i < FourByteLengthVarint; i++)
        {
            bytes[unknownLengthDelimitedField127Tag.Length + i] =
                (byte)((length >> (7 * i)) | (i < FourByteLengthVarint - 1 ? 0x80 : 0));
        }

        return bytes;
    }

    private static IMessage Request(string signal, bool invalid, bool warning)
    {
        var resource = new Resource
        {
            Attributes =
            {
                OtlpEvidence.Pair("custom.value", warning
                    ? new AnyValue { StringValueStrindex = 7 }
                    : new AnyValue { IntValue = 9_007_199_254_740_993L })
            }
        };
        switch (signal)
        {
            case Signals.Traces:
                List<Span> spans =
                [
                    new()
                    {
                        TraceId = OtlpEvidence.Id("01010101010101010101010101010101"),
                        SpanId = OtlpEvidence.Id("0202020202020202"), Name = "preserved"
                    }
                ];
                if (invalid) spans.Add(new Span { Name = "bad-identity" });
                return OtlpEvidence.Traces(resource, new InstrumentationScope { Name = "grpc.trace" }, spans);
            case Signals.Logs:
                List<ProtoLog> logs = [new() { Body = new AnyValue { BoolValue = false } }];
                if (invalid) logs.Add(new ProtoLog { TraceId = OtlpEvidence.Id("01") });
                return OtlpEvidence.Logs(resource, logs);
            default:
                List<Metric> metrics =
                [
                    new()
                    {
                        Name = "grpc.counter", Sum = new Sum
                        {
                            IsMonotonic = true, AggregationTemporality = AggregationTemporality.Delta,
                            DataPoints = { new NumberDataPoint { TimeUnixNano = 10, AsInt = 7 } }
                        }
                    }
                ];
                if (invalid)
                {
                    metrics.Add(new Metric
                    {
                        Name = "bad-attributes", Gauge = new Gauge
                        {
                            DataPoints =
                            {
                                new NumberDataPoint
                                {
                                    TimeUnixNano = 10, AsInt = 9, Attributes =
                                        { OtlpEvidence.Pair("same", 1), OtlpEvidence.Pair("same", 2) }
                                }
                            }
                        }
                    });
                }

                return OtlpEvidence.Metrics(resource, metrics);
        }
    }

    private sealed class WireHeaders : DelegatingHandler
    {
        internal WireHeaders()
        {
            InnerHandler = new SocketsHttpHandler();
        }

        internal string? Encoding { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Encoding = request.Headers.TryGetValues("grpc-encoding", out var values) ? values.Single() : null;
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class PausedGrpcContent : HttpContent
    {
        internal readonly TaskCompletionSource _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationToken _operationCancellation;

        internal PausedGrpcContent(CancellationToken operationCancellation)
        {
            _operationCancellation = operationCancellation;
            Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 7;
            return true;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return WriteAsync(stream, CancellationToken.None);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context,
            CancellationToken cancellationToken)
        {
            return WriteAsync(stream, cancellationToken);
        }

        private async Task WriteAsync(Stream stream, CancellationToken cancellationToken)
        {
            using var linked =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _operationCancellation);
            cancellationToken = linked.Token;
            // A unary frame declaring two bytes, paused after the first payload byte.
            await stream.WriteAsync(new byte[] { 0, 0, 0, 0, 2, 0x80 }, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            _started.TrySetResult();
            await _resume.Task.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await stream.WriteAsync(new byte[] { 0 }, cancellationToken);
        }
    }
}
