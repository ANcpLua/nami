using System.IO.Compression;
using System.Text;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Nami.Collector.Ingestion;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using LogRecord = OpenTelemetry.Proto.Logs.V1.LogRecord;

namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public sealed class OtlpPayloadParserTests(TestContext context)
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task Parse_LargeNonSeekableRequest_PreservesContentWithoutInstallingDiskBuffering(bool json, bool gzip)
    {
        var expected = new ExportLogsServiceRequest
        {
            ResourceLogs =
            {
                new ResourceLogs
                {
                    ScopeLogs =
                    {
                        new ScopeLogs
                        {
                            LogRecords = { new LogRecord { Body = new AnyValue { StringValue = new string('x', 64 * 1024) } } }
                        }
                    }
                }
            }
        };
        var bytes = json ? Encoding.UTF8.GetBytes(JsonFormatter.Default.Format(expected)) : expected.ToByteArray();
        if (gzip)
        {
            using var compressed = new MemoryStream();
            await using (var compressor = new GZipStream(compressed, CompressionLevel.Fastest, true))
                await compressor.WriteAsync(bytes, context.CancellationToken);
            bytes = compressed.ToArray();
        }

        await using var body = new NonSeekableStream(bytes);
        var request = new DefaultHttpContext().Request;
        request.Body = body;
        if (gzip) request.Headers.ContentEncoding = "gzip";

        var actual = await OtlpPayloadParser.ParseLogsRequestAsync(request,
            json ? OtlpPayloadEncoding.Json : OtlpPayloadEncoding.Protobuf, context.CancellationToken);

        Assert.AreEqual(expected, actual);
        Assert.AreSame(body, request.Body, "Parsing must not install a second request-body buffer that can spill to disk.");
        Assert.IsTrue(body.CanRead, "The request owns its input stream after parsing.");
    }

    [TestMethod]
    public async Task Parse_CancelledRequest_PropagatesCancellationAndPreservesInputOwnership()
    {
        await using var body = new NonSeekableStream([0x80]);
        var request = new DefaultHttpContext().Request;
        request.Body = body;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            OtlpPayloadParser.ParseLogsRequestAsync(request, OtlpPayloadEncoding.Protobuf, cancellation.Token));

        Assert.AreSame(body, request.Body);
        Assert.IsTrue(body.CanRead);
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes, false)
    {
        public override bool CanSeek => false;
    }
}
