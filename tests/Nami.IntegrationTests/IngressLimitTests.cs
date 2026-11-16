using System.IO.Compression;
using System.Net;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Nami.Collector.Ingestion;

namespace Nami.IntegrationTests;

/// <summary>
///     E4a. What one export request may cost before nami has decoded it. nami receiver policy: a request holds at
///     most <see cref="IngestLimits.MaxRequestWireBytes" /> on the wire and
///     <see cref="IngestLimits.MaxRequestDecompressedBytes" /> decompressed, 16 MiB each unless configured, over
///     OTLP/HTTP and OTLP/gRPC alike. Anything over is refused at once: HTTP 413 with an OTLP status, gRPC
///     RESOURCE_EXHAUSTED. The body buffer grows with the bytes that arrive, never to a length a sender only
///     declared (engine audit A8), and a compressed body is never inflated past the bound.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class IngressLimitTests(TestContext context)
{
    private const int Limit = 16 * 1024 * 1024;
    private const string GrpcEncodingRequestHeader = "grpc-internal-encoding-request";

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow(false, false, DisplayName = "Content-Length")]
    [DataRow(false, true, DisplayName = "chunked, no Content-Length")]
    [DataRow(true, false, DisplayName = "gzip, Content-Length")]
    [DataRow(true, true, DisplayName = "gzip, chunked")]
    public async Task Http_ABodyAtSixteenMebibytesIsAccepted_AndOneByteOverIsRefusedWith413(bool gzip, bool chunked)
    {
        await using var host = await StartAsync();

        using var atTheBound = await PostAsync(host, UnknownFieldPayload(Limit), gzip, chunked);
        Assert.AreEqual(HttpStatusCode.OK, atTheBound.StatusCode);

        using var over = await PostAsync(host, UnknownFieldPayload(Limit + 1), gzip, chunked);
        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, over.StatusCode);
        var error = await Assert.That.HasOtlpErrorAsync(over, context.CancellationToken);
        Assert.AreEqual($"OTLP payload exceeds the 16777216-byte {(gzip ? "decompressed" : "wire")} limit.", error.Message);

        using var afterwards = await PostAsync(host, UnknownFieldPayload(64), gzip, chunked);
        Assert.AreEqual(HttpStatusCode.OK, afterwards.StatusCode, "The listener keeps accepting exports after a refusal.");
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task Http_ASmallGzipBodyThatInflatesToSixtyFourMebibytes_IsRefusedWith413()
    {
        await using var host = await StartAsync();
        var bomb = OtlpHttp.Compress(UnknownFieldPayload(4 * Limit));
        Assert.IsLessThan(Limit / 16, bomb.Length, "The compressed body is small.");

        using var response = await PostAsync(host, bomb, false, false, "gzip");

        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var error = await Assert.That.HasOtlpErrorAsync(response, context.CancellationToken);
        Assert.AreEqual("OTLP payload exceeds the 16777216-byte decompressed limit.", error.Message);
    }

    /// <summary>
    ///     The same bomb at the reader, where the cost can be counted: 256 MiB behind some hundred kilobytes of gzip.
    ///     The read stops with the first byte past the bound, so most of the compressed input is never taken and the
    ///     allocations stay near the bound, far below what the body would inflate to.
    /// </summary>
    [TestMethod]
    public async Task Parser_AGzipBomb_IsRefusedWhileItInflates_WithoutAllocatingWhatItWouldInflateTo()
    {
        const int Inflated = 16 * Limit;
        await using var body = new CountingStream(ZerosCompressed(Inflated));
        Assert.IsLessThan(1024 * 1024, (int)body.Length, "The compressed body is small.");
        var request = new DefaultHttpContext().Request;
        request.Body = body;
        request.ContentLength = body.Length;
        request.Headers.ContentEncoding = "gzip";

        var before = GC.GetAllocatedBytesForCurrentThread();
        var parse = OtlpPayloadParser.ParseTraceRequestAsync(request, OtlpPayloadEncoding.Protobuf,
            new IngestLimits(), context.CancellationToken);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.IsTrue(parse.IsCompleted, "The body completes every read synchronously, so the count covers the whole read.");
        var refused = await Assert.ThrowsExactlyAsync<OtlpPayloadTooLargeException>(() => parse);
        Assert.AreEqual("OTLP payload exceeds the 16777216-byte decompressed limit.", refused.Message);
        Assert.IsLessThan(3L * Limit, allocated, "A doubling buffer up to the bound, not the 256 MiB the body inflates to.");
        Assert.IsLessThan(body.Length / 4, body.BytesRead, "Inflating stopped at the bound, with most of the input unread.");
    }

    /// <summary>
    ///     Engine audit A8. A request that declares the largest legal Content-Length and then sends one kilobyte costs
    ///     about what arrived, not the sixteen mebibytes it declared.
    /// </summary>
    [TestMethod]
    public async Task Parser_ADeclaredLengthThatDoesNotArrive_AllocatesForTheBytesThatArrived()
    {
        await using var body = new StallingStream(1024);
        var request = new DefaultHttpContext().Request;
        request.Body = body;
        request.ContentLength = Limit;

        var before = GC.GetAllocatedBytesForCurrentThread();
        var parse = OtlpPayloadParser.ParseTraceRequestAsync(request, OtlpPayloadEncoding.Protobuf,
            new IngestLimits(), context.CancellationToken);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.IsTrue(parse.IsCompleted, "The body completes every read synchronously, so the count covers the whole read.");
        _ = await Assert.ThrowsExactlyAsync<EndOfStreamException>(() => parse);
        Assert.IsLessThan(1024 * 1024, allocated, "The buffer follows the bytes that arrived.");
    }

    /// <summary>A body that arrives in full under its declared length is kept byte for byte in a buffer of that length.</summary>
    [TestMethod]
    public async Task Parser_ADeclaredLengthThatArrives_IsReadWholeIntoABufferOfThatLength()
    {
        var payload = UnknownFieldPayload(3 * 1024 * 1024 + 17);
        payload.AsSpan(6).Fill(0x5a);
        await using var body = new CountingStream(payload);
        var request = new DefaultHttpContext().Request;
        request.Body = body;
        request.ContentLength = payload.Length;

        var before = GC.GetAllocatedBytesForCurrentThread();
        var parse = OtlpPayloadParser.ParseTraceRequestAsync(request, OtlpPayloadEncoding.Protobuf,
            new IngestLimits(), context.CancellationToken);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.IsTrue(parse.IsCompleted);
        Assert.AreSequenceEqual(payload, Google.Protobuf.MessageExtensions.ToByteArray(await parse));
        // The doubling steps below the declared length sum to less than twice that length; the parsed message holds
        // one more copy of the unknown field.
        Assert.IsLessThan(4L * payload.Length, allocated);
    }

    /// <summary>Kestrel's own request-body bound, where it is reached first, is the same refusal and not a 400.</summary>
    [TestMethod]
    public async Task Parser_KestrelRefusingTheBodyAsTooLarge_IsThePayloadTooLargeRefusal()
    {
        await using var body = new StallingStream(1024, new BadHttpRequestException("Request body too large.",
            StatusCodes.Status413PayloadTooLarge));
        var request = new DefaultHttpContext().Request;
        request.Body = body;

        var refused = await Assert.ThrowsExactlyAsync<OtlpPayloadTooLargeException>(() =>
            OtlpPayloadParser.ParseTraceRequestAsync(request, OtlpPayloadEncoding.Protobuf, new IngestLimits(),
                context.CancellationToken));

        Assert.AreEqual("OTLP payload exceeds the 16777216-byte wire limit.", refused.Message);
    }

    /// <summary>
    ///     The bounds are the engine's configuration, not constants: 1 KiB on the wire and 4 KiB decompressed here.
    ///     An uncompressed body is its own decompressed form and is held to the smaller of the two.
    /// </summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task Http_ConfiguredBounds_HoldOnTheWireAndDecompressed()
    {
        await using var host = await StartAsync(new Dictionary<string, string?>
        {
            [IngestLimits.MaxRequestWireBytesKey] = "1024",
            [IngestLimits.MaxRequestDecompressedBytesKey] = "4096"
        });

        using (var response = await PostAsync(host, UnknownFieldPayload(1024), false, false))
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using (var response = await PostAsync(host, UnknownFieldPayload(1025), false, false))
            await AssertRefusedAsync(response, "OTLP payload exceeds the 1024-byte wire limit.");
        using (var response = await PostAsync(host, UnknownFieldPayload(1025), false, true))
            await AssertRefusedAsync(response, "OTLP payload exceeds the 1024-byte wire limit.");

        using (var response = await PostAsync(host, UnknownFieldPayload(4096), true, false))
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using (var response = await PostAsync(host, UnknownFieldPayload(4097), true, false))
            await AssertRefusedAsync(response, "OTLP payload exceeds the 4096-byte decompressed limit.");

        // Bytes gzip cannot shrink: within the decompressed bound, over the wire bound once compressed.
        var incompressible = UnknownFieldPayload(3000);
        new Random(20261116).NextBytes(incompressible.AsSpan(6));
        Assert.IsGreaterThan(1024, OtlpHttp.Compress(incompressible).Length);
        using (var response = await PostAsync(host, incompressible, true, false))
            await AssertRefusedAsync(response, "OTLP payload exceeds the 1024-byte wire limit.");
        using (var response = await PostAsync(host, incompressible, true, true))
            await AssertRefusedAsync(response, "OTLP payload exceeds the 1024-byte wire limit.");
    }

    /// <summary>Engine audit A15: gRPC kept the library's 4 MiB beside HTTP's 16 MiB. Both hold one bound now.</summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow(false, DisplayName = "identity")]
    [DataRow(true, DisplayName = "gzip")]
    public async Task Grpc_AMessageAtSixteenMebibytesIsAccepted_AndOneByteOverIsRefusedWithResourceExhausted(bool gzip)
    {
        await using var host = await StartAsync();

        Assert.IsEmpty(await ExportAsync(host, UnknownFieldPayload(Limit), gzip));

        var refused = await Assert.ThrowsExactlyAsync<RpcException>(() =>
            ExportAsync(host, UnknownFieldPayload(Limit + 1), gzip));
        Assert.AreEqual(StatusCode.ResourceExhausted, refused.StatusCode, refused.Status.Detail);

        Assert.IsEmpty(await ExportAsync(host, UnknownFieldPayload(64), gzip),
            "The listener keeps accepting exports after a refusal.");
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task Grpc_ASmallCompressedMessageThatInflatesToSixtyFourMebibytes_IsRefusedWithResourceExhausted()
    {
        await using var host = await StartAsync();

        var refused = await Assert.ThrowsExactlyAsync<RpcException>(() =>
            ExportAsync(host, UnknownFieldPayload(4 * Limit), true));

        Assert.AreEqual(StatusCode.ResourceExhausted, refused.StatusCode, refused.Status.Detail);
    }

    /// <summary>
    ///     gRPC has one number for a message, on the wire and decompressed, so it takes the smaller of the two
    ///     configured bounds: nothing over either is accepted.
    /// </summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow("1024", "4096", false, DisplayName = "wire bound smaller, identity")]
    [DataRow("1024", "4096", true, DisplayName = "wire bound smaller, gzip")]
    [DataRow("4096", "1024", false, DisplayName = "decompressed bound smaller, identity")]
    [DataRow("4096", "1024", true, DisplayName = "decompressed bound smaller, gzip")]
    public async Task Grpc_ConfiguredBounds_TheSmallerOneHolds(string wire, string decompressed, bool gzip)
    {
        await using var host = await StartAsync(new Dictionary<string, string?>
        {
            [IngestLimits.MaxRequestWireBytesKey] = wire,
            [IngestLimits.MaxRequestDecompressedBytesKey] = decompressed
        });

        Assert.IsEmpty(await ExportAsync(host, UnknownFieldPayload(1024), gzip));

        var refused = await Assert.ThrowsExactlyAsync<RpcException>(() =>
            ExportAsync(host, UnknownFieldPayload(1025), gzip));
        Assert.AreEqual(StatusCode.ResourceExhausted, refused.StatusCode, refused.Status.Detail);
    }

    private Task<CollectorHost> StartAsync(IReadOnlyDictionary<string, string?>? settings = null)
    {
        var directory = context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");
        return CollectorHost.StartAsync(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".duckdb"),
            context.CancellationToken, settings: settings);
    }

    private async Task<HttpResponseMessage> PostAsync(CollectorHost host, byte[] payload, bool gzip, bool chunked,
        string? declaredEncoding = null)
    {
        using var content = OtlpHttp.Content(gzip ? OtlpHttp.Compress(payload) : payload,
            gzip ? "gzip" : declaredEncoding, chunked);
        Assert.AreEqual(!chunked, content.Headers.ContentLength.HasValue);
        return await TelemetryLifecycle.Http.PostAsync(new Uri(host.HttpEndpoint, "v1/" + Signals.Traces), content,
            context.CancellationToken);
    }

    private Task<byte[]> ExportAsync(CollectorHost host, byte[] payload, bool gzip)
    {
        var headers = new Metadata();
        if (gzip) headers.Add(GrpcEncodingRequestHeader, "gzip");
        return host.ExportGrpcAsync(Signals.Traces, payload, context.CancellationToken, headers);
    }

    private async Task AssertRefusedAsync(HttpResponseMessage response, string message)
    {
        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var error = await Assert.That.HasOtlpErrorAsync(response, context.CancellationToken);
        Assert.AreEqual(message, error.Message);
    }

    /// <summary>A well-formed export of <paramref name="size" /> bytes: one unknown length-delimited field (127) of zeros.</summary>
    private static byte[] UnknownFieldPayload(int size)
    {
        var payload = new byte[size];
        payload[0] = 0xfa;
        payload[1] = 0x07;
        var length = size - 6;
        for (var i = 0; i < 4; i++) payload[2 + i] = (byte)((length >> (7 * i)) | (i < 3 ? 0x80 : 0));
        return payload;
    }

    /// <summary>The gzip encoding of <paramref name="count" /> zero bytes, written without holding them.</summary>
    private static byte[] ZerosCompressed(int count)
    {
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, true))
        {
            var block = new byte[1024 * 1024];
            for (var written = 0; written < count; written += block.Length) gzip.Write(block);
        }

        return compressed.ToArray();
    }

    /// <summary>A body that completes every read synchronously and counts what was taken from it.</summary>
    private sealed class CountingStream(byte[] bytes) : Stream
    {
        internal long BytesRead { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => bytes.Length;
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }

        public override int Read(Span<byte> buffer)
        {
            var read = (int)Math.Min(buffer.Length, bytes.Length - BytesRead);
            bytes.AsSpan((int)BytesRead, read).CopyTo(buffer);
            BytesRead += read;
            return read;
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Read(buffer.Span));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer.AsSpan(offset, count)));

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A body that delivers <paramref name="available" /> bytes and then fails as a connection that ended does.</summary>
    private sealed class StallingStream(int available, Exception? failure = null) : Stream
    {
        private int _left = available;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_left is 0) throw failure ?? new EndOfStreamException("Unexpected end of request content.");
            var read = Math.Min(_left, buffer.Length);
            buffer[..read].Clear();
            _left -= read;
            return read;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            try
            {
                return ValueTask.FromResult(Read(buffer.Span));
            }
            catch (Exception error)
            {
                return ValueTask.FromException<int>(error);
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
