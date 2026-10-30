using Grpc.Core;
using Grpc.Net.Client;
using Nami.Collector.Storage;
using Nami.Tests.Infrastructure;

namespace Nami.EndToEndTests;

[TestClass]
[TestCategory("EndToEnd")]
public sealed class NativeGrpcTests(TestContext context)
{
    [TestMethod]
    [Timeout(90000, CooperativeCancellation = true)]
    [DataRow("trace", "TraceService")]
    [DataRow("logs", "LogsService")]
    [DataRow("metrics", "MetricsService")]
    public async Task Export_MalformedProtobuf_ReturnsInvalidArgumentAndKeepsTheChannelUsable(string signal, string service)
    {
        var directory = context.TestTempDirectory!;
        await using (var host = await McpHarness.StartAsync(TestData.HostBinary, true, directory, context.CancellationToken))
        {
            using var channel = GrpcChannel.ForAddress($"http://127.0.0.1:{host.GrpcPort}");
            var method = ExportMethod(signal, service);

            var failure = await Assert.ThrowsExactlyAsync<RpcException>(() =>
                ExportAsync(channel, method, [0x80], false, context.CancellationToken));

            Assert.AreEqual(StatusCode.InvalidArgument, failure.StatusCode);
            Assert.AreEqual("The OTLP request contains invalid protobuf.", failure.Status.Detail);
            Assert.IsEmpty(await ExportAsync(channel, method, [], false, context.CancellationToken));
        }

        await using var store = new DuckDbStore(Path.Combine(directory, "nami.duckdb"));
        var snapshot = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
        Assert.IsEmpty(snapshot.Spans);
        Assert.IsEmpty(snapshot.Logs);
        Assert.IsEmpty(snapshot.Metrics);
    }

    [TestMethod]
    [Timeout(90000, CooperativeCancellation = true)]
    [DataRow(false, DisplayName = "Native gRPC identity limit")]
    [DataRow(true, DisplayName = "Native gRPC decompressed limit")]
    public async Task Export_ReceiveLimit_AcceptsBoundaryRejectsExcessAndRecovers(bool gzip)
    {
        const int Limit = 4 * 1024 * 1024;
        await using var host = await McpHarness.StartAsync(TestData.HostBinary, true,
            context.TestTempDirectory!, context.CancellationToken);
        using var channel = GrpcChannel.ForAddress($"http://127.0.0.1:{host.GrpcPort}");
        var method = ExportMethod("trace", "TraceService");
        Assert.IsEmpty(await ExportAsync(channel, method, UnknownFieldPayload(Limit - 1), gzip, context.CancellationToken));
        Assert.IsEmpty(await ExportAsync(channel, method, UnknownFieldPayload(Limit), gzip, context.CancellationToken));

        var failure = await Assert.ThrowsExactlyAsync<RpcException>(() =>
            ExportAsync(channel, method, UnknownFieldPayload(Limit + 1), gzip, context.CancellationToken));

        Assert.AreEqual(StatusCode.ResourceExhausted, failure.StatusCode);
        Assert.IsEmpty(await ExportAsync(channel, method, [], gzip, context.CancellationToken));
    }

    private static Method<byte[], byte[]> ExportMethod(string signal, string service)
    {
        var bytes = Marshallers.Create<byte[]>(static value => value, static value => value);
        return new Method<byte[], byte[]>(MethodType.Unary,
            $"opentelemetry.proto.collector.{signal}.v1.{service}", "Export", bytes, bytes);
    }

    private static async Task<byte[]> ExportAsync(GrpcChannel channel, Method<byte[], byte[]> method, byte[] bytes,
        bool gzip, CancellationToken cancellationToken)
    {
        var headers = new Metadata();
        if (gzip) headers.Add("grpc-internal-encoding-request", "gzip");
        using var call = channel.CreateCallInvoker().AsyncUnaryCall(method, null,
            new CallOptions(headers, cancellationToken: cancellationToken), bytes);
        return await call.ResponseAsync;
    }

    private static byte[] UnknownFieldPayload(int size)
    {
        var bytes = new byte[size];
        bytes[0] = 0xfa;
        bytes[1] = 0x07; // Unknown length-delimited field 127.
        var length = size - 6;
        for (var index = 0; index < 4; index++)
            bytes[index + 2] = (byte)((length >> (7 * index)) | (index < 3 ? 0x80 : 0));
        return bytes;
    }
}
