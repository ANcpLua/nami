using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Grpc.Core;
using JetBrains.Annotations;

namespace Nami.IntegrationTests.Receiver;

[TestClass]
public sealed class TransportTests
{
    [UsedImplicitly] public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("unknown", OtlpReceiver.ProtobufMediaType, null, HttpStatusCode.NotFound, StatusCode.NotFound,
        OtlpReceiver.ProtobufMediaType)]
    [DataRow(Signals.Traces, "application/json", null, HttpStatusCode.UnsupportedMediaType, StatusCode.Unimplemented,
        "application/json")]
    [DataRow(Signals.Traces, null, null, HttpStatusCode.UnsupportedMediaType, StatusCode.Unimplemented,
        OtlpReceiver.ProtobufMediaType)]
    [DataRow(Signals.Traces, OtlpReceiver.ProtobufMediaType, "br", HttpStatusCode.UnsupportedMediaType,
        StatusCode.Unimplemented, OtlpReceiver.ProtobufMediaType)]
    public async Task HttpReceiver_InvalidRouteMediaTypeOrEncoding_ReturnsTheProtocolError(string signal, string? media,
        string? encoding, HttpStatusCode expected, StatusCode expectedCode, string errorMedia)
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        using var content = Content(media, encoding);
        using var response = await TelemetryLifecycle.Http.PostAsync(new Uri(receiver.Endpoint, "v1/" + signal),
            content, TestContext.CancellationToken);
        Assert.AreEqual(expected, response.StatusCode);
        var error = await Assert.That.HasOtlpErrorAsync(response, TestContext.CancellationToken, errorMedia);
        Assert.AreEqual((int)expectedCode, error.Code);
        Assert.IsFalse(string.IsNullOrWhiteSpace(error.Message));
        Assert.AreEqual(error.Message, Assert.ContainsSingle(receiver.Snapshot().Receipts).Error);
    }

    [TestMethod]
    public async Task HttpReceiver_ProtobufMediaTypeWithCharset_IsAccepted()
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        using var content = Content("application/x-protobuf; charset=utf-8", null);
        using var response = await TelemetryLifecycle.Http.PostAsync(new Uri(receiver.Endpoint, "v1/" + Signals.Traces),
            content, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    [DataRow("application/x-protobuf ; charset=utf-8")]
    [DataRow("application/x-protobuf\t;charset=utf-8")]
    public async Task HttpReceiver_MediaTypeWithWhitespaceBeforeItsParameter_IsAccepted(string contentType)
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        using var connection = new TcpClient();
        await connection.ConnectAsync(receiver.Endpoint.Host, receiver.Endpoint.Port, TestContext.CancellationToken);
        var stream = connection.GetStream();
        var unnormalizedRequestHead = $"POST /v1/traces HTTP/1.1\r\nHost: {receiver.Endpoint.Authority}\r\n"
                                      + $"Content-Type: {contentType}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(unnormalizedRequestHead), TestContext.CancellationToken);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        Assert.AreEqual("HTTP/1.1 200 OK", await reader.ReadLineAsync(TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task HttpReceiver_InvalidGzip_ReturnsBadRequestWithItsErrorRecord()
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        using var response = await OtlpHttp.PostAsync(receiver.Endpoint, [1, 2, 3], TestContext.CancellationToken,
            Signals.Logs, "gzip");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var receipt = Assert.ContainsSingle(receiver.Snapshot().Receipts);
        Assert.AreEqual(Signals.Logs, receipt.Signal);
        Assert.IsFalse(string.IsNullOrEmpty(receipt.Error));
        Assert.AreEqual(0L, receipt.Accepted);
        var error = await Assert.That.HasOtlpErrorAsync(response, TestContext.CancellationToken);
        Assert.AreEqual((int)StatusCode.InvalidArgument, error.Code);
        Assert.AreEqual(receipt.Error, error.Message);
    }

    private static ByteArrayContent Content(string? media, string? encoding)
    {
        var content = new ByteArrayContent([]);
        content.Headers.ContentType = media is null ? null : MediaTypeHeaderValue.Parse(media);
        if (encoding is not null) content.Headers.ContentEncoding.Add(encoding);
        return content;
    }
}
