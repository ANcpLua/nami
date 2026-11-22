using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using ModelContextProtocol.Protocol;
using Nami.Collector.Storage;
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
    private Task<McpHarness> StartAsTheImageDoesAsync(string publicUrl, string bindAddress = "0.0.0.0")
    {
        return McpHarness.StartAsync(TestData.HostBinary, false, Directory, context.CancellationToken, Keys, ReadKey,
            new Dictionary<string, string?>
            {
                ["NAMI_BIND_ADDRESS"] = bindAddress, ["NAMI_PUBLIC_URL"] = publicUrl,
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

    private async Task<HttpStatusCode> GetHealthAsync(McpHarness host, string? origin = null, string? hostHeader = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{host.HttpPort}/health");
        if (origin is not null) request.Headers.Add("Origin", origin);
        if (hostHeader is not null) request.Headers.Host = hostHeader;
        using var response = await host.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.CancellationToken);
        return response.StatusCode;
    }

    [TestMethod]
    [Timeout(90000, CooperativeCancellation = true)]
    [DataRow("0.0.0.0", DisplayName = "Every interface, as the image binds")]
    [DataRow("0", DisplayName = "Every interface, written short")]
    public async Task WildcardBindAddress_IsNoHostAndNoOrigin(string bindAddress)
    {
        // A wildcard says where nami listens; it is not a name of this machine. A request that names it, as
        // Host or as the Origin of a browser page, is refused like any other foreign name.
        const string asHost = "0.0.0.0";
        await using var host = await StartAsTheImageDoesAsync("http://127.0.0.1:5100", bindAddress);
        Assert.AreEqual(HttpStatusCode.OK, await PostMcpAsync(host, ReadKey), "Precondition: the listener answers.");
        Assert.AreEqual(HttpStatusCode.Forbidden, await PostMcpAsync(host, ReadKey, hostHeader: $"{asHost}:{host.HttpPort}"));
        Assert.AreEqual(HttpStatusCode.Forbidden, await PostMcpAsync(host, ReadKey, hostHeader: asHost));
        Assert.AreEqual(HttpStatusCode.Forbidden, await PostMcpAsync(host, ReadKey, $"http://{asHost}:5100"));
        Assert.AreEqual(HttpStatusCode.Forbidden, await PostMcpAsync(host, ReadKey, $"http://{asHost}:{host.HttpPort}"));
        Assert.AreEqual(HttpStatusCode.Forbidden, await GetHealthAsync(host, hostHeader: $"{asHost}:{host.HttpPort}"));
        Assert.AreEqual(HttpStatusCode.Forbidden, await GetHealthAsync(host, $"http://{asHost}:{host.HttpPort}"));
        // The names of this machine still pass, as Host and as Origin.
        Assert.AreEqual(HttpStatusCode.OK, await PostMcpAsync(host, ReadKey, $"http://127.0.0.1:{host.HttpPort}", $"127.0.0.1:{host.HttpPort}"));
        Assert.AreEqual(HttpStatusCode.OK, await GetHealthAsync(host));
    }

    [TestMethod]
    [Timeout(90000, CooperativeCancellation = true)]
    public async Task Health_UnderRailwaysProbeHost_IsAnsweredAndNothingElseIs()
    {
        // Railway's deploy check asks GET /health with the Host healthcheck.railway.app, not with the service's
        // own name (https://docs.railway.com/deployments/healthchecks).
        const string Probe = "healthcheck.railway.app";
        await using var host = await StartAsTheImageDoesAsync("https://nami.example");
        Assert.AreEqual(HttpStatusCode.OK, await GetHealthAsync(host, hostHeader: Probe));
        Assert.AreEqual(HttpStatusCode.OK, await GetHealthAsync(host, hostHeader: $"{Probe}:{host.HttpPort}"));

        // Only that Host, only /health, only GET; the Origin is still judged.
        Assert.AreEqual(HttpStatusCode.Forbidden, await PostMcpAsync(host, ReadKey, hostHeader: Probe));
        Assert.AreEqual(HttpStatusCode.Forbidden, await GetHealthAsync(host, hostHeader: "evil.example"));
        Assert.AreEqual(HttpStatusCode.Forbidden, await GetHealthAsync(host, hostHeader: $"{Probe}.evil.example"));
        Assert.AreEqual(HttpStatusCode.Forbidden, await GetHealthAsync(host, "https://evil.example", Probe));
        foreach (var (method, path) in ((HttpMethod, string)[])[(HttpMethod.Post, "/health"), (HttpMethod.Get, "/mcp"),
                     (HttpMethod.Get, "/health/"), (HttpMethod.Get, "/")])
        {
            using var request = new HttpRequestMessage(method, $"http://127.0.0.1:{host.HttpPort}{path}");
            request.Headers.Host = Probe;
            request.Headers.Add("x-otlp-api-key", ReadKey);
            using var response = await host.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.CancellationToken);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, $"{method} {path} under the probe's Host.");
        }
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    [DataRow("https://0.0.0.0")]
    [DataRow("https://[::]:8443")]
    public async Task PublicUrl_WildcardAddress_RefusedAtStart(string publicUrl)
    {
        var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            await using var host = await StartAsTheImageDoesAsync(publicUrl);
        });
        Assert.Contains("NAMI_PUBLIC_URL names a wildcard address", failure.Message);
    }

    [TestMethod]
    [Timeout(90000, CooperativeCancellation = true)]
    [DataRow("https://nami.example", "https://nami.example", true, DisplayName = "The public origin itself")]
    [DataRow("https://nami.example", "https://nami.example:443", true, DisplayName = "The public origin with its default port written out")]
    [DataRow("https://nami.example", "http://nami.example", false, DisplayName = "The public host over plain HTTP")]
    [DataRow("https://nami.example", "https://nami.example:8443", false, DisplayName = "The public host on another port")]
    [DataRow("https://nami.example", "http://nami.example:443", false, DisplayName = "The public host and port over plain HTTP")]
    [DataRow("https://nami.example:8443", "https://nami.example:8443", true, DisplayName = "A public origin with a port")]
    [DataRow("https://nami.example:8443", "https://nami.example", false, DisplayName = "A public origin with a port, asked without it")]
    [DataRow("https://nami.example", "http://localhost:9999", true, DisplayName = "A page of this machine, on any port")]
    public async Task Origin_IsComparedBySchemeHostAndPort(string publicUrl, string origin, bool allowed)
    {
        await using var host = await StartAsTheImageDoesAsync(publicUrl);
        Assert.AreEqual(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, await PostMcpAsync(host, ReadKey, origin));
        Assert.AreEqual(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, await GetHealthAsync(host, origin));
        // The Host header stays a name without a port: a proxy in front may listen on any port.
        Assert.AreEqual(HttpStatusCode.OK, await PostMcpAsync(host, ReadKey, hostHeader: "nami.example"));
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

    /// <summary>The container's health probe as Docker runs it: the bundled file under Bun, in the container's environment.</summary>
    private Task<ProcessResult> RunHealthProbeAsync(IReadOnlyDictionary<string, string?> environment)
    {
        return ProcessRunner.RunAsync("bun",
            [Path.Combine(ProcessRunner.RepositoryRoot, "artifacts", "publish", "mcp", "healthcheck.js")], Directory,
            context.CancellationToken, environment);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    [DataRow("0")]
    [DataRow("00", DisplayName = "Zero written with two digits")]
    public async Task HealthProbe_PortOfTheSystemsChoice_IsReadFromTheReadyManifest(string port)
    {
        // The probe has the container's environment, so it sees the port as the host does: a system-chosen one,
        // however the zero is written, is in the ready manifest and nowhere else.
        await using var host = await McpHarness.StartAsync(TestData.HostBinary, false, Directory, context.CancellationToken,
            environment: new Dictionary<string, string?> { ["NAMI_PORT"] = port }, readyManifest: true);
        var probe = await RunHealthProbeAsync(new Dictionary<string, string?>
        {
            ["NAMI_PORT"] = port, ["PORT"] = null, ["NAMI_BIND_ADDRESS"] = null, ["NAMI_READY_FILE"] = host.ReadyFile
        });
        Assert.AreEqual(0, probe.ExitCode, probe.Error);

        // A probe that cannot know the port says so instead of asking a port nobody listens on.
        var blind = await RunHealthProbeAsync(new Dictionary<string, string?>
        {
            ["NAMI_PORT"] = port, ["PORT"] = null, ["NAMI_BIND_ADDRESS"] = null, ["NAMI_READY_FILE"] = null
        });
        Assert.AreEqual(1, blind.ExitCode);
        Assert.Contains("NAMI_READY_FILE", blind.Error);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task HealthProbe_ConfiguredPort_AsksThatPortAndFailsWhenNothingListens()
    {
        Dictionary<string, string?> environment;
        await using (var host = await McpHarness.StartAsync(TestData.HostBinary, false, Directory, context.CancellationToken))
        {
            environment = new Dictionary<string, string?>
            {
                ["NAMI_PORT"] = host.HttpPort.ToString(CultureInfo.InvariantCulture), ["PORT"] = null,
                ["NAMI_BIND_ADDRESS"] = null, ["NAMI_READY_FILE"] = null
            };
            var probe = await RunHealthProbeAsync(environment);
            Assert.AreEqual(0, probe.ExitCode, probe.Error);
        }

        var stopped = await RunHealthProbeAsync(environment);
        Assert.AreEqual(1, stopped.ExitCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(stopped.Error), "A failed probe says why.");
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task Start_PortOfTheSystemsChoiceWithoutAReadyManifest_Refused()
    {
        // Nobody could find such a listener, and the container's health probe would fail for as long as it ran.
        var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            await using var host = await McpHarness.StartAsync(TestData.HostBinary, false, Directory,
                context.CancellationToken, environment: new Dictionary<string, string?> { ["NAMI_PORT"] = "0" });
        });
        Assert.Contains("NAMI_PORT=0 leaves the port to the operating system; name a ready manifest", failure.Message);
    }

    private static Dictionary<string, string?> StopBudget(string seconds)
    {
        return new Dictionary<string, string?> { ["NAMI_STOP_BUDGET_SECONDS"] = seconds };
    }

    [TestMethod]
    [Timeout(90000, CooperativeCancellation = true)]
    [DataRow(2, DisplayName = "A budget below the old fixed 8 seconds")]
    [DataRow(12, DisplayName = "A budget above the old fixed 8 seconds")]
    public async Task Stop_EngineThatDoesNotEnd_IsKilledAtTheEndOfTheStopBudget(int budgetSeconds)
    {
        // The engine is stopped, so it sees neither the end of its input nor SIGTERM: only the host's SIGKILL at
        // the end of the budget ends it. Until this budget existed the kill came after 8 seconds, whatever the
        // grace period of whoever stopped nami.
        await using var host = await McpHarness.StartAsync(TestData.HostBinary, false, Directory,
            context.CancellationToken, environment: StopBudget(budgetSeconds.ToString(CultureInfo.InvariantCulture)));
        var owner = LinuxProcess.ParentOf(host.HostProcessId);
        var engine = LinuxProcess.ChildOf(host.HostProcessId, TestData.HostBinary);
        await LinuxProcess.SignalAsync(engine, "STOP", context.CancellationToken);
        var stopping = Stopwatch.StartNew();
        await LinuxProcess.SignalAsync(host.HostProcessId, "TERM", context.CancellationToken);

        // Not before the budget ends: one and a half seconds before it, host and engine are both still there.
        var early = TimeSpan.FromSeconds(budgetSeconds - 1.5) - stopping.Elapsed;
        Assert.IsFalse(await host.WaitForHostExitAsync(early, context.CancellationToken),
            $"The host ended {stopping.Elapsed.TotalSeconds:F1} s into a stop budget of {budgetSeconds} s.");
        Assert.Contains(engine, LinuxProcess.RunningIn(Directory, owner), "The engine keeps its whole budget.");
        // And not long after it.
        Assert.IsTrue(await host.WaitForHostExitAsync(TimeSpan.FromSeconds(5), context.CancellationToken),
            $"The host still ran {stopping.Elapsed.TotalSeconds:F1} s into a stop budget of {budgetSeconds} s.");
        await LinuxProcess.AssertNothingLeftInAsync(Directory, owner, TimeSpan.FromSeconds(5), context.CancellationToken);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task Stop_ReadyManifestThatCannotBeRemoved_StillStopsTheEngine()
    {
        await using var host = await McpHarness.StartAsync(TestData.HostBinary, false, Directory,
            context.CancellationToken, readyManifest: true);
        var owner = LinuxProcess.ParentOf(host.HostProcessId);
        // A directory where the manifest was: removing it as a file fails, also for root.
        File.Delete(host.ReadyFile!);
        System.IO.Directory.CreateDirectory(Path.Combine(host.ReadyFile!, "kept"));
        await LinuxProcess.SignalAsync(host.HostProcessId, "TERM", context.CancellationToken);

        // One signal is enough: the host goes on to stop its engine, and both end well inside the stop budget.
        Assert.IsTrue(await host.WaitForHostExitAsync(TimeSpan.FromSeconds(6), context.CancellationToken),
            "The host did not end after SIGTERM: a manifest it could not remove kept it from stopping its engine.");
        Assert.AreEqual(0, host.HostExitCode, "The stop itself was clean; the manifest is reported on standard error.");
        await LinuxProcess.AssertNothingLeftInAsync(Directory, owner, TimeSpan.FromSeconds(5), context.CancellationToken);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    [DataRow("0")]
    [DataRow("3601")]
    [DataRow("2.5")]
    [DataRow("0x10", DisplayName = "Hexadecimal, which only the host used to take")]
    [DataRow("1e1", DisplayName = "An exponent, which only the host used to take")]
    [DataRow("10.0", DisplayName = "A decimal point, which only the host used to take")]
    [DataRow("+10", DisplayName = "A sign, which both used to take")]
    [DataRow(" 10", DisplayName = "White space, which both used to take")]
    [DataRow("")]
    public async Task StopBudget_OutsideItsRange_RefusedAtStart(string budget)
    {
        var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            await using var host = await McpHarness.StartAsync(TestData.HostBinary, false, Directory,
                context.CancellationToken, environment: StopBudget(budget));
        });
        Assert.Contains("NAMI_STOP_BUDGET_SECONDS must be a whole number of seconds from 1 to 3600.", failure.Message);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    [DataRow("0", "must be between 1 and 3600")]
    [DataRow("3601", "must be between 1 and 3600")]
    [DataRow("0x10", "NAMI_STOP_BUDGET_SECONDS")]
    [DataRow("1e1", "NAMI_STOP_BUDGET_SECONDS")]
    [DataRow("10.0", "NAMI_STOP_BUDGET_SECONDS")]
    [DataRow("+10", "in decimal digits")]
    [DataRow(" 10", "in decimal digits")]
    [DataRow("", "in decimal digits")]
    public async Task StopBudget_EngineAlone_RefusesWhatTheHostRefuses(string budget, string reason)
    {
        // The engine without its host: the host refuses these values before it starts an engine, so only this
        // reaches the engine's own check. An engine that accepts the value starts, sees its input end and exits 0.
        System.IO.Directory.CreateDirectory(Directory);
        var engine = await ProcessRunner.RunAsync(TestData.HostBinary, ["--engine"], Directory, context.CancellationToken,
            new Dictionary<string, string?>
            {
                ["NAMI_STOP_BUDGET_SECONDS"] = budget, ["NAMI_DATA_PATH"] = Path.Combine(Directory, "nami.duckdb"),
                ["NAMI_OTLP_PORT"] = "0", ["NAMI_GRPC_PORT"] = "0", ["NAMI_OTLP_AUTH_MODE"] = "Unsecured",
                ["DOTNET_ENVIRONMENT"] = "Development"
            });
        Assert.AreNotEqual(0, engine.ExitCode, "The engine started with a stop budget the host refuses.");
        Assert.Contains(reason, engine.Error);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    [DataRow("Unsecured", "0.0.0.0", "Production", "NAMI_BIND_ADDRESS=127.0.0.1",
        DisplayName = "Unsecured on every interface, as the image binds")]
    [DataRow(null, null, "Development", "NAMI_OTLP_PROJECT_KEYS", DisplayName = "No mode, in the Development environment")]
    public async Task Unsecured_EngineAlone_RefusedOffLoopbackAndNeverTurnedOnByTheEnvironmentName(string? mode,
        string? address, string environment, string reason)
    {
        // The engine without its host, which refuses the first row before it starts an engine. Unsecured takes
        // requests without a key, so the engine accepts it on loopback alone and only when the mode names it: an
        // engine that accepts either row starts, sees its input end and exits 0.
        System.IO.Directory.CreateDirectory(Directory);
        var engine = await ProcessRunner.RunAsync(TestData.HostBinary, ["--engine"], Directory, context.CancellationToken,
            new Dictionary<string, string?>
            {
                ["NAMI_OTLP_AUTH_MODE"] = mode, ["NAMI_BIND_ADDRESS"] = address, ["NAMI_OTLP_PROJECT_KEYS"] = null,
                ["DOTNET_ENVIRONMENT"] = environment, ["ASPNETCORE_ENVIRONMENT"] = null,
                ["NAMI_DATA_PATH"] = Path.Combine(Directory, "nami.duckdb"),
                ["NAMI_OTLP_PORT"] = "0", ["NAMI_GRPC_PORT"] = "0"
            });
        Assert.AreNotEqual(0, engine.ExitCode, "The engine started without a key and without the loopback guard.");
        Assert.Contains(reason, engine.Error);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    [DataRow("0025", 25)]
    [DataRow("3600", 3600)]
    public async Task StopBudget_DecimalDigits_AcceptedByHostAndEngine(string budget, int seconds)
    {
        await using var host = await McpHarness.StartAsync(TestData.HostBinary, false, Directory,
            context.CancellationToken, environment: StopBudget(budget));
        using var health = await host.Http.GetAsync($"http://127.0.0.1:{host.HttpPort}/health", context.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, health.StatusCode, $"A stop budget of {seconds} seconds.");
    }

    [TestMethod]
    [Timeout(180000, CooperativeCancellation = true)]
    public async Task Stop_WhileExportsWaitForTheirWrite_LosesNoAcknowledgedExportAndEndsWithinTheBudget()
    {
        // Many exports at once: one is being written, the others wait in the store's write queue or for their
        // turn. The stop comes when the first of them has its answer. An export is acknowledged only after its
        // write, so whatever happens to the others (written late, refused with 503, or cut off for the sender to
        // retry), every export that got 200 must be in the store the stopped engine leaves behind.
        const int Exports = 120;
        const int SpansEach = 400;
        const int BudgetSeconds = 6;
        var start = TestData.Nanos(DateTimeOffset.UtcNow.AddSeconds(-2));
        var payloads = Enumerable.Range(0, Exports)
            .Select(index => OtlpData.Traces(start, SpansEach, index * SpansEach, service: "load")).ToArray();
        string dataPath;
        HttpStatusCode?[] answers;
        var stopping = new Stopwatch();
        int unansweredAtStop;
        await using (var host = await McpHarness.StartAsync(TestData.HostBinary, false, Directory, context.CancellationToken,
                         environment: StopBudget(BudgetSeconds.ToString(CultureInfo.InvariantCulture)), readyManifest: true))
        {
            dataPath = host.ReadyManifest.GetProperty("data_path").GetString()!;
            var owner = LinuxProcess.ParentOf(host.HostProcessId);
            var sends = payloads.Select(async payload =>
            {
                try
                {
                    using var response = await host.SendJsonAsync("traces", payload, context.CancellationToken);
                    return (HttpStatusCode?)response.StatusCode;
                }
                catch (HttpRequestException)
                {
                    // The connection ended without an answer: not acknowledged, the sender retries.
                    return null;
                }
            }).ToArray();
            var first = await Task.WhenAny(sends).WaitAsync(TimeSpan.FromSeconds(60), context.CancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, await first, "Precondition: the first answer is an acknowledgement.");
            unansweredAtStop = sends.Count(static send => !send.IsCompleted);
            stopping.Start();
            await LinuxProcess.SignalAsync(host.HostProcessId, "TERM", context.CancellationToken);
            answers = await Task.WhenAll(sends).WaitAsync(TimeSpan.FromSeconds(60), context.CancellationToken);
            Assert.IsTrue(await host.WaitForHostExitAsync(TimeSpan.FromSeconds(BudgetSeconds + 5), context.CancellationToken),
                "The host must end within its stop budget.");
            stopping.Stop();
            Assert.AreEqual(0, host.HostExitCode, "A stop on SIGTERM is a clean exit.");
            await LinuxProcess.AssertNothingLeftInAsync(Directory, owner, TimeSpan.FromSeconds(5), context.CancellationToken);
        }

        var acknowledged = answers.Count(static answer => answer == HttpStatusCode.OK);
        context.WriteLine($"{Exports} exports: {unansweredAtStop} unanswered when the stop began; {acknowledged} acknowledged, " +
                          $"{answers.Count(static answer => answer == HttpStatusCode.ServiceUnavailable)} refused with 503, " +
                          $"{answers.Count(static answer => answer is null)} cut off; the host ended after {stopping.Elapsed.TotalSeconds:F1} s.");
        Assert.IsGreaterThan(0, unansweredAtStop, "Precondition: exports were still waiting when the stop began.");
        foreach (var answer in answers)
            Assert.IsTrue(answer is null or HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable,
                $"An export that is not acknowledged is refused as retryable or cut off, but one was answered {answer}.");

        // The store opens (the engine released it) and holds every span of every acknowledged export.
        await using var store = new DuckDbStore(dataPath);
        var stored = (await store.ReadSnapshotAsync("default", ct: context.CancellationToken)).Spans
            .Select(static span => span.TraceId).ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < Exports; index++)
        {
            if (answers[index] != HttpStatusCode.OK) continue;
            var missing = Enumerable.Range(index * SpansEach + 1, SpansEach)
                .Count(id => !stored.Contains(id.ToString("x32", CultureInfo.InvariantCulture)));
            Assert.AreEqual(0, missing, $"Export {index} was acknowledged, but {missing} of its {SpansEach} spans are not stored.");
        }
    }

    [TestMethod]
    [Timeout(90000, CooperativeCancellation = true)]
    public async Task Store_SecondHostOnTheSameFile_DoesNotStartAndTheFirstKeepsServing()
    {
        // Two containers on one volume are two engines on one DuckDB file. The first holds the file's lock.
        var first = Path.Combine(Directory, "first");
        await using var host = await McpHarness.StartAsync(TestData.HostBinary, false, first, context.CancellationToken);
        var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            await using var second = await McpHarness.StartAsync(TestData.HostBinary, false,
                Path.Combine(Directory, "second"), context.CancellationToken,
                environment: new Dictionary<string, string?> { ["NAMI_DATA_PATH"] = Path.Combine(first, "nami.duckdb") });
        });
        Assert.StartsWith("Nami exited (", failure.Message);
        Assert.Contains("Could not set lock on file", failure.Message);

        using var accepted = await host.SendJsonAsync("traces",
            OtlpData.Traces(TestData.Nanos(DateTimeOffset.UtcNow.AddSeconds(-2))), context.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
        var traces = Content(await host.CallAsync("traces_slowest", "{}", context.CancellationToken));
        Assert.HasCount(1, traces.GetProperty("traces").EnumerateArray().ToArray());
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    [DataRow(null, null, "DOTNET_GCgen0size=0x1000000", DisplayName = "The host's default gen0 budget")]
    [DataRow("0x800000", null, "DOTNET_GCgen0size=0x800000", DisplayName = "A gen0 budget from the host's own environment")]
    [DataRow(null, "0x800000", "COMPlus_GCgen0size=0x800000", DisplayName = "A gen0 budget in the older COMPlus_ spelling")]
    [DataRow("", null, "DOTNET_GCgen0size=", DisplayName = "An empty gen0 budget: the runtime's own, without the host's default")]
    public async Task Engine_Gen0Budget_IsTheHostsDefaultUnlessTheEnvironmentNamesOne(string? dotnet, string? complus,
        string expected)
    {
        // The .NET runtime reads the setting from the environment only, and DOTNET_ before COMPlus_: what the
        // engine process was started with is what its garbage collector uses.
        await using var host = await McpHarness.StartAsync(TestData.HostBinary, false, Directory, context.CancellationToken,
            environment: new Dictionary<string, string?> { ["DOTNET_GCgen0size"] = dotnet, ["COMPlus_GCgen0size"] = complus });
        var engine = LinuxProcess.ChildOf(host.HostProcessId, TestData.HostBinary);
        var variables = (await File.ReadAllTextAsync($"/proc/{engine}/environ", context.CancellationToken)).Split('\0');
        Assert.AreSequenceEqual((string[])[expected],
            variables.Where(static variable => variable.Contains("_GCgen0size=", StringComparison.Ordinal)).ToArray());
    }
}
