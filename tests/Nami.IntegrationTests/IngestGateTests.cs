using System.Diagnostics;
using System.Net;
using Grpc.Core;
using Nami.Collector.Ingestion;
using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

/// <summary>
///     E4e. nami reads, parses, converts and stores <see cref="IngestLimits.MaxConcurrentRequests" /> export
///     requests at once, one unless configured (<see cref="OtlpIngestGate" />). A request takes its slot before its
///     body is read. One that finds no slot within <see cref="IngestLimits.MaxRequestWaitSeconds" /> is turned away
///     with an answer an exporter retries, unlike a request over a size bound: OTLP/HTTP 503 with Retry-After and
///     an OTLP Status, OTLP/gRPC UNAVAILABLE.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class IngestGateTests(TestContext context)
{
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow(CollectorHost.ProtobufTransport, DisplayName = "OTLP/HTTP protobuf")]
    [DataRow(CollectorHost.JsonTransport, DisplayName = "OTLP/HTTP JSON")]
    [DataRow(CollectorHost.GrpcTransport, DisplayName = "OTLP/gRPC")]
    public async Task ARequestThatFindsNoSlot_IsTurnedAwayToBeSentAgain_AndIsAcceptedOnceTheSlotIsFree(string transport)
    {
        var directory = context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");
        // One slot and a wait of one second. The wait is what makes the order certain: the held request, should it
        // arrive while a probe is in the slot, waits those milliseconds and takes the slot after it; a probe that
        // finds the slot held is turned away after its second.
        await using var host = await CollectorHost.StartAsync(Path.Combine(directory, "gate-" + transport + ".duckdb"),
            context.CancellationToken, settings: new Dictionary<string, string?>
            {
                [IngestLimits.MaxConcurrentRequestsKey] = "1", [IngestLimits.MaxRequestWaitSecondsKey] = "1"
            });
        var export = OtlpData.Traces(TestData.Nanos(DateTimeOffset.UtcNow.AddSeconds(-2)));

        // One export holds the slot: its headers and the first half of its body are sent, the rest is not.
        var payload = CollectorHost.Encode(Signals.Traces, export);
        var held = new HeldBody(payload);
        var holding = PostHeldAsync(host, held);
        var deadline = Stopwatch.StartNew();
        string? turnedAway = null;
        try
        {
            while (turnedAway is null)
            {
                Assert.IsLessThan(TimeSpan.FromSeconds(60), deadline.Elapsed, "The held request never took the slot.");
                // The collector takes the slot when the request's headers arrive. Should a probe have been in
                // the slot at that moment, the held request was turned away itself: hold another.
                _ = await Task.WhenAny(held.FirstHalfSent, holding).WaitAsync(context.CancellationToken);
                if (holding.IsCompleted)
                {
                    (await holding).Dispose();
                    held.Dispose();
                    held = new HeldBody(payload);
                    holding = PostHeldAsync(host, held);
                    continue;
                }

                turnedAway = await TrySendAsync(host, transport, export);
            }

            Assert.AreEqual(OtlpIngestGate.BusyMessage, turnedAway);

            held.Release();
            using (var first = await holding) Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
            Assert.IsNull(await TrySendAsync(host, transport, export), "With the slot free the same export is accepted.");
        }
        finally
        {
            held.Release();
            held.Dispose();
        }
    }

    private Task<HttpResponseMessage> PostHeldAsync(CollectorHost host, HeldBody held)
    {
        held.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(OtlpReceiver.ProtobufMediaType);
        return TelemetryLifecycle.Http.PostAsync(new Uri(host.HttpEndpoint, "v1/" + Signals.Traces), held,
            context.CancellationToken);
    }

    // Null when the export is accepted; the message of the answer when it is turned away, after its shape is checked.
    private async Task<string?> TrySendAsync(CollectorHost host, string transport, string export)
    {
        if (transport == CollectorHost.GrpcTransport)
        {
            try
            {
                _ = await host.ExportGrpcAsync(Signals.Traces, CollectorHost.Encode(Signals.Traces, export),
                    context.CancellationToken);
                return null;
            }
            catch (RpcException turnedAway)
            {
                Assert.AreEqual(StatusCode.Unavailable, turnedAway.StatusCode, turnedAway.Status.Detail);
                return turnedAway.Status.Detail;
            }
        }

        using var response = transport == CollectorHost.JsonTransport
            ? await host.PostJsonAsync(Signals.Traces, export, context.CancellationToken)
            : await host.PostProtobufAsync(Signals.Traces, CollectorHost.Encode(Signals.Traces, export), context.CancellationToken);
        if (response.StatusCode is HttpStatusCode.OK) return null;
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.AreEqual(TimeSpan.FromSeconds(OtlpIngestGate.RetryAfterSeconds), response.Headers.RetryAfter?.Delta);
        var error = await Assert.That.HasOtlpErrorAsync(response, context.CancellationToken,
            transport == CollectorHost.JsonTransport ? "application/json" : OtlpReceiver.ProtobufMediaType);
        return error.Message;
    }

    /// <summary>A request body whose first half is sent at once and whose second half waits for <see cref="Release" />.</summary>
    private sealed class HeldBody(byte[] payload) : HttpContent
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstHalfSent = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes when the headers and the first half of the body are on the wire.</summary>
        internal Task FirstHalfSent => _firstHalfSent.Task;

        internal void Release() => _released.TrySetResult();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context,
            CancellationToken cancellationToken)
        {
            var half = payload.Length / 2;
            await stream.WriteAsync(payload.AsMemory(0, half), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            _ = _firstHalfSent.TrySetResult();
            await _released.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(payload.AsMemory(half), cancellationToken).ConfigureAwait(false);
        }
    }
}
