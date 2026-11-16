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
    [DataRow("0")]
    [DataRow("3601")]
    [DataRow("2.5")]
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
    [DataRow(null, "0x1000000", DisplayName = "The host's default gen0 budget")]
    [DataRow("0x800000", "0x800000", DisplayName = "A gen0 budget from the host's own environment")]
    public async Task Engine_Gen0Budget_IsTheHostsDefaultUnlessTheEnvironmentNamesOne(string? configured, string expected)
    {
        // The .NET runtime reads DOTNET_GCgen0size from the environment only, so what the engine process was
        // started with is what its garbage collector uses.
        await using var host = await McpHarness.StartAsync(TestData.HostBinary, false, Directory, context.CancellationToken,
            environment: new Dictionary<string, string?> { ["DOTNET_GCgen0size"] = configured });
        var engine = LinuxProcess.ChildOf(host.HostProcessId, TestData.HostBinary);
        var variables = (await File.ReadAllTextAsync($"/proc/{engine}/environ", context.CancellationToken)).Split('\0');
        Assert.AreSequenceEqual((string[])[$"DOTNET_GCgen0size={expected}"],
            variables.Where(static variable => variable.StartsWith("DOTNET_GCgen0size=", StringComparison.Ordinal)).ToArray());
    }
}
