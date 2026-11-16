using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Nami.Api.Contracts;
using Nami.Tests;
using Nami.Tests.Infrastructure;

namespace Nami.EndToEndTests;

/// <summary>
///     The published engine's private protocol over its real standard input and output, fed the frames
///     <c>src/nami.mcp/src/engine.ts</c> writes. The claims and the counterexamples these tests reproduce are in
///     <c>eng/proofs/NamiProofs/HostEngine</c> (<c>Spec.lean</c>, <c>Proofs/Engine.lean</c>, <c>Proofs/System.lean</c>).
/// </summary>
[TestClass]
[TestCategory("EndToEnd")]
public sealed class EnginePipeTests(TestContext context)
{
    private static string Call(string id, string tool = "sources_status", string input = "{}", string? revision = null) =>
        $"{{\"id\":\"{id}\",\"revision\":\"{revision ?? ContractRevision.Value}\",\"method\":\"call\",\"name\":\"{tool}\"," +
        $"\"input\":{input},\"credential\":null,\"traceparent\":null,\"tracestate\":null}}\n";

    private static string Health(string id) =>
        $"{{\"id\":\"{id}\",\"revision\":\"{ContractRevision.Value}\",\"method\":\"health\"}}\n";

    private static string Cancel(string id) =>
        $"{{\"id\":\"{id}\",\"revision\":\"{ContractRevision.Value}\",\"method\":\"cancel\"}}\n";

