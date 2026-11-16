using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Nami.Api.Contracts;
using Nami.Api.Contracts.Engine;
using Nami.Tests;
using Nami.Tests.Infrastructure;

namespace Nami.EndToEndTests;

/// <summary>
///     Channel separation of the published stack over stdio (requirement D6). nami has three text channels: MCP on
///     the host's standard output, the private frames between the host and its engine, and diagnostics on
///     standard error, which both processes share. Whatever is written as a diagnostic must reach neither of
///     the first two.
/// </summary>
[TestClass]
[TestCategory("EndToEnd")]
public sealed class ChannelSeparationTests(TestContext context)
{
    private const int Rounds = 10;

    private string Directory => context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task Stdio_WhileHostAndEngineWriteDiagnostics_EveryMcpFrameAndEveryPrivateFrameIsWhole()
    {
        System.IO.Directory.CreateDirectory(Directory);
        // The engine behind a tap: the host starts this script in place of the engine, and the script records
        // both directions of the private channel on their way through. Standard error and the listeners pipe
        // (descriptor 3) pass to the engine untouched.
        var toEngine = Path.Combine(Directory, "to-engine.jsonl");
        var fromEngine = Path.Combine(Directory, "from-engine.jsonl");
        var tap = Path.Combine(Directory, "engine-tap.sh");
        await File.WriteAllTextAsync(tap,
            "#!/bin/sh\ntee \"$NAMI_TAP_TO_ENGINE\" | \"$NAMI_TAP_ENGINE\" \"$@\" | tee \"$NAMI_TAP_FROM_ENGINE\"\n",
            context.CancellationToken);
        File.SetUnixFileMode(tap, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var otlpPort = FreePort();
        var start = new ProcessStartInfo("bun")
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, WorkingDirectory = Directory,
            StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false)
        };
        start.ArgumentList.Add(Path.Combine(ProcessRunner.RepositoryRoot, "artifacts", "publish", "mcp", "main.js"));
        start.ArgumentList.Add("--engine");
        start.ArgumentList.Add(tap);
        start.ArgumentList.Add("--stdio");
        start.Environment["NAMI_TAP_ENGINE"] = TestData.HostBinary;
        start.Environment["NAMI_TAP_TO_ENGINE"] = toEngine;
        start.Environment["NAMI_TAP_FROM_ENGINE"] = fromEngine;
        start.Environment["NAMI_DATA_PATH"] = Path.Combine(Directory, "nami.duckdb");
        start.Environment["NAMI_OTLP_PORT"] = otlpPort.ToString(CultureInfo.InvariantCulture);
        start.Environment["NAMI_GRPC_PORT"] = FreePort().ToString(CultureInfo.InvariantCulture);
        start.Environment["NAMI_OTLP_AUTH_MODE"] = "Unsecured";
        start.Environment["DOTNET_ENVIRONMENT"] = "Development";
        // Diagnostics of the engine. A warning at every start: Kestrel says that it overrides this address (the
        // runtime image set it until the Dockerfile cleared it). Noise: the engine's log from Information up,
        // several lines for every request it receives. Failures that get logged: the exports below.
        start.Environment["ASPNETCORE_HTTP_PORTS"] = "8080";
        start.Environment["Logging__LogLevel__Default"] = "Information";

