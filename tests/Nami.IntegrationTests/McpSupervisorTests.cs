using System.Text.Json;
using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public sealed class McpSupervisorTests(TestContext context)
{
    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task OversizedMcpInput_RefusesOnlyThatRequest()
    {
        using var result = await RunAsync("oversize");
        var observed = result.RootElement;
        Assert.AreEqual("invalid_argument", observed.GetProperty("refusalCode").GetString());
        Assert.IsFalse(observed.GetProperty("pendingIsError").GetBoolean(), "The call held during the refusal must still succeed.");
        Assert.IsFalse(observed.GetProperty("subsequentIsError").GetBoolean(), "A call after the refusal must succeed.");
        Assert.AreEqual("fixture-snapshot", observed.GetProperty("subsequentSnapshot").GetString());
        Assert.IsGreaterThan(0, observed.GetProperty("childBefore").GetInt32());
        Assert.AreEqual(observed.GetProperty("childBefore").GetInt32(), observed.GetProperty("childAfter").GetInt32(),
            "The refusal must not restart the engine.");
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task Backpressure_CancellationSettlesWithoutUnhandledRejection()
    {
        using var result = await RunAsync("backpressure");
        var observed = result.RootElement;
        Assert.AreEqual("rejected", observed.GetProperty("settled").GetString(),
            "Cancellation must settle without waiting for pipe drain.");
        Assert.AreEqual(0, observed.GetProperty("unhandledRejections").GetInt32());
        Assert.IsTrue(observed.GetProperty("cancelForwarded").GetBoolean(), "The engine must receive the cancel command.");
        Assert.IsFalse(observed.GetProperty("subsequentIsError").GetBoolean(), "A call after the cancellation must succeed.");
        Assert.IsGreaterThan(0, observed.GetProperty("child").GetInt32());
        Assert.AreEqual(observed.GetProperty("child").GetInt32(), observed.GetProperty("subsequentChild").GetInt32(),
            "The cancellation must not restart the engine.");
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task FailedEngine_CloseReapsSigtermResistantChild()
    {
        using var result = await RunAsync("failure");
        var observed = result.RootElement;
        Assert.IsTrue(observed.GetProperty("termReceived").GetBoolean(), "Shutdown must send SIGTERM first.");
        Assert.IsFalse(observed.GetProperty("childRunningAfterClose").GetBoolean(),
            "Shutdown must await termination of a SIGTERM-resistant child.");
        Assert.IsTrue(observed.GetProperty("sameShutdown").GetBoolean(), "Concurrent shutdown calls must share completion.");
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task Http_ClosedStdin_RemainsAvailableUntilSignal()
    {
        using var result = await RunAsync("http");
        var observed = result.RootElement;
        Assert.AreEqual("alive", observed.GetProperty("afterEof").GetString(), "Closed stdin must not stop the HTTP host.");
        Assert.AreEqual(200, observed.GetProperty("healthStatus").GetInt32());
        Assert.AreEqual(200, observed.GetProperty("toolStatus").GetInt32());
        Assert.IsTrue(observed.GetProperty("toolReturnedSnapshot").GetBoolean(), "The tool call must return the engine's result.");
        Assert.AreEqual(0, observed.GetProperty("exitCodeAfterSignal").GetInt32());
        Assert.IsFalse(observed.GetProperty("childRunningAfterSignal").GetBoolean(), "SIGTERM must also end the engine.");
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task Stdio_Eof_ShutsDownOwnedEngine()
    {
        using var result = await RunAsync("stdio");
        var observed = result.RootElement;
        Assert.AreEqual(0, observed.GetProperty("exitCodeOnEof").GetInt32());
        Assert.IsFalse(observed.GetProperty("childRunningAfterExit").GetBoolean(), "End of input must also end the engine.");
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task Health_EngineGone_Returns503()
    {
        // P04 "engine gone": the engine has failed and the host has not exited yet. The fixture
        // engine writes one line that is not JSON and ignores SIGTERM, so the host waits for it.
        using var result = await RunAsync("gone");
        var observed = result.RootElement;
        Assert.AreEqual(200, observed.GetProperty("healthBefore").GetInt32());
        Assert.IsTrue(observed.GetProperty("engineFailed").GetBoolean(), "The host must stop the failed engine.");
        Assert.AreEqual(503, observed.GetProperty("healthStatus").GetInt32(),
            "GET /health between the engine's failure and the host's exit must answer 503.");
        Assert.AreEqual("unhealthy", observed.GetProperty("healthReportStatus").GetString());
        var exit = observed.GetProperty("hostExit");
        Assert.AreEqual(JsonValueKind.Number, exit.ValueKind, "The host must exit after its engine failed; it was still " + exit);
        Assert.AreNotEqual(0, exit.GetInt32(), "The host must exit non-zero when its engine failed.");
        Assert.IsTrue(observed.GetProperty("childGone").GetBoolean(), "No engine process may be left behind.");
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task Host_EngineKilledMidCall_AnswersTheCallThenExitsNonZero()
    {
        // P04 over the fixture engine: the host must not end before the call its engine's death failed is answered.
        using var result = await RunAsync("killed");
        var observed = result.RootElement;
        Assert.IsTrue(observed.GetProperty("waiting").GetBoolean(), "Precondition: the call waits in the engine.");
        Assert.AreEqual(JsonValueKind.Null, observed.GetProperty("transportError").ValueKind,
            "The waiting call must get an answer, not a closed connection.");
        Assert.IsTrue(observed.GetProperty("isError").GetBoolean());
        Assert.AreEqual("collector_unavailable", observed.GetProperty("code").GetString());
        Assert.IsTrue(observed.GetProperty("retryable").GetBoolean());
        var exit = observed.GetProperty("hostExit");
        Assert.AreEqual(JsonValueKind.Number, exit.ValueKind, "The host must exit after its engine died; it was still " + exit);
        Assert.AreNotEqual(0, exit.GetInt32());
        Assert.IsTrue(observed.GetProperty("childGone").GetBoolean());
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task Host_SecondSigtermWhileClosing_EndsByItselfAndTakesTheEngineWithIt()
    {
        // The engine ignores SIGTERM and the end of its input, so the first SIGTERM leaves the host waiting
        // for it. A second one used to find no handler: the host died of the signal and its engine lived on.
        using var result = await RunAsync("twice");
        var observed = result.RootElement;
        Assert.IsTrue(observed.GetProperty("closingBegan").GetBoolean(), "The first SIGTERM must stop the listener.");
        Assert.AreEqual(JsonValueKind.Null, observed.GetProperty("hostSignal").ValueKind,
            "The host must end by itself, so that its exit listener runs; it died of " + observed.GetProperty("hostSignal"));
        Assert.AreEqual(JsonValueKind.Number, observed.GetProperty("hostExit").ValueKind,
            "The second SIGTERM must end the host at once; it was still " + observed.GetProperty("hostExit"));
        Assert.AreNotEqual(0, observed.GetProperty("hostExit").GetInt32(), "A forced end is not a clean exit.");
        Assert.IsTrue(observed.GetProperty("childGone").GetBoolean(), "No engine process may be left behind.");
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task Health_EngineReportsError_Returns503()
    {
        using var result = await RunAsync("unhealthy");
        var observed = result.RootElement;
        Assert.AreEqual(503, observed.GetProperty("healthStatus").GetInt32());
        Assert.AreEqual("unhealthy", observed.GetProperty("healthReportStatus").GetString());
        Assert.AreEqual(0, observed.GetProperty("exitCodeAfterSignal").GetInt32());
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task Health_FullEngineQueue_AnswersWithAHealthReport()
    {
        // 32 tool calls wait in a healthy engine (ENGINE_LIMITS.max_pending). /health must still answer
        // with a health report, not with the host's own queue limit as an unhandled error.
        using var result = await RunAsync("busy");
        var observed = result.RootElement;
        Assert.IsTrue(observed.GetProperty("allWaiting").GetBoolean(), "The fixture must hold 32 calls.");
        var status = observed.GetProperty("healthStatus").GetInt32();
        Assert.IsTrue(status is 200 or 503, $"GET /health answered {status} while 32 calls were waiting.");
        Assert.IsTrue(observed.GetProperty("healthReportStatus").GetString() is "healthy" or "degraded" or "unhealthy",
            "The answer must be a health report.");
        Assert.AreEqual(32, observed.GetProperty("callsSucceeded").GetInt32());
        Assert.AreEqual(0, observed.GetProperty("exitCodeAfterSignal").GetInt32(), "A clean shutdown must exit 0.");
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task Host_CancelMidCall_CompletesTheWaitingCall()
    {
        // 32 calls wait; the caller of one goes away. The host must end that waiting call: forward a
        // cancel and free its slot, so a 33rd call reaches the engine instead of the host's limit.
        using var result = await RunAsync("cancel");
        var observed = result.RootElement;
        Assert.IsTrue(observed.GetProperty("allWaiting").GetBoolean(), "The fixture must hold 32 calls.");
        Assert.AreEqual("AbortError", observed.GetProperty("abortedCaller").GetString());
        Assert.IsTrue(observed.GetProperty("cancelForwarded").GetBoolean(), "The engine must receive the cancel.");
        Assert.IsTrue(observed.GetProperty("nextForwarded").GetBoolean(),
            "The cancelled call's slot must be free for the next call.");
        Assert.IsFalse(observed.GetProperty("nextIsError").GetBoolean());
        Assert.AreEqual(31, observed.GetProperty("othersSucceeded").GetInt32());
        Assert.AreEqual(0, observed.GetProperty("exitCodeAfterSignal").GetInt32());
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task TraceContext_OnlyW3cFormsReachTheEngine_AndACallWithoutOneStillRuns()
    {
        // H1: an agent's _meta trace context was forwarded as it came, and a lone surrogate in it ended the engine.
        const string Trace = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
        using var result = await RunAsync("trace");
        var seen = result.RootElement;
        foreach (var call in seen.EnumerateObject())
        {
            Assert.IsFalse(call.Value.GetProperty("isError").GetBoolean(), call.Name + ": a trace context never fails a call.");
            Assert.IsTrue(call.Value.GetProperty("forwarded").GetBoolean(), call.Name + ": the call must reach the engine.");
        }

        Assert.AreEqual(Trace, seen.GetProperty("valid").GetProperty("traceparent").GetString());
        Assert.AreEqual("vendor=value, tenant@system=1", seen.GetProperty("valid").GetProperty("tracestate").GetString());
        // A later version is forwarded as the fields every version shares.
        Assert.AreEqual("01" + Trace[2..], seen.GetProperty("laterVersion").GetProperty("traceparent").GetString());
        string[] noParent =
        [
            "loneSurrogate", "upperCase", "zeroTraceId", "zeroParentId", "versionFf", "trailingOnVersion00", "notText",
            "stateWithoutParent"
        ];
        foreach (var name in noParent)
        {
            Assert.AreEqual(JsonValueKind.Null, seen.GetProperty(name).GetProperty("traceparent").ValueKind,
                name + ": a traceparent that is not in W3C form must not be forwarded.");
            Assert.AreEqual(JsonValueKind.Null, seen.GetProperty(name).GetProperty("tracestate").ValueKind,
                name + ": a tracestate travels only with its traceparent.");
        }

        string[] noState = ["stateWithSurrogate", "stateWithoutValue", "stateWithRepeatedKey", "laterVersion"];
        foreach (var name in noState.Take(3))
            Assert.AreEqual(Trace, seen.GetProperty(name).GetProperty("traceparent").GetString(), name);
        foreach (var name in noState)
            Assert.AreEqual(JsonValueKind.Null, seen.GetProperty(name).GetProperty("tracestate").ValueKind,
                name + ": a tracestate that is not a well-formed list must not be forwarded.");
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task SchemaRefusal_AnsweredByTheHostWithFieldAllowedAndHint_AndNeverForwarded()
    {
        // H2: arguments the input schema refused were forwarded as they came; 70 levels of nesting ended the engine.
        using var result = await RunAsync("refusal");
        var seen = result.RootElement;
        string[] accepted = ["service", "since", "fields", "max_bytes", "snapshot_id", "cursor"];
        (string Case, string Field, string Message, string Hint, bool Allowed)[] expected =
        [
            ("unknownNestedDeep", "nested", "nested is not accepted here; accepted: " + string.Join(", ", accepted),
                "Replace the argument with one of the accepted arguments or drop it.", true),
            ("belowMinimum", "max_bytes", "max_bytes must be >= 1024", "Constraint: minimum.", false),
            ("wrongItemType", "fields[1]", "fields[1] must be string", "Constraint: type.", false),
            ("tooLong", "service", "service must NOT have more than 256 characters", "Constraint: maxLength.", false),
            ("nameWithSurrogate", "\uFFFD", "\uFFFD is not accepted here; accepted: " + string.Join(", ", accepted),
                "Replace the argument with one of the accepted arguments or drop it.", true)
        ];
        foreach (var (name, field, message, hint, allowed) in expected)
        {
            var refusal = seen.GetProperty(name);
            Assert.IsTrue(refusal.GetProperty("isError").GetBoolean(), name);
            Assert.IsFalse(refusal.GetProperty("forwarded").GetBoolean(), name + ": refused arguments must not reach the engine.");
            Assert.AreEqual("invalid_argument", refusal.GetProperty("code").GetString(), name);
            Assert.IsFalse(refusal.GetProperty("retryable").GetBoolean(), name);
            Assert.AreEqual(field, refusal.GetProperty("field").GetString(), name);
            Assert.AreEqual(message, refusal.GetProperty("message").GetString(), name);
            Assert.AreEqual(hint, refusal.GetProperty("hint").GetString(), name);
            if (allowed)
                Assert.AreSequenceEqual(accepted,
                    refusal.GetProperty("allowed").EnumerateArray().Select(static value => value.GetString()!).ToArray(), name);
            else
                Assert.AreEqual(JsonValueKind.Null, refusal.GetProperty("allowed").ValueKind, name);
            Assert.AreEqual(2, refusal.GetProperty("parts").GetInt32(), name + ": a headline and the JSON text.");
            Assert.IsTrue(refusal.GetProperty("textIsStructured").GetBoolean(), name + ": the text part must be the structured content.");
            Assert.IsFalse(refusal.GetProperty("loneSurrogate").GetBoolean(), name + ": the refusal must be valid Unicode.");
        }

        Assert.IsFalse(seen.GetProperty("laterIsError").GetBoolean(), "A valid call after the refusals must succeed.");
        Assert.IsTrue(seen.GetProperty("laterForwarded").GetBoolean());
        Assert.AreEqual(1, seen.GetProperty("framesInAll").GetInt32(), "Only the valid call may have reached the engine.");
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task Headline_CutInsideAnAstralCharacter_StaysValidUnicode()
    {
        // H8: the headline cut each value at 96 UTF-16 units, which can end in half of a character.
        using var result = await RunAsync("headline");
        var seen = result.RootElement;
        Assert.IsFalse(seen.GetProperty("isError").GetBoolean());
        Assert.IsTrue(seen.GetProperty("cutAfterIt").GetBoolean(), "Precondition: the value is longer than the cut.");
        Assert.IsFalse(seen.GetProperty("loneSurrogate").GetBoolean(), "The headline must not end a value in a lone surrogate.");
        Assert.IsTrue(seen.GetProperty("keepsTheCharacter").GetBoolean(), "The 96th character is kept whole.");
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task EventStreamCall_EngineAnswersAfterFifteenSeconds_ReturnsItsResult()
    {
        // H5: Bun closed a connection after 10 silent seconds, so a 2025-11-25 call that ran longer ended as an
        // empty 200 and its engine call was cancelled.
        using var result = await RunAsync("slow");
        var observed = result.RootElement;
        Assert.AreEqual(200, observed.GetProperty("status").GetInt32());
        Assert.IsGreaterThanOrEqualTo(15, observed.GetProperty("seconds").GetInt32(), "Precondition: the call outlasts the old idle timeout.");
        Assert.AreEqual("fixture-snapshot", observed.GetProperty("snapshot").GetString(), "The call must return the engine's result.");
        Assert.IsFalse(observed.GetProperty("isError").GetBoolean());
        Assert.IsGreaterThanOrEqualTo(2, observed.GetProperty("keepAlives").GetInt32(),
            "The stream must carry a keep-alive more often than every 10 seconds.");
        // A 2026-07-28 call is one JSON body and was not cut before either; it must stay so.
        Assert.AreEqual(200, observed.GetProperty("jsonStatus").GetInt32());
        Assert.AreEqual("fixture-snapshot", observed.GetProperty("jsonSnapshot").GetString());
        Assert.AreEqual(0, observed.GetProperty("exitCodeAfterSignal").GetInt32());
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task MalformedHostHeader_RefusedAsJsonRpc_WithoutAnErrorPage()
    {
        // H6: the URL was read before the Host check, and a Host that is no host made that throw: Bun answered 500
        // with its development page, 50 KB of source lines, paths and a stack, to a caller without a credential.
        using var result = await RunAsync("badhost");
        var seen = result.RootElement;
        string[] cases = ["space", "openBracket", "portOutOfRange", "foreign"];
        foreach (var name in cases)
        {
            var answer = seen.GetProperty(name);
            var status = answer.GetProperty("status").GetInt32();
            Assert.IsTrue(status is 400 or 403, $"{name}: a Host that is not allowed is refused, not a failure; got {status}.");
            Assert.AreEqual("application/json", answer.GetProperty("contentType").GetString(), name);
            Assert.IsNotNull(answer.GetProperty("rpcCode").GetString(), name + ": the body must be a JSON-RPC error.");
            Assert.IsLessThan(1024, answer.GetProperty("bytes").GetInt32(), name);
            Assert.IsFalse(answer.GetProperty("disclosesSource").GetBoolean(), name + ": no page, path or stack.");
        }

        Assert.AreEqual(200, seen.GetProperty("healthAfter").GetInt32(), "The host must keep serving.");
        Assert.AreEqual(0, seen.GetProperty("exitCodeAfterSignal").GetInt32());
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task Stdio_InApiKeyMode_RefusedAtStartWithItsReason()
    {
        // H11: stdio carries no credential, so in ApiKey mode every call over it was answered "unauthorized".
        using var result = await RunAsync("stdio-apikey");
        var observed = result.RootElement;
        var exit = observed.GetProperty("hostExit");
        Assert.AreEqual(JsonValueKind.Number, exit.ValueKind, "The host must refuse to start; it was still " + exit);
        Assert.AreNotEqual(0, exit.GetInt32());
        Assert.IsFalse(observed.GetProperty("engineStarted").GetBoolean(), "The refusal comes before the engine starts.");
        Assert.IsTrue(observed.GetProperty("namesTheTransport").GetBoolean(), "The reason must name --stdio.");
        Assert.IsTrue(observed.GetProperty("namesTheMode").GetBoolean(), "The reason must name the mode.");
        Assert.IsTrue(observed.GetProperty("namesTheWayOut").GetBoolean(), "The reason must say what works instead.");
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task Stdio_WithOnlyTheDevelopmentEnvironment_RefusedAsApiKeyMode()
    {
        // The environment name alone never turns Unsecured on: the mode is ApiKey until NAMI_OTLP_AUTH_MODE says otherwise.
        using var result = await RunAsync("stdio-development");
        var observed = result.RootElement;
        var exit = observed.GetProperty("hostExit");
        Assert.AreEqual(JsonValueKind.Number, exit.ValueKind, "The host must refuse to start; it was still " + exit);
        Assert.AreNotEqual(0, exit.GetInt32());
        Assert.IsFalse(observed.GetProperty("engineStarted").GetBoolean(), "The refusal comes before the engine starts.");
        Assert.IsTrue(observed.GetProperty("namesTheMode").GetBoolean(), "The reason must name the mode in force.");
        Assert.IsTrue(observed.GetProperty("namesTheWayOut").GetBoolean(), "The reason must say how to ask for Unsecured.");
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    [DataRow("unsecured-http", DisplayName = "Streamable HTTP on every interface, as the image binds")]
    [DataRow("unsecured-http-ipv6", DisplayName = "Streamable HTTP on every IPv6 interface")]
    [DataRow("unsecured-http-nic", DisplayName = "Streamable HTTP on one network interface")]
    [DataRow("unsecured-stdio", DisplayName = "Stdio, with the engine's OTLP listeners on every interface")]
    public async Task Unsecured_OnAnAddressThatIsNotLoopback_RefusedAtStartWithWhatToSet(string scenario)
    {
        using var result = await RunAsync(scenario);
        var observed = result.RootElement;
        var exit = observed.GetProperty("hostExit");
        Assert.AreEqual(JsonValueKind.Number, exit.ValueKind, "The host must refuse to start; it was still " + exit);
        Assert.AreNotEqual(0, exit.GetInt32());
        Assert.IsFalse(observed.GetProperty("engineStarted").GetBoolean(), "The refusal comes before the engine starts.");
        Assert.IsTrue(observed.GetProperty("namesTheAddress").GetBoolean(), "The reason must name the address it refuses.");
        Assert.IsTrue(observed.GetProperty("namesLoopback").GetBoolean(), "The reason must say how to stay Unsecured.");
        Assert.IsTrue(observed.GetProperty("namesKeys").GetBoolean(), "The reason must say how to listen elsewhere.");
    }

    private async Task<JsonDocument> RunAsync(string scenario)
    {
        var directory = context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory.");
        var result = await ProcessRunner.RunAsync("bun",
            [Path.Combine(ProcessRunner.RepositoryRoot, "tests/Nami.IntegrationTests/Fixtures/mcp-supervisor.ts"), scenario, directory],
            ProcessRunner.RepositoryRoot, context.CancellationToken);
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        return JsonDocument.Parse(result.Output);
    }
}