    private string Directory => context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task EngineProtocol_MalformedFrame_DoesNotCancelInFlightCalls()
    {
        // P14: one read with a valid call, a line that is not JSON, and a valid call. Proofs/Engine.lean proves
        // badFrameNotFatal and badFrameKeepsOtherCalls for the engine as it is; Mutants.lean refutes both for the
        // engine at 843383e (mutant_badFrameNotFatal, mutant_badFrameKeepsOtherCalls).
        await using var engine = await EngineProcess.StartAsync(Directory, context.CancellationToken);
        await engine.WriteAsync(Call("1") + "not-json\n" + Call("2"), context.CancellationToken);
        var replies = await engine.ReadRepliesAsync(["1", "2"], TimeSpan.FromSeconds(20), context.CancellationToken);
        Assert.IsTrue(replies.ContainsKey("1") && replies.ContainsKey("2"),
            $"Both calls around the invalid frame must be answered; answered [{string.Join(", ", replies.Keys)}]; {await engine.DescribeAsync()}");
        await engine.AssertServesAsync("3", context.CancellationToken);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task EngineProtocol_WrongRevision_RefusesThatCallBeforeDispatch_AndKeepsServing()
    {
        await using var engine = await EngineProcess.StartAsync(Directory, context.CancellationToken);
        await engine.WriteAsync(Call("1", revision: "sha256:0") + Call("2"), context.CancellationToken);
        var replies = await engine.ReadRepliesAsync(["1", "2"], TimeSpan.FromSeconds(20), context.CancellationToken);
        Assert.IsTrue(replies.TryGetValue("1", out var refused),
            $"A frame with a wrong revision must be refused for that id; answered [{string.Join(", ", replies.Keys)}]; {await engine.DescribeAsync()}");
        Assert.IsTrue(refused.GetProperty("is_error").GetBoolean(), "The refusal must be an error reply.");
        Assert.IsTrue(replies.TryGetValue("2", out var next) && !next.GetProperty("is_error").GetBoolean(),
            "The call after the refused frame must be answered.");
        await engine.AssertServesAsync("3", context.CancellationToken);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task EngineProtocol_DuplicateId_RefusedWithoutEndingOthers()
    {
        await using var engine = await EngineProcess.StartAsync(Directory, context.CancellationToken);
        await engine.WriteAsync(Call("1") + Call("1") + Call("2"), context.CancellationToken);
        var replies = await engine.ReadRepliesAsync(["1", "2"], TimeSpan.FromSeconds(20), context.CancellationToken);
        Assert.IsTrue(replies.ContainsKey("1") && replies.ContainsKey("2"),
            $"A duplicate id must not end the other calls; answered [{string.Join(", ", replies.Keys)}]; {await engine.DescribeAsync()}");
        await engine.AssertServesAsync("3", context.CancellationToken);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task EngineProtocol_FragmentedAndCoalescedReads_KeepRequestIdentity()
    {
        // Spec.ReadsKeepFrameIdentity (proved): frames cut across writes, also inside a two-byte character,
        // and several frames in one write. Each reply proves the engine read up to that frame, so the rest
        // of the same write waits in its buffer as a partial frame.
        await using var engine = await EngineProcess.StartAsync(Directory, context.CancellationToken);
        var a = Encoding.UTF8.GetBytes(Health("a"));
        var b = Encoding.UTF8.GetBytes(Call("b", input: "{\"cursor\":\"grüße\"}"));
        var c = Encoding.UTF8.GetBytes(Call("c"));
        var d = Encoding.UTF8.GetBytes(Health("d"));
        var inside = Array.IndexOf(b, (byte)0xC3) + 1; // between the two bytes of "ü"
        Assert.IsGreaterThan(0, inside);
        await engine.WriteAsync([.. a, .. b[..inside]], context.CancellationToken);
        var first = await engine.ReadRepliesAsync(["a"], TimeSpan.FromSeconds(20), context.CancellationToken);
        await engine.WriteAsync([.. b[inside..], .. c, .. d[..7]], context.CancellationToken);
        var second = await engine.ReadRepliesAsync(["b", "c"], TimeSpan.FromSeconds(20), context.CancellationToken);
        await engine.WriteAsync(d[7..], context.CancellationToken);
        var third = await engine.ReadRepliesAsync(["d"], TimeSpan.FromSeconds(20), context.CancellationToken);
        var answered = first.Concat(second).Concat(third).ToDictionary();
        string[] ids = ["a", "b", "c", "d"];
        Assert.AreSequenceEqual(ids, [.. answered.Keys.Order(StringComparer.Ordinal)], await engine.DescribeAsync());
        Assert.IsFalse(answered["a"].GetProperty("is_error").GetBoolean());
        Assert.AreEqual("invalid_argument", answered["b"].GetProperty("result").GetProperty("code").GetString(),
            "The cursor must arrive intact and be judged as a cursor.");
        Assert.IsFalse(answered["c"].GetProperty("is_error").GetBoolean());
        Assert.IsFalse(answered["d"].GetProperty("is_error").GetBoolean());
        Assert.AreEqual(0, await engine.EndInputAndWaitAsync(TimeSpan.FromSeconds(30), context.CancellationToken),
            "End of input must end the engine cleanly.");
    }

    [TestMethod]
    [Timeout(180000, CooperativeCancellation = true)]
    public async Task EngineProtocol_CancelThenNewCalls_WithinTheHostLimit_KeepServing()
    {
        // Proofs/System.lean, correctHostNeverEndsTheEngine: engine.ts frees a slot when its caller aborts
        // (abort in Engine.request) and allows 32 waiting requests (ENGINE_LIMITS.max_pending). The engine frees
        // its slot when it reads the cancel frame (EngineProtocol.Accept), although a tool's work ignores
        // cancellation once it runs (ToolDispatcher.Run) and so goes on; a call at a full table is refused for
        // its id and the read loop goes on. At 843383e the slot stayed taken until the cancelled task had ended
        // and a call at a full table ended the engine (Mutants.lean, mutant_correctHost). The test sends what
        // engine.ts sends: call 1, its cancel, then 32 new calls.
        await using var engine = await EngineProcess.StartAsync(Directory, context.CancellationToken);
        const string Trace = "00000000000000000000000000000001";
        await engine.SeedTraceAsync(Trace, 20_000, context.CancellationToken);
        var slow = $"{{\"trace_id\":\"{Trace}\",\"max_bytes\":4096}}";
        // DuckDB work runs on one writer: call 2's snapshot starts only after call 1's snapshot is done.
        // When call 2 answers, call 1 has left its snapshot and is in ToolDispatcher.Run.
        await engine.WriteAsync(Call("1", "traces_get", slow) + Call("2"), context.CancellationToken);
        var setup = await engine.ReadRepliesAsync(["2"], TimeSpan.FromSeconds(120), context.CancellationToken);
        Assert.IsTrue(setup.ContainsKey("2"), await engine.DescribeAsync());
        Assert.IsFalse(setup.ContainsKey("1"), "Precondition: call 1 must still run; seed a larger trace.");
        var burst = new StringBuilder(Cancel("1"));
        for (var id = 100; id < 132; id++) burst.Append(Call(id.ToString(CultureInfo.InvariantCulture)));
        await engine.WriteAsync(burst.ToString(), context.CancellationToken);
        var expected = Enumerable.Range(100, 32).Select(static id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
        var replies = await engine.ReadRepliesAsync(expected, TimeSpan.FromSeconds(120), context.CancellationToken);
        var answered = expected.Count(replies.ContainsKey);
        Assert.AreEqual(32, answered,
            $"All 32 new calls are within the host's limit and must be answered; answered {answered}; {await engine.DescribeAsync()}");
        await engine.AssertServesAsync("200", context.CancellationToken);
    }

    /// <summary>The published engine started with <c>--engine</c>, as engine.ts starts it.</summary>
    private sealed class EngineProcess : IAsyncDisposable
    {
        private readonly ConcurrentQueue<string> _errors = new();
        private readonly HttpClient _http = new();
        private readonly int _otlpPort;
        private readonly OwnedProcess _process;
        private readonly Task _stderr;
        private Task<string?>? _line;

        private EngineProcess(OwnedProcess process, int otlpPort)
        {
            _process = process;
            _otlpPort = otlpPort;
            _stderr = Task.Run(async () =>
            {
                while (await process.StandardError.ReadLineAsync(CancellationToken.None) is { } line) _errors.Enqueue(line);
            });
        }

        public async ValueTask DisposeAsync()
        {
            _http.Dispose();
            // The ownership host keeps the engine's pipes open; disposing it ends both and every descendant.
            await _process.DisposeAsync();
            try
            {
                await _stderr.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                // A descendant kept the pipe open past its owner; the test's verdict stands without the rest.
            }
        }

        private static int FreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        internal static async Task<EngineProcess> StartAsync(string directory, CancellationToken cancellationToken)
        {
            System.IO.Directory.CreateDirectory(directory);
            var otlpPort = FreePort();
            var start = new ProcessStartInfo(TestData.HostBinary)
            {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true, WorkingDirectory = directory, StandardOutputEncoding = new UTF8Encoding(false)
            };
            start.ArgumentList.Add("--engine");
            start.Environment["NAMI_DATA_PATH"] = Path.Combine(directory, "nami.duckdb");
            start.Environment["NAMI_OTLP_PORT"] = otlpPort.ToString(CultureInfo.InvariantCulture);
            start.Environment["NAMI_GRPC_PORT"] = FreePort().ToString(CultureInfo.InvariantCulture);
            start.Environment["NAMI_PORT"] = FreePort().ToString(CultureInfo.InvariantCulture);
            start.Environment["NAMI_OTLP_AUTH_MODE"] = "Unsecured";
            start.Environment["DOTNET_ENVIRONMENT"] = "Development";
            var engine = new EngineProcess(await OwnedProcess.StartAsync(start, cancellationToken), otlpPort);
            try
            {
                var ready = await engine.ReadFrameAsync(TimeSpan.FromSeconds(60), cancellationToken)
                            ?? throw new InvalidOperationException("The engine ended before it was ready: " + await engine.DescribeAsync());
                Assert.AreEqual("ready", ready.GetProperty("id").GetString());
                return engine;
            }
            catch
            {
                await engine.DisposeAsync();
                throw;
            }
        }

        internal async Task WriteAsync(string frames, CancellationToken cancellationToken)
        {
            await WriteAsync(Encoding.UTF8.GetBytes(frames), cancellationToken);
        }

        /// <summary>One write on the engine's standard input.</summary>
        internal async Task WriteAsync(byte[] bytes, CancellationToken cancellationToken)
        {
            var stream = _process.StandardInput.BaseStream;
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        /// <summary>The next reply frame; null once the engine has exited and its output is drained, or at the deadline.</summary>
        private async Task<JsonElement?> ReadFrameAsync(TimeSpan deadline, CancellationToken cancellationToken)
        {
            // The ownership host keeps the output pipe open after the engine exits, so its exit, not the
            // end of the output, ends the reading. A read in progress stays for the next call.
            _line ??= _process.StandardOutput.ReadLineAsync(CancellationToken.None).AsTask();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(deadline);
            var exited = _process.WaitForExitAsync(timeout.Token);
            if (await Task.WhenAny(_line, exited) != _line)
            {
                try
                {
                    await exited;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                // Lines the engine wrote before it exited are already in the pipe.
                if (await Task.WhenAny(_line, Task.Delay(TimeSpan.FromSeconds(2), cancellationToken)) != _line) return null;
            }

            var line = await _line;
            _line = null;
            if (line is null) return null;
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }

        /// <summary>Replies by id until every expected id answered, the output ended, or the deadline passed.</summary>
        internal async Task<Dictionary<string, JsonElement>> ReadRepliesAsync(IReadOnlyCollection<string> expected,
            TimeSpan deadline, CancellationToken cancellationToken)
        {
            var replies = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            var until = TimeProvider.System.GetTimestamp() + (long)(deadline.TotalSeconds * TimeProvider.System.TimestampFrequency);
            while (!expected.All(replies.ContainsKey))
            {
                var left = TimeProvider.System.GetElapsedTime(TimeProvider.System.GetTimestamp(), until);
                if (TimeProvider.System.GetTimestamp() >= until) break;
                JsonElement? frame;
                try
                {
                    frame = await ReadFrameAsync(left, cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (frame is not { } reply) break;
                replies.TryAdd(reply.GetProperty("id").GetString()!, reply);
            }

            return replies;
        }

        internal async Task AssertServesAsync(string id, CancellationToken cancellationToken)
        {
            await WriteAsync(Health(id), cancellationToken);
            var replies = await ReadRepliesAsync([id], TimeSpan.FromSeconds(20), cancellationToken);
            Assert.IsTrue(replies.TryGetValue(id, out var reply) && !reply.GetProperty("is_error").GetBoolean(),
                "The engine must keep serving: " + await DescribeAsync());
        }

        /// <summary>Closes the engine's input and returns its exit code.</summary>
        internal async Task<int> EndInputAndWaitAsync(TimeSpan deadline, CancellationToken cancellationToken)
        {
            await _process.StandardInput.DisposeAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(deadline);
            await _process.WaitForExitAsync(timeout.Token);
            return _process.ExitCode;
        }

        internal Task<string> DescribeAsync()
        {
            if (!_process.HasExited) return Task.FromResult("the engine is running");
            return Task.FromResult($"the engine exited with code {_process.ExitCode}: {string.Join(" | ", _errors.Take(2))}");
        }

        /// <summary>One trace of <paramref name="spans" /> spans through the engine's OTLP/HTTP JSON endpoint.</summary>
        internal async Task SeedTraceAsync(string traceId, int spans, CancellationToken cancellationToken)
        {
            var now = TestData.Nanos(TimeProvider.System.GetUtcNow().AddMinutes(-1));
            for (var offset = 0; offset < spans; offset += 4000)
            {
                var batch = new StringBuilder("{\"resourceSpans\":[{\"resource\":{\"attributes\":[{\"key\":\"service.name\",\"value\":{\"stringValue\":\"pipe\"}}]},\"scopeSpans\":[{\"scope\":{\"name\":\"pipe\"},\"spans\":[");
                for (var index = offset; index < Math.Min(spans, offset + 4000); index++)
                {
                    if (index > offset) batch.Append(',');
                    batch.Append(CultureInfo.InvariantCulture,
                        $"{{\"traceId\":\"{traceId}\",\"spanId\":\"{index + 1:x16}\",\"name\":\"op{index % 50}\",\"kind\":2," +
                        $"\"startTimeUnixNano\":\"{now + (ulong)index}\",\"endTimeUnixNano\":\"{now + 1_000_000_000UL + (ulong)index}\"}}");
                }

                batch.Append("]}]}]}");
                using var content = new StringContent(batch.ToString(), Encoding.UTF8, "application/json");
                using var response = await _http.PostAsync($"http://127.0.0.1:{_otlpPort}/v1/traces", content, cancellationToken);
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
            }
        }
    }
}
