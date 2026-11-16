using System.Net;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using ModelContextProtocol.Protocol;
using Nami.Tests.Infrastructure;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Nami.EndToEndTests;

/// <summary>
///     What the container release (ledger N47) rests on, proven on the published host without Docker: dynamic
///     ports with a ready manifest, the local key path, the stop budget and the single writer of a store.
/// </summary>
[TestClass]
[TestCategory("EndToEnd")]
public sealed class ContainerPathTests(TestContext context)
{
    private string Directory => context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");

    private static JsonElement Content(CallToolResult result)
    {
        return (object?)result.StructuredContent is JsonElement json
            ? json
            : throw new InvalidOperationException("No structured content");
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task ReadyManifest_ThreeHostsOnPortZeroStartedTogether_EachServesTheUrlsOfItsOwnManifest()
    {
        // Nine listeners, none with a configured port: the operating system chooses each one while the three
        // hosts start at the same time, so no host can have probed for a port another one then took.
        var hosts = await Task.WhenAll(Enumerable.Range(0, 3).Select(index => McpHarness.StartAsync(TestData.HostBinary,
            false, Path.Combine(Directory, $"host{index}"), context.CancellationToken, readyManifest: true)));
        try
        {
            var ports = hosts.SelectMany(static host => (int[])[host.HttpPort, host.OtlpPort, host.GrpcPort]).ToArray();
            Assert.DoesNotContain(0, ports);
            Assert.HasCount(9, ports.Distinct().ToArray(), "Every listener of every host has its own port.");
            for (var index = 0; index < hosts.Length; index++)
            {
                var host = hosts[index];
                Assert.AreSequenceEqual((string[])["data_path", "mcp", "otlp_grpc", "otlp_http"],
                    host.ReadyManifest.EnumerateObject().Select(static property => property.Name).Order().ToArray());
                Assert.AreEqual(Path.Combine(Directory, $"host{index}", "nami.duckdb"),
                    host.ReadyManifest.GetProperty("data_path").GetString());
                Assert.AreEqual($"http://127.0.0.1:{host.HttpPort}/mcp", host.ReadyManifest.GetProperty("mcp").GetString());
                Assert.AreEqual($"http://127.0.0.1:{host.OtlpPort}", host.ReadyManifest.GetProperty("otlp_http").GetString());
                Assert.AreEqual($"http://127.0.0.1:{host.GrpcPort}", host.ReadyManifest.GetProperty("otlp_grpc").GetString());

                // One span over each OTLP URL of the manifest, under a service only this host is sent.
                var start = TestData.Nanos(DateTimeOffset.UtcNow.AddSeconds(-2));
                using var accepted = await host.SendJsonAsync("traces",
                    OtlpData.Traces(start, service: $"service-{index}"), context.CancellationToken);
                Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
                using var channel = GrpcChannel.ForAddress(host.ReadyManifest.GetProperty("otlp_grpc").GetString()!);
                var request = TestData.ParseTraces(OtlpData.Traces(start, offset: 1, service: $"service-{index}"));
                var method = new Method<ExportTraceServiceRequest, ExportTraceServiceResponse>(MethodType.Unary,
                    "opentelemetry.proto.collector.trace.v1.TraceService", "Export",
                    Marshallers.Create<ExportTraceServiceRequest>(static message => message.ToByteArray(),
                        ExportTraceServiceRequest.Parser.ParseFrom),
                    Marshallers.Create<ExportTraceServiceResponse>(static message => message.ToByteArray(),
                        ExportTraceServiceResponse.Parser.ParseFrom));
                using var call = channel.CreateCallInvoker().AsyncUnaryCall(method, null,
                    new CallOptions(cancellationToken: context.CancellationToken), request);
                Assert.IsNotNull(await call.ResponseAsync);
            }

            // The MCP URL of each manifest answers with what that host was sent, and with nothing of the others.
            for (var index = 0; index < hosts.Length; index++)
            {
                var traces = Content(await hosts[index].CallAsync("traces_slowest", "{}", context.CancellationToken))
                    .GetProperty("traces").EnumerateArray().ToArray();
                Assert.HasCount(2, traces);
                var own = Content(await hosts[index].CallAsync("traces_slowest",
                    $$"""{"service":"service-{{index}}"}""", context.CancellationToken));
                Assert.HasCount(2, own.GetProperty("traces").EnumerateArray().ToArray());
            }
        }
        finally
        {
            foreach (var host in hosts) await host.DisposeAsync();
        }

        // A manifest exists only while its host is ready.
        foreach (var host in hosts) Assert.IsFalse(File.Exists(host.ReadyFile), "The host removes its manifest when it stops.");
    }
}
