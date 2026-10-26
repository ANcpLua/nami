using System.Net;
using Grpc.Core;
using Grpc.Net.Client;
using JetBrains.Annotations;

namespace Nami.IntegrationTests.Receiver;

[TestClass]
public sealed class PayloadLimitTests
{
    [UsedImplicitly] public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task OtlpReceiver_NonpositiveLimit_IsRejectedBeforeListenersOpen(int limit)
    {
        var failure = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            OtlpReceiver.StartAsync(limit, TestContext.CancellationToken));
        Assert.AreEqual("maxPayloadBytes", failure.ParamName);
    }

    [TestMethod]
    [DataRow(OtlpReceiver.DefaultMaxPayloadBytes, false, false)]
    [DataRow(OtlpReceiver.DefaultMaxPayloadBytes, true, false)]
    [DataRow(OtlpReceiver.DefaultMaxPayloadBytes, true, true)]
    [DataRow(1024, false, false)]
    [DataRow(1024, true, false)]
    [DataRow(1024, true, true)]
    public async Task HttpReceiver_DefaultOrConfiguredLimit_IsEnforcedWithAndWithoutContentLength(
        int limit, bool gzip, bool chunked)
    {
        await using var receiver = await StartReceiverAsync(limit);
        var belowLimit = UnknownFieldPayload(limit - 1);
        using var belowRequest = Request(receiver.Endpoint, belowLimit, gzip, chunked);
        Assert.AreEqual(!chunked, belowRequest.Content!.Headers.ContentLength.HasValue);
        using var belowResponse = await TelemetryLifecycle.Http.SendAsync(belowRequest, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, belowResponse.StatusCode);
        Assert.IsEmpty(await belowResponse.Content.ReadAsByteArrayAsync(TestContext.CancellationToken));
        Assert.AreSequenceEqual(belowLimit, receiver.Snapshot().Requests[^1].Payload.ToArray());

        var atLimit = UnknownFieldPayload(limit);
        using var atRequest = Request(receiver.Endpoint, atLimit, gzip, chunked);
        Assert.AreEqual(!chunked, atRequest.Content!.Headers.ContentLength.HasValue);
        using var atResponse = await TelemetryLifecycle.Http.SendAsync(atRequest, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, atResponse.StatusCode);
        Assert.IsEmpty(await atResponse.Content.ReadAsByteArrayAsync(TestContext.CancellationToken));
        Assert.AreSequenceEqual(atLimit, receiver.Snapshot().Requests[^1].Payload.ToArray());

        using var aboveRequest = Request(receiver.Endpoint, UnknownFieldPayload(limit + 1), gzip, chunked);
        Assert.AreEqual(!chunked, aboveRequest.Content!.Headers.ContentLength.HasValue);
        using var aboveResponse = await TelemetryLifecycle.Http.SendAsync(aboveRequest, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, aboveResponse.StatusCode);
        var error = await Assert.That.HasOtlpErrorAsync(aboveResponse, TestContext.CancellationToken);
        Assert.AreEqual((int)StatusCode.ResourceExhausted, error.Code);
        Assert.Contains($"{limit}-byte", error.Message);
        Assert.AreEqual(error.Message, receiver.Snapshot().Receipts[^1].Error);
        var snapshot = receiver.Snapshot();
        Assert.HasCount(2, snapshot.Requests);
        Assert.HasCount(3, snapshot.Receipts);
        Assert.IsTrue(snapshot.Receipts.Take(2).All(static receipt => receipt.Error is null));
        Assert.IsEmpty(snapshot.Spans);
    }

    [TestMethod]
    public async Task HttpReceiver_ChunkedIdentityRequest_IsHeldToTheKestrelWireBudget()
    {
        await using var receiver = await OtlpReceiver.StartAsync(1024, TestContext.CancellationToken);
        const int AcceptedSizeLeavingRoomForKestrelChunkFraming = 992;
        const int RejectedSizeAboveTheWireBudgetByItself = 1025;
        using var acceptedContent = OtlpHttp.Content(UnknownFieldPayload(AcceptedSizeLeavingRoomForKestrelChunkFraming),
            chunked: true);
        Assert.IsNull(acceptedContent.Headers.ContentLength);
        using var accepted = await TelemetryLifecycle.Http.PostAsync(
            new Uri(receiver.Endpoint, "v1/" + Signals.Metrics),
            acceptedContent, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
        using var rejectedContent =
            OtlpHttp.Content(UnknownFieldPayload(RejectedSizeAboveTheWireBudgetByItself), chunked: true);
        Assert.IsNull(rejectedContent.Headers.ContentLength);
        using var rejected = await TelemetryLifecycle.Http.PostAsync(
            new Uri(receiver.Endpoint, "v1/" + Signals.Metrics),
            rejectedContent, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, rejected.StatusCode);
        var error = await Assert.That.HasOtlpErrorAsync(rejected, TestContext.CancellationToken);
        Assert.AreEqual((int)StatusCode.ResourceExhausted, error.Code);
        Assert.Contains("1024-byte", error.Message);
        Assert.AreEqual(error.Message, receiver.Snapshot().Receipts[^1].Error);
        Assert.HasCount(1, receiver.Snapshot().Requests);
        Assert.HasCount(2, receiver.Snapshot().Receipts);
    }

    [TestMethod]
    public async Task HttpReceiver_CompressedWireOverflowWithoutContentLength_ReturnsAnOtlpError()
    {
        await using var receiver = await OtlpReceiver.StartAsync(1024, TestContext.CancellationToken);
        var gzip = OtlpHttp.Compress([0xfa, 0x07, 0x00]);
        var body = new byte[gzip.Length + 1026];
        gzip.AsSpan(0, 10).CopyTo(body);
        const byte GzipFileNameFlagEmittingNoDecodedBytes = 0x08;
        body[3] |= GzipFileNameFlagEmittingNoDecodedBytes;
        body.AsSpan(10, 1025).Fill((byte)'x');
        gzip.AsSpan(10).CopyTo(body.AsSpan(1036));
        using var content = OtlpHttp.Content(body, "gzip", true);
        Assert.IsNull(content.Headers.ContentLength);
        using var response = await TelemetryLifecycle.Http.PostAsync(new Uri(receiver.Endpoint, "v1/logs"),
            content, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var error = await Assert.That.HasOtlpErrorAsync(response, TestContext.CancellationToken);
        Assert.AreEqual((int)StatusCode.ResourceExhausted, error.Code);
        Assert.Contains("1024-byte", error.Message);
        Assert.IsEmpty(receiver.Snapshot().Requests);
        Assert.AreEqual(error.Message, Assert.ContainsSingle(receiver.Snapshot().Receipts).Error);
    }

    [TestMethod]
    public async Task TraceCapture_ConfiguredLimit_AppliesToHttpAndGrpc()
    {
        var trace = await TelemetryCapture.TraceAsync(async capture =>
            {
                using var channel = GrpcChannel.ForAddress(capture.GrpcEndpoint);
                using var call = OtlpGrpc.ExportAsync(channel, Signals.Traces, new byte[129],
                    new CallOptions(cancellationToken: TestContext.CancellationToken));
                var failure = await Assert.ThrowsExactlyAsync<RpcException>(() => call.ResponseAsync);
                Assert.AreEqual(StatusCode.ResourceExhausted, failure.StatusCode);
                Assert.Contains("128-byte", failure.Status.Detail);
                using var response =
                    await OtlpHttp.PostAsync(capture.Endpoint, new byte[129], TestContext.CancellationToken);
                Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
            }, result => result.Capture.Receipts.Count == 1, 128,
            cancellationToken: TestContext.CancellationToken);
        Assert.IsEmpty(trace.Capture.Requests);
        Assert.IsEmpty(trace.Spans);
        var error = Assert.ContainsSingle(trace.Capture.Receipts).Error;
        Assert.IsNotNull(error);
        Assert.Contains("128-byte", error);
    }

    private Task<OtlpReceiver> StartReceiverAsync(int limit)
    {
        return limit == OtlpReceiver.DefaultMaxPayloadBytes
            ? OtlpReceiver.StartAsync(TestContext.CancellationToken)
            : OtlpReceiver.StartAsync(limit, TestContext.CancellationToken);
    }

    private static HttpRequestMessage Request(Uri endpoint, byte[] payload, bool gzip, bool chunked)
    {
        return new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "v1/" + Signals.Traces))
        {
            Content = OtlpHttp.Content(gzip ? OtlpHttp.Compress(payload) : payload, gzip ? "gzip" : null, chunked),
            Headers = { ExpectContinue = !chunked }
        };
    }

    private static byte[] UnknownFieldPayload(int size)
    {
        var payload = new byte[size];
        payload[0] = 0xfa;
        payload[1] = 0x07;
        var length = size - 6;
        for (var i = 0; i < 4; i++) payload[2 + i] = (byte)((length >> (7 * i)) | (i < 3 ? 0x80 : 0));
        return payload;
    }
}