        var stdout = new ConcurrentQueue<string>();
        var stderr = new ConcurrentQueue<string>();
        int exitCode;
        using var http = new HttpClient();
        var host = await OwnedProcess.StartAsync(start, context.CancellationToken);
        var readOutput = Task.Run(async () =>
        {
            while (await host.StandardOutput.ReadLineAsync(CancellationToken.None) is { } line) stdout.Enqueue(line);
        });
        var readErrors = Task.Run(async () =>
        {
            while (await host.StandardError.ReadLineAsync(CancellationToken.None) is { } line) stderr.Enqueue(line);
        });
        try
        {
            async Task SendAsync(string line)
            {
                await host.StandardInput.BaseStream.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), context.CancellationToken);
                await host.StandardInput.BaseStream.FlushAsync(context.CancellationToken);
            }

            async Task WaitForAnswersAsync(IReadOnlyCollection<long> ids, TimeSpan deadline)
            {
                var waited = Stopwatch.StartNew();
                while (!ids.All(id => stdout.Any(line => line.Contains($"\"id\":{id}", StringComparison.Ordinal))))
                {
                    Assert.IsFalse(host.HasExited, "The host ended: " + string.Join(" | ", stderr.TakeLast(5)));
                    Assert.IsLessThan(deadline, waited.Elapsed,
                        $"No answer for every one of the requests {string.Join(", ", ids)}; standard error ends with: " +
                        string.Join(" | ", stderr.TakeLast(5)));
                    await Task.Delay(TimeSpan.FromMilliseconds(25), context.CancellationToken);
                }
            }

            await SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"channel-separation","version":"1"}}}""");
            await WaitForAnswersAsync([1], TimeSpan.FromSeconds(60));
            await SendAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");

            // Each round: two tool calls (one the engine answers with a result, one it refuses), two lines the
            // host cannot serve and reports on standard error, and two exports the engine cannot store and logs.
            var expected = new List<long> { 1 };
            for (var round = 0; round < Rounds; round++)
            {
                var call = 100 + round * 2;
                expected.AddRange([call, call + 1]);
                await SendAsync($$"""{"jsonrpc":"2.0","id":{{call}},"method":"tools/call","params":{"name":"sources_status","arguments":{}}}""");
                await SendAsync("warn: this line is no JSON, and says so the way a log line would");
                await SendAsync($$"""{"jsonrpc":"2.0","id":{{call + 1}},"method":"tools/call","params":{"name":"traces_slowest","arguments":{"limit":-5}}}""");
                await SendAsync("""{"id":"no-version","method":"tools/list"}""");
                using var noJson = new StringContent("""{"resourceSpans":[{"scopeSpans":[{"spans":[{"traceId":"zz"}]}]}]}""",
                    Encoding.UTF8, "application/json");
                using var refusedJson = await http.PostAsync($"http://127.0.0.1:{otlpPort}/v1/traces", noJson, context.CancellationToken);
                Assert.AreEqual(HttpStatusCode.BadRequest, refusedJson.StatusCode);
                using var noProtobuf = new ByteArrayContent("neither protobuf nor anything else"u8.ToArray());
                noProtobuf.Headers.ContentType = new("application/x-protobuf");
                using var refusedProtobuf = await http.PostAsync($"http://127.0.0.1:{otlpPort}/v1/traces", noProtobuf, context.CancellationToken);
                Assert.AreEqual(HttpStatusCode.BadRequest, refusedProtobuf.StatusCode);
            }

            await WaitForAnswersAsync(expected, TimeSpan.FromSeconds(60));
            // The end of MCP input stops the host, and the host stops the engine.
            await host.StandardInput.DisposeAsync();
            using var stopped = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            stopped.CancelAfter(TimeSpan.FromSeconds(30));
            await host.WaitForExitAsync(stopped.Token);
            exitCode = host.ExitCode;
        }
        finally
        {
            // The ownership host keeps the pipes open; disposing it ends them and every descendant.
            await host.DisposeAsync();
            await Task.WhenAll(readOutput, readErrors).WaitAsync(TimeSpan.FromSeconds(10), context.CancellationToken);
        }

        var errors = string.Join('\n', stderr);
        Assert.AreEqual(0, exitCode, "The host must end cleanly when its input ends. Standard error: " + errors);

        // Precondition: the diagnostics were written, by both processes, while the frames below were exchanged.
        Assert.Contains("warn: Microsoft.AspNetCore.Server.Kestrel", errors, "The engine's warning at start.");
        Assert.IsGreaterThan(Rounds * 2 - 1, stderr.Count(static line => line.StartsWith("warn: OtlpTracesEndpoint", StringComparison.Ordinal)),
            "The engine logs every export it could not store.");
        Assert.IsGreaterThan(Rounds * 4 - 1, stderr.Count(static line => line.StartsWith("info: ", StringComparison.Ordinal)),
            "The engine's log from Information up.");
        Assert.IsGreaterThan(Rounds - 1, stderr.Count(static line => line.Contains("\"jsonrpc\"", StringComparison.Ordinal)),
            "The host reports each line without a JSON-RPC version; its report is itself shaped like JSON.");

        // MCP: every line the host wrote to standard output is one JSON-RPC message, and every request was
        // answered exactly once.
        Assert.IsNotEmpty(stdout);
        var answers = new List<long>();
        foreach (var line in stdout)
        {
            JsonRpcMessage? message = null;
            try
            {
                message = JsonSerializer.Deserialize<JsonRpcMessage>(line, McpJsonUtilities.DefaultOptions);
            }
            catch (JsonException error)
            {
                Assert.Fail($"A line on the host's standard output is no MCP message ({error.Message}): {line}");
            }

            Assert.IsNotNull(message, "A line on the host's standard output is no MCP message: " + line);
            using var frame = JsonDocument.Parse(line);
            Assert.AreEqual("2.0", frame.RootElement.GetProperty("jsonrpc").GetString(), line);
            if (message is JsonRpcResponse or JsonRpcError) answers.Add(frame.RootElement.GetProperty("id").GetInt64());
        }

        Assert.AreSequenceEqual(expected.Order().ToArray(), answers.Order().ToArray());

        // The private channel: every line in either direction is one frame of the contract.
        var commands = await File.ReadAllTextAsync(toEngine, context.CancellationToken);
        var replies = await File.ReadAllTextAsync(fromEngine, context.CancellationToken);
        Assert.IsTrue(commands.EndsWith('\n'), "The last frame to the engine is whole.");
        Assert.IsTrue(replies.EndsWith('\n'), "The last frame from the engine is whole.");
        var called = new List<string>();
        foreach (var line in commands.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var frame = ParseFrame(line, "to the engine");
            Assert.AreEqual(ContractRevision.Value, frame.RootElement.GetProperty("revision").GetString(), line);
            Assert.AreEqual("call", frame.RootElement.GetProperty("method").GetString(), line);
            Assert.Contains(frame.RootElement.GetProperty("name").GetString(), (string[])["sources_status", "traces_slowest"], line);
            called.Add(frame.RootElement.GetProperty("id").GetString()!);
        }

        var answered = new List<string>();
        foreach (var line in replies.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            EngineResponse? reply = null;
            try
            {
                reply = JsonSerializer.Deserialize(line, ContractTypes.TypeInfo<EngineResponse>());
            }
            catch (JsonException error)
            {
                Assert.Fail($"A line from the engine is no frame ({error.Message}): {line}");
            }

            Assert.IsNotNull(reply, "A line from the engine is no frame: " + line);
            Assert.AreEqual(ContractRevision.Value, reply.Revision, line);
            answered.Add(reply.Id);
        }

        Assert.HasCount(Rounds * 2, called, "One frame to the engine for every tool call.");
        Assert.AreSequenceEqual(called.Append("ready").Order(StringComparer.Ordinal).ToArray(),
            answered.Order(StringComparer.Ordinal).ToArray(), "One frame from the engine at start and one for every call.");
        context.WriteLine($"{stdout.Count} MCP frames, {called.Count} frames to the engine, {answered.Count} from it, " +
                          $"{stderr.Count} lines of diagnostics.");
    }

    private static JsonDocument ParseFrame(string line, string direction)
    {
        JsonDocument? frame = null;
        try
        {
            frame = JsonDocument.Parse(line);
        }
        catch (JsonException error)
        {
            Assert.Fail($"A line {direction} is no frame ({error.Message}): {line}");
        }

        Assert.AreEqual(JsonValueKind.Object, frame.RootElement.ValueKind, $"A line {direction} is no frame: {line}");
        return frame;
    }
}
