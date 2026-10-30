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
        Assert.AreEqual("invalid_argument", result.RootElement.GetProperty("refusalCode").GetString());
        Assert.IsTrue(result.RootElement.GetProperty("pendingSucceeded").GetBoolean());
        Assert.IsTrue(result.RootElement.GetProperty("subsequentSucceeded").GetBoolean());
        Assert.IsTrue(result.RootElement.GetProperty("sameChild").GetBoolean());
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task Backpressure_CancellationSettlesWithoutUnhandledRejection()
    {
        using var result = await RunAsync("backpressure");
        Assert.AreEqual("rejected", result.RootElement.GetProperty("settled").GetString());
        Assert.AreEqual(0, result.RootElement.GetProperty("unhandledRejections").GetInt32());
        Assert.IsTrue(result.RootElement.GetProperty("cancelForwarded").GetBoolean());
        Assert.IsTrue(result.RootElement.GetProperty("subsequentSucceeded").GetBoolean());
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task FailedEngine_CloseReapsSigtermResistantChild()
    {
        using var result = await RunAsync("failure");
        Assert.IsTrue(result.RootElement.GetProperty("termReceived").GetBoolean());
        Assert.IsTrue(result.RootElement.GetProperty("childExited").GetBoolean());
        Assert.IsTrue(result.RootElement.GetProperty("sameShutdown").GetBoolean());
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task Http_ClosedStdin_RemainsAvailableUntilSignal()
    {
        using var result = await RunAsync("http");
        Assert.AreEqual(200, result.RootElement.GetProperty("healthStatus").GetInt32());
        Assert.AreEqual(200, result.RootElement.GetProperty("toolStatus").GetInt32());
        Assert.IsTrue(result.RootElement.GetProperty("aliveAfterEof").GetBoolean());
        Assert.IsTrue(result.RootElement.GetProperty("childExitedAfterSignal").GetBoolean());
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task Stdio_Eof_ShutsDownOwnedEngine()
    {
        using var result = await RunAsync("stdio");
        Assert.IsTrue(result.RootElement.GetProperty("exitedOnEof").GetBoolean());
        Assert.IsTrue(result.RootElement.GetProperty("childExited").GetBoolean());
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
