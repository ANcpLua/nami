using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Nami.Api.Contracts;
using Nami.Api.Contracts.Engine;
using Nami.ApiKeys;
using Nami.Collector.Storage;
using Nami.Tools;

namespace Nami.IntegrationTests;

/// <summary>
///     The engine's frame reader over in-memory pipes, fed the frames <c>engine.ts</c> writes. The host forwards an
///     agent's tool arguments and <c>_meta</c> trace context unchanged, so whatever an agent can send must leave the
///     engine answering: a request it cannot decode is refused alone.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class EngineProtocolTests(TestContext context)
{
    private static readonly DateTimeOffset s_now = DateTimeOffset.Parse("2026-11-16T00:00:00Z", CultureInfo.InvariantCulture);

    // JSON.stringify(EngineCommandJson.toJson(request)) + "\n", as engine.ts encodes a call.
    private static string Call(string id, string tool = "sources_status", string input = "{}", string traceparent = "null",
        string tracestate = "null", string? revision = null) =>
        $"{{\"id\":\"{id}\",\"revision\":\"{revision ?? ContractRevision.Value}\",\"method\":\"call\",\"name\":\"{tool}\"," +
        $"\"input\":{input},\"credential\":null,\"traceparent\":{traceparent},\"tracestate\":{tracestate}}}\n";

    private static string Health(string id) =>
        $"{{\"id\":\"{id}\",\"revision\":\"{ContractRevision.Value}\",\"method\":\"health\"}}\n";

    private static string Cancel(string id, string? revision = null) =>
        $"{{\"id\":\"{id}\",\"revision\":\"{revision ?? ContractRevision.Value}\",\"method\":\"cancel\"}}\n";

    /// <summary>
    ///     Ledger N56. The tool name of a call is the caller's text. nami's own span and its duration metric carry it
    ///     only when it is the name of one of nami's tools; any other name is "_OTHER" and the span is named by the
    ///     method alone.
    /// </summary>
    [TestMethod]
    [DoNotParallelize]
    public async Task ToolCall_ANameThatIsNoneOfNamisTools_ReachesNoSpanNameAndNoTagOfNamisOwnTelemetry()
    {
        const string received = "zqt-received-ZQT900X000ZZZZZZ";
        var spans = new System.Collections.Concurrent.ConcurrentQueue<(string Name, string Tags)>();
        using var activities = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = static source => source.Name == "nami",
            Sample = static (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => spans.Enqueue((activity.DisplayName + "|" + activity.OperationName,
                string.Join(";", activity.TagObjects.Select(static tag => tag.Key + "=" + tag.Value))))
        };
        System.Diagnostics.ActivitySource.AddActivityListener(activities);
        var measured = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var meters = new System.Diagnostics.Metrics.MeterListener();
        meters.InstrumentPublished = static (instrument, own) =>
        {
            if (instrument.Meter.Name == "nami") own.EnableMeasurementEvents(instrument);
        };
        meters.SetMeasurementEventCallback<double>((_, _, tags, _) =>
            measured.Enqueue(string.Join(";", tags.ToArray().Select(static tag => tag.Key + "=" + tag.Value))));
        meters.Start();

        await WithEngineAsync(async exchange =>
        {
            var unknown = await exchange(Call("1", received));
            Assert.IsTrue(unknown.GetProperty("is_error").GetBoolean());
            var known = await exchange(Call("2"));
            Assert.IsFalse(known.GetProperty("is_error").GetBoolean());
        });

        var all = spans.ToArray();
        Assert.IsTrue(all.Any(static span => span.Name == "tools/call|tools/call" && span.Tags.Contains("gen_ai.tool.name=_OTHER", StringComparison.Ordinal)),
            "No span for the unknown tool: " + string.Join(" / ", all));
        Assert.IsTrue(all.Any(static span => span.Name.StartsWith("tools/call sources_status|", StringComparison.Ordinal) &&
                                             span.Tags.Contains("gen_ai.tool.name=sources_status", StringComparison.Ordinal)),
            "One of nami's own tools keeps its name: " + string.Join(" / ", all));
        Assert.IsFalse(all.Any(static span => (span.Name + span.Tags).Contains("ZQT900X000ZZZZZZ", StringComparison.Ordinal)),
            "A received name in a span: " + string.Join(" / ", all));
        Assert.IsTrue(measured.Any(static tags => tags.Contains("gen_ai.tool.name=_OTHER", StringComparison.Ordinal)));
        Assert.IsFalse(measured.Any(static tags => tags.Contains("ZQT900X000ZZZZZZ", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow(62, DisplayName = "Inside the reader's default depth")]
    [DataRow(63, DisplayName = "One level past the default depth")]
    [DataRow(4000, DisplayName = "Far past the default depth")]
    public Task HostFrame_ArgumentsNestedPastTheReaderDepth_RefusedAloneAndTheEngineKeepsServing(int depth) =>
        WithEngineAsync(async exchange =>
        {
            // Arguments the MCP's schema refused are forwarded unchanged, so the engine can return its own refusal.
            var arguments = $"{{\"fields\":{new string('[', depth)}{new string(']', depth)}}}";
            var reply = await exchange(Call("1", "sources_status", arguments));
            Assert.AreEqual("1", reply.GetProperty("id").GetString());
            Assert.IsTrue(reply.GetProperty("is_error").GetBoolean());
            Assert.AreEqual("invalid_argument", reply.GetProperty("result").GetProperty("code").GetString());

            var health = await exchange(Health("2"));
            Assert.IsFalse(health.GetProperty("is_error").GetBoolean());
        });

    [TestMethod]
    [DataRow("\"\\ud800\"", "null", true, DisplayName = "Traceparent with a lone high surrogate")]
    [DataRow("\"00-\\udfff-01\"", "null", true, DisplayName = "Traceparent with a lone low surrogate")]
    [DataRow("null", "\"a=\\ud800\"", true, DisplayName = "Tracestate with a lone surrogate")]
    [DataRow("\"\\ud83d\\ude00\"", "null", false, DisplayName = "Traceparent with a surrogate pair")]
    public Task HostFrame_TraceContextWithAnUnpairedSurrogate_RefusedAloneAndTheEngineKeepsServing(string traceparent,
        string tracestate, bool refused) =>
        WithEngineAsync(async exchange =>
        {
            // JSON.stringify writes an unpaired surrogate of an agent's _meta trace context as an escape.
            var reply = await exchange(Call("1", "sources_status", "{}", traceparent, tracestate));
            Assert.AreEqual("1", reply.GetProperty("id").GetString());
            Assert.AreEqual(refused, reply.GetProperty("is_error").GetBoolean(), reply.GetRawText());
            if (refused)
                Assert.AreEqual("invalid_argument", reply.GetProperty("result").GetProperty("code").GetString());

            var health = await exchange(Health("2"));
            Assert.IsFalse(health.GetProperty("is_error").GetBoolean());
        });

    [TestMethod]
    public Task HostFrame_ArgumentNamedWithAnUnpairedSurrogate_RefusedAloneAndTheEngineKeepsServing() =>
        WithEngineAsync(async exchange =>
        {
            // The MCP's schema refuses the unknown member and forwards the arguments for the engine's refusal.
            var reply = await exchange(Call("1", "sources_status", "{\"\\udc00\":1}"));
            Assert.IsTrue(reply.GetProperty("is_error").GetBoolean());
            Assert.AreEqual("invalid_argument", reply.GetProperty("result").GetProperty("code").GetString());

            var health = await exchange(Health("2"));
            Assert.IsFalse(health.GetProperty("is_error").GetBoolean());
        });

    [TestMethod]
    public Task HostFrame_ArgumentValueWithAnUnpairedSurrogate_RefusedAsAnInvalidArgument() =>
        WithEngineAsync(async exchange =>
        {
            // The MCP's schema accepts this string, so the tool's own contract check is the one that sees it.
            var reply = await exchange(Call("1", "sources_status", "{\"service\":\"a\\ud800\"}"));
            Assert.IsTrue(reply.GetProperty("is_error").GetBoolean());
            Assert.AreEqual("invalid_argument", reply.GetProperty("result").GetProperty("code").GetString(),
                reply.GetRawText());
            Assert.AreEqual("service", reply.GetProperty("result").GetProperty("field").GetString());
        });

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public Task Slots_CancelAtTheLimitThenNewCalls_AllAcceptedAndTheCancelledCallIsNotAnswered() =>
        WithHeldEngineAsync(async engine =>
        {
            // eng/proofs Design.traceA: the host frees a slot when its caller goes away (engine.ts, abort) and fills
            // it at once. 32 calls wait, one is cancelled, and a 33rd arrives in the same write as the cancel.
            var first = Enumerable.Range(1, 32).Select(static id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
            await engine.Session.WriteAsync(string.Concat(first.Select(static id => Call(id))), context.CancellationToken);
            await engine.Store.ArrivedAsync(2, context.CancellationToken);
            await engine.Session.WriteAsync(Cancel("1") + Call("33"), context.CancellationToken);
            // The line for the frame that is no request is written once the engine has read every frame before it.
            await engine.Session.WriteAsync(Cancel("2") + Health("h") + "not-json\n", context.CancellationToken);
            await engine.DroppedAsync(1, context.CancellationToken);
            engine.Store.Open();
            var replies = await engine.RepliesAsync(32, context.CancellationToken);
            string[] expected = [.. first.Skip(2), "33", "h"];
            Assert.AreSequenceEqual(expected.Order(StringComparer.Ordinal).ToArray(),
                replies.Keys.Order(StringComparer.Ordinal).ToArray(), engine.Describe());
            Assert.IsTrue(replies.Values.All(static reply => !reply.GetProperty("is_error").GetBoolean()),
                "No call within the host's limit may be refused.");
            Assert.IsNull(await engine.Session.ReadLineAsync(TimeSpan.FromMilliseconds(300), context.CancellationToken),
                "A cancelled call is not answered: the host stopped waiting when it wrote the cancel.");
        });

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public Task Slots_ReplyReadBeforeItsWriteReturned_TheNextCallIsAccepted() =>
        WithHeldEngineAsync(async engine =>
        {
            // eng/proofs Design.traceB: the host frees a slot when it reads the reply and fills it at once, while
            // the engine's write of that reply has not returned. The entry must be gone before the reply is written.
            var first = Enumerable.Range(1, 32).Select(static id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
            await engine.Session.WriteAsync(string.Concat(first.Select(static id => Call(id))), context.CancellationToken);
            await engine.Store.ArrivedAsync(2, context.CancellationToken);
            engine.HoldReplies();
            engine.Store.Open();
            var one = await engine.RepliesAsync(1, context.CancellationToken);
            Assert.HasCount(1, one, engine.Describe());
            // The reply's write returns only after the engine has read call 33: the line for the frame behind it
            // says so. An engine that ends at call 33 never writes that line.
            await engine.Session.WriteAsync(Call("33") + "not-json\n", context.CancellationToken);
            await engine.DroppedAsync(1, context.CancellationToken);
            engine.ReleaseReplies();
            var rest = await engine.RepliesAsync(32, context.CancellationToken);
            Assert.HasCount(32, rest, engine.Describe());
            Assert.IsTrue(rest.ContainsKey("33"), engine.Describe());
            Assert.IsFalse(rest["33"].GetProperty("is_error").GetBoolean(),
                "The call that fills the slot of a reply the host has read must be accepted: " + rest["33"].GetRawText());
            Assert.IsTrue(rest.Values.All(static reply => !reply.GetProperty("is_error").GetBoolean()));
        });

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public Task Refusal_CallPastThePendingLimit_RefusedForItsIdAndTheOthersFinish() =>
        WithHeldEngineAsync(async engine =>
        {
            var first = Enumerable.Range(1, 32).Select(static id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
            await engine.Session.WriteAsync(string.Concat(first.Select(static id => Call(id))) + Call("33"),
                context.CancellationToken);
            var refusal = (await engine.RepliesAsync(1, context.CancellationToken)).GetValueOrDefault("33");
            Assert.AreEqual(JsonValueKind.Object, refusal.ValueKind, engine.Describe());
            Assert.IsTrue(refusal.GetProperty("is_error").GetBoolean());
            Assert.AreEqual("collector_unavailable", refusal.GetProperty("result").GetProperty("code").GetString());
            Assert.IsTrue(refusal.GetProperty("result").GetProperty("retryable").GetBoolean(),
                "A full engine is a state that passes: the refusal must say the call can be repeated.");
            engine.Store.Open();
            var replies = await engine.RepliesAsync(32, context.CancellationToken);
            Assert.AreSequenceEqual(first.Order(StringComparer.Ordinal).ToArray(),
                replies.Keys.Order(StringComparer.Ordinal).ToArray(), engine.Describe());
            Assert.IsTrue(replies.Values.All(static reply => !reply.GetProperty("is_error").GetBoolean()));
        });

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public Task Refusal_DuplicateId_RefusedForThatIdAndTheFirstCallFinishes() =>
        WithHeldEngineAsync(async engine =>
        {
            await engine.Session.WriteAsync(Call("1") + Call("1") + Call("2"), context.CancellationToken);
            var refusal = (await engine.RepliesAsync(1, context.CancellationToken)).GetValueOrDefault("1");
            Assert.AreEqual(JsonValueKind.Object, refusal.ValueKind, engine.Describe());
            Assert.IsTrue(refusal.GetProperty("is_error").GetBoolean());
            Assert.AreEqual("invalid_argument", refusal.GetProperty("result").GetProperty("code").GetString());
            Assert.AreEqual("id", refusal.GetProperty("result").GetProperty("field").GetString());
            engine.Store.Open();
            var replies = await engine.RepliesAsync(2, context.CancellationToken);
            Assert.IsTrue(replies.ContainsKey("1") && replies.ContainsKey("2"), engine.Describe());
            Assert.IsTrue(replies.Values.All(static reply => !reply.GetProperty("is_error").GetBoolean()),
                "The first call with the id and the call after the duplicate must both finish.");
        });

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public Task Refusal_WrongRevision_RefusedBeforeDispatchAndTheEngineKeepsServing() =>
        WithHeldEngineAsync(async engine =>
        {
            await engine.Session.WriteAsync(Call("1") + Call("2", revision: "sha256:0") + Cancel("1", "sha256:0"),
                context.CancellationToken);
            var refusal = (await engine.RepliesAsync(1, context.CancellationToken)).GetValueOrDefault("2");
            Assert.AreEqual(JsonValueKind.Object, refusal.ValueKind, engine.Describe());
            Assert.IsTrue(refusal.GetProperty("is_error").GetBoolean());
            Assert.AreEqual("collector_contract_mismatch", refusal.GetProperty("result").GetProperty("code").GetString());
            await engine.Store.ArrivedAsync(1, context.CancellationToken);
            await engine.DroppedAsync(1, context.CancellationToken);
            engine.Store.Open();
            var replies = await engine.RepliesAsync(1, context.CancellationToken);
            Assert.IsTrue(replies.TryGetValue("1", out var served) && !served.GetProperty("is_error").GetBoolean(),
                "A cancel that names another revision is not obeyed, and the call before the refusal finishes: " +
                engine.Describe());
            Assert.AreEqual(1, engine.Store.Arrived, "The refused call must not reach the store.");
            string[] dropped = ["nami engine: dropped a cancel frame for another contract revision."];
            Assert.AreSequenceEqual(dropped, engine.Diagnostics);
        });

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public Task BadFrames_NotJsonWithoutIdAndOversize_DroppedWithOneLineEachAndCallsInFlightFinish() =>
        WithHeldEngineAsync(async engine =>
        {
            // P14: a frame that cannot be parsed or is too large is dropped up to the next newline, with one line on
            // standard error; all other running calls finish.
            var oversize = "{\"id\":\"big\",\"pad\":\"" + new string('x', EngineLimits.MaxFrameBytesDefault + 4096) + "\"}\n";
            await engine.Session.WriteAsync(Call("1") + "not-json\n" + "{\"id\":7}\n" + oversize + Call("2"),
                context.CancellationToken);
            await engine.Store.ArrivedAsync(2, context.CancellationToken);
            engine.Store.Open();
            var replies = await engine.RepliesAsync(2, context.CancellationToken);
            Assert.IsTrue(replies.ContainsKey("1") && replies.ContainsKey("2"), engine.Describe());
            Assert.IsTrue(replies.Values.All(static reply => !reply.GetProperty("is_error").GetBoolean()));
            string[] dropped =
            [
                "nami engine: dropped a frame that is not an engine request.",
                "nami engine: dropped a frame that is not an engine request.",
                "nami engine: dropped a frame longer than the frame limit."
            ];
            Assert.AreSequenceEqual(dropped, engine.Diagnostics);
            await engine.Session.CloseInputAsync();
            await engine.Session.Run.WaitAsync(TimeSpan.FromSeconds(10), context.CancellationToken);
        });

    /// <summary>Serves one engine over pipes for the test body, which exchanges frames for replies.</summary>
    private async Task WithEngineAsync(Func<Func<string, Task<JsonElement>>, Task> body)
    {
        var clock = new FakeTimeProvider(s_now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        await using var provider = new ServiceCollection()
            .AddSingleton<INamiStore>(store)
            .AddSingleton(Options.Create(new OtlpApiKeyOptions()))
            .AddSingleton<NamiToolService>()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();
        await using var session = new EngineSession(provider);
        Assert.IsNotNull(await session.ReadLineAsync(TimeSpan.FromSeconds(30), context.CancellationToken),
            "The engine did not report ready.");
        await body(async frame =>
        {
            await session.WriteAsync(frame, new Random(1), context.CancellationToken);
            var line = await session.ReadLineAsync(TimeSpan.FromSeconds(30), context.CancellationToken);
            if (line is null)
            {
                Assert.IsTrue(session.Run.IsCompleted, "The engine neither answered nor stopped.");
                Assert.Fail("The engine stopped instead of answering: " + session.Run.Exception?.InnerException);
            }

            using var reply = JsonDocument.Parse(line);
            return reply.RootElement.Clone();
        });
    }

    /// <summary>
    ///     Serves one engine whose tool calls wait at the store until the test opens it, so the test decides which
    ///     calls are in flight when the next frame arrives.
    /// </summary>
    private async Task WithHeldEngineAsync(Func<HeldEngine, Task> body)
    {
        var clock = new FakeTimeProvider(s_now);
        await using var inner = new DuckDbStore(":memory:", timeProvider: clock);
        await using var store = new HeldStore(inner);
        await using var provider = new ServiceCollection()
            .AddSingleton<INamiStore>(store)
            .AddSingleton(Options.Create(new OtlpApiKeyOptions()))
            .AddSingleton<NamiToolService>()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();
        using var diagnostics = new Lines();
        var engine = new HeldEngine(store, diagnostics);
        await using var session = new EngineSession(provider, diagnostics, engine.ReplyGate);
        engine.Session = session;
        Assert.IsNotNull(await session.ReadLineAsync(TimeSpan.FromSeconds(30), context.CancellationToken),
            "The engine did not report ready.");
        try
        {
            await body(engine);
        }
        finally
        {
            // Whatever the test judged, nothing may stay held when the session is disposed.
            store.Open();
            engine.ReleaseReplies();
        }
    }

    /// <summary>A store whose snapshot reads wait until the test opens it, and do not end early when cancelled.</summary>
    private sealed class HeldStore(INamiStore inner) : INamiStore
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        /// <summary>The snapshot reads that have started.</summary>
        internal int Arrived => Volatile.Read(ref _arrived);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public async Task<Nami.Collector.Storage.TelemetrySnapshot> ReadSnapshotAsync(string projectId, string? snapshotId = null,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _arrived);
            // A tool's work does not observe cancellation once it runs (ToolDispatcher.Run); this wait stands for it.
            await _open.Task;
            return await inner.ReadSnapshotAsync(projectId, snapshotId, CancellationToken.None);
        }

        public ValueTask EnqueueAsync(SpanBatch batch, CancellationToken ct = default) => inner.EnqueueAsync(batch, ct);

        public Task InsertLogsAsync(IReadOnlyList<LogStorageRow> logs, CancellationToken ct = default) =>
            inner.InsertLogsAsync(logs, ct);

        public Task InsertMetricsAsync(IReadOnlyList<MetricSeriesRow> series,
            IReadOnlyList<MetricObservationRow> observations, CancellationToken ct = default) =>
            inner.InsertMetricsAsync(series, observations, ct);

        public Task<(long Logs, long Spans, long MetricPoints, long MetricSeries)> DeleteExpiredAsync(
            ulong cutoffUnixNano, CancellationToken ct = default) => inner.DeleteExpiredAsync(cutoffUnixNano, ct);

        public StorageFileMetrics GetStorageFileMetrics() => inner.GetStorageFileMetrics();

        public Task<StorageStats> GetStorageStatsAsync(string projectId, CancellationToken ct = default) =>
            inner.GetStorageStatsAsync(projectId, ct);

        internal void Open() => _open.TrySetResult();

        internal async Task ArrivedAsync(int count, CancellationToken ct)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            while (Arrived < count) await Task.Delay(10, deadline.Token);
        }
    }

    /// <summary>The lines the engine writes to its diagnostics stream, which is standard error in production.</summary>
    private sealed class Lines() : TextWriter(CultureInfo.InvariantCulture)
    {
        private readonly Lock _gate = new();
        private readonly List<string> _written = [];

        public override Encoding Encoding => Encoding.UTF8;

        public override void WriteLine(string? value)
        {
            lock (_gate) _written.Add(value ?? "");
        }

        internal string[] Written()
        {
            lock (_gate) return [.. _written];
        }
    }

    private sealed class HeldEngine(HeldStore store, Lines diagnostics)
    {
        private readonly Lock _gate = new();
        private TaskCompletionSource _replies = Completed();

        internal HeldStore Store => store;
        internal EngineSession Session { get; set; } = null!;

        /// <summary>The lines the engine wrote to its diagnostics stream so far.</summary>
        internal string[] Diagnostics => diagnostics.Written();

        /// <summary>
        ///     Waits until the engine has dropped <paramref name="count" /> frames, which it does in the order it reads
        ///     them: every frame written before the dropped one has then been read. Returns when the engine stops or
        ///     20 seconds pass, too; the assertions that follow judge that.
        /// </summary>
        internal async Task DroppedAsync(int count, CancellationToken ct)
        {
            var until = TimeProvider.System.GetTimestamp() + 20 * TimeProvider.System.TimestampFrequency;
            while (Diagnostics.Length < count && !Session.Run.IsCompleted && TimeProvider.System.GetTimestamp() < until)
                await Task.Delay(10, ct);
        }

        private static TaskCompletionSource Completed()
        {
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            source.SetResult();
            return source;
        }

        internal Task ReplyGate()
        {
            lock (_gate) return _replies.Task;
        }

        /// <summary>From now on a reply's write does not return, though its bytes can be read.</summary>
        internal void HoldReplies()
        {
            lock (_gate) _replies = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        internal void ReleaseReplies()
        {
            lock (_gate) _replies.TrySetResult();
        }

        internal string Describe() =>
            Session.Run.IsCompleted
                ? "the engine stopped: " + (Session.Run.Exception?.InnerException?.Message ?? Session.Run.Status.ToString())
                : "the engine is running";

        /// <summary>The next <paramref name="count" /> replies by id; fewer when the engine stops or stays silent.</summary>
        internal async Task<Dictionary<string, JsonElement>> RepliesAsync(int count, CancellationToken ct)
        {
            var replies = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            while (replies.Count < count && await Session.ReadLineAsync(TimeSpan.FromSeconds(20), ct) is { } line)
            {
                using var reply = JsonDocument.Parse(line);
                // A refusal of a duplicate id and the first call's reply share the id: keep the first seen.
                replies.TryAdd(reply.RootElement.GetProperty("id").GetString()!, reply.RootElement.Clone());
            }

            return replies;
        }
    }
}
