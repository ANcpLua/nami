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
