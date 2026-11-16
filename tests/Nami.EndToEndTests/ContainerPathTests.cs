using System.Net;
using System.Text;
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
    // Not secrets: the two keys of the one project these tests configure, as a user of the local container would.
    private const string IngestKey = "fake-ingest-key-0001";
    private const string ReadKey = "fake-read-key-0001";

    private const string Keys =
        $$"""{"demo":[{"key":"{{IngestKey}}","roles":["ingest"]},{"key":"{{ReadKey}}","roles":["read"]}]}""";

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

    /// <summary>
    ///     The host as the image starts it: every interface, no auth mode set outside Development (so ApiKey), and
    ///     the public URL a user gives for <c>-p 127.0.0.1:5100:5100</c>. The listeners take ports of the system's
    ///     choice, so the port a client reaches is not the 5100 of the public URL, as with a dynamic host port.
    /// </summary>
    private Task<McpHarness> StartAsTheImageDoesAsync(string publicUrl)
    {
        return McpHarness.StartAsync(TestData.HostBinary, false, Directory, context.CancellationToken, Keys, ReadKey,
            new Dictionary<string, string?>
            {
                ["NAMI_BIND_ADDRESS"] = "0.0.0.0", ["NAMI_PUBLIC_URL"] = publicUrl,
                ["NAMI_OTLP_AUTH_MODE"] = null, ["DOTNET_ENVIRONMENT"] = "Production", ["NODE_ENV"] = "production"
            }, true);
    }

    private async Task<HttpStatusCode> PostMcpAsync(McpHarness host, string? key, string? origin = null, string? hostHeader = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{host.HttpPort}/mcp");
        request.Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json");
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (key is not null) request.Headers.Add("x-otlp-api-key", key);
        if (origin is not null) request.Headers.Add("Origin", origin);
        if (hostHeader is not null) request.Headers.Host = hostHeader;
        using var response = await host.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.CancellationToken);
        return response.StatusCode;
    }

    [TestMethod]
    [Timeout(90000, CooperativeCancellation = true)]
    public async Task LocalContainer_PublishedOnLoopback_KeyHostAndOriginDecideEveryRequest()
    {
        await using var host = await StartAsTheImageDoesAsync("http://127.0.0.1:5100");
        Assert.AreNotEqual(5100, host.HttpPort, "Precondition: the client's port is not the one in NAMI_PUBLIC_URL.");

        // The key decides: OTLP needs the ingest key, MCP the read key, and neither accepts the other's.
        var span = OtlpData.Traces(TestData.Nanos(DateTimeOffset.UtcNow.AddSeconds(-2)), project: "demo");
        using (var refused = await host.SendJsonAsync("traces", span, context.CancellationToken))
            Assert.AreEqual(HttpStatusCode.Unauthorized, refused.StatusCode);
        using (var accepted = await host.SendJsonAsync("traces", span, context.CancellationToken, IngestKey))
            Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, await PostMcpAsync(host, null));
        Assert.AreEqual(HttpStatusCode.Forbidden, await PostMcpAsync(host, IngestKey));
        Assert.AreEqual(HttpStatusCode.OK, await PostMcpAsync(host, ReadKey));
        var traces = Content(await host.CallAsync("traces_slowest", "{}", context.CancellationToken));
        Assert.HasCount(1, traces.GetProperty("traces").EnumerateArray().ToArray());

        // Origin (MCP, Streamable HTTP: a server validates it against DNS rebinding). A page of another site is
        // refused even with the right key; this machine's own origins pass on any port; no Origin is no browser.
        Assert.AreEqual(HttpStatusCode.Forbidden, await PostMcpAsync(host, ReadKey, "https://evil.example"));
        Assert.AreEqual(HttpStatusCode.Forbidden, await PostMcpAsync(host, ReadKey, "http://0.0.0.0:5100.evil.example"));
        Assert.AreEqual(HttpStatusCode.Forbidden, await PostMcpAsync(host, ReadKey, "null"));
        Assert.AreEqual(HttpStatusCode.OK, await PostMcpAsync(host, ReadKey, "http://127.0.0.1:5100"));
        Assert.AreEqual(HttpStatusCode.OK, await PostMcpAsync(host, ReadKey, $"http://localhost:{host.HttpPort}"));

        // Host: a name that was rebound to this machine is refused, whatever its port.
        Assert.AreEqual(HttpStatusCode.Forbidden, await PostMcpAsync(host, ReadKey, hostHeader: $"evil.example:{host.HttpPort}"));
        Assert.AreEqual(HttpStatusCode.OK, await PostMcpAsync(host, ReadKey, hostHeader: "localhost:5100"));
        // The checks come before the key: a refused origin learns nothing about keys.
        Assert.AreEqual(HttpStatusCode.Forbidden, await PostMcpAsync(host, null, "https://evil.example"));
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    [DataRow("http://nami.example", DisplayName = "Plain HTTP off this machine")]
    [DataRow("http://127.0.0.1.nami.example:5100", DisplayName = "A name that only starts like loopback")]
    public async Task PublicUrl_PlainHttpThatIsNotLoopback_RefusedAtStart(string publicUrl)
    {
        var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            await using var host = await StartAsTheImageDoesAsync(publicUrl);
        });
        Assert.Contains("NAMI_PUBLIC_URL must be an HTTPS origin, or an HTTP origin on localhost, 127.0.0.1 or [::1]", failure.Message);
    }
}
