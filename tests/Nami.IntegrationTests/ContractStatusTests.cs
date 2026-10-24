using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public sealed class ContractStatusTests(TestContext context)
{
    [TestCleanup]
    public void Cleanup() => InspectionFixture.Cleanup(context);

    private async Task<(JsonDocument Data, string Root, string State)> RunAsync(string scenario)
    {
        var temporary = InspectionFixture.Temporary(context);
        var root = Path.Combine(temporary, "checkout");
        var state = Path.Combine(temporary, "state");
        var result = await ProcessRunner.RunAsync("node",
        [
            Path.Combine(ProcessRunner.RepositoryRoot, "tests/Nami.IntegrationTests/Fixtures/contract-status.mjs"),
            scenario, root, state,
            Path.Combine(ProcessRunner.RepositoryRoot, "contracts/scripts/lib/contract-status.mjs")
        ], ProcessRunner.RepositoryRoot, context.CancellationToken);
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        return (JsonDocument.Parse(result.Output), root, state);
    }

    [TestMethod]
    [DataRow("current", "CURRENT", 0, "UNCHANGED")]
    [DataRow("add", "GENERATION_REQUIRED", 1, "ADDITIONS")]
    [DataRow("remove", "GENERATION_REQUIRED", 1, "REMOVALS")]
    [DataRow("modify", "GENERATION_REQUIRED", 1, "MODIFICATIONS")]
    [DataRow("mixed", "GENERATION_REQUIRED", 1, "MIXED")]
    [DataRow("docs", "GENERATION_REQUIRED", 1, "UNCHANGED")]
    [DataRow("metadata", "CURRENT", 0, "UNCHANGED")]
    [DataRow("untracked", "CURRENT", 0, "UNAVAILABLE")]
    public async Task ContractStatus_ReportsChangesAndRestoresOutputs(string scenario, string status, int exitCode, string wire)
    {
        var (data, _, _) = await RunAsync(scenario);
        using (data)
        {
            var report = data.RootElement.GetProperty("report");
            Assert.AreEqual(status, report.GetProperty("status").GetString());
            Assert.AreEqual(exitCode, report.GetProperty("exitCode").GetInt32());
            Assert.AreEqual(wire, report.GetProperty("wire").GetProperty("status").GetString());
            Assert.AreEqual("NOT_ASSESSED", report.GetProperty("wire").GetProperty("compatibility").GetString());
            Assert.AreEqual("NOT_ASSESSED", report.GetProperty("wire").GetProperty("quality").GetString());
            Assert.IsTrue(data.RootElement.GetProperty("restored").GetBoolean());
            Assert.AreEqual(2, data.RootElement.GetProperty("calls").GetInt32());
            if (scenario == "add")
                Assert.Contains("/Widget/properties/count", report.GetProperty("wire").GetProperty("changes").GetRawText());
            if (scenario == "remove")
                Assert.Contains("original.cs", report.GetProperty("fileChanges").GetRawText());
        }
    }

    [TestMethod]
    public async Task ContractStatus_NondeterministicGeneration_IsRejected()
    {
        var (data, _, _) = await RunAsync("nondeterministic");
        using (data)
        {
            var report = data.RootElement.GetProperty("report");
            Assert.AreEqual("NONDETERMINISTIC", report.GetProperty("status").GetString());
            Assert.AreEqual(3, report.GetProperty("exitCode").GetInt32());
            Assert.AreEqual(1, report.GetProperty("reproducibilityChanges").GetArrayLength());
            Assert.IsTrue(data.RootElement.GetProperty("restored").GetBoolean());
        }
    }

    [TestMethod]
    public async Task ContractStatus_GeneratorFailure_PreservesExitAndRestoresOutputs()
    {
        var (data, _, _) = await RunAsync("failed");
        using (data)
        {
            var report = data.RootElement.GetProperty("report");
            Assert.AreEqual("GENERATION_FAILED", report.GetProperty("status").GetString());
            Assert.AreEqual(2, report.GetProperty("exitCode").GetInt32());
            Assert.AreEqual(17, report.GetProperty("generatorExitCode").GetInt32());
            Assert.IsTrue(data.RootElement.GetProperty("restored").GetBoolean());
        }
    }

    [TestMethod]
    public async Task ContractStatus_Generate_KeepsReviewedCandidateOutputs()
    {
        var (data, root, _) = await RunAsync("generate");
        using (data)
        {
            var report = data.RootElement.GetProperty("report");
            Assert.AreEqual("GENERATED", report.GetProperty("status").GetString());
            Assert.AreEqual(0, report.GetProperty("exitCode").GetInt32());
            Assert.IsFalse(data.RootElement.GetProperty("restored").GetBoolean());
            Assert.AreEqual("added source\n", await File.ReadAllTextAsync(
                Path.Combine(root, "internal/nami.contracts/Nami/added.cs"), context.CancellationToken));
        }
    }

    [TestMethod]
    public async Task ContractStatus_ChangedSource_InvalidatesRecordedSuccess()
    {
        var (data, root, state) = await RunAsync("stale");
        using (data)
        {
            Assert.AreEqual(0, data.RootElement.GetProperty("report").GetProperty("exitCode").GetInt32());
            var result = await ProcessRunner.RunAsync("node",
            [
                Path.Combine(ProcessRunner.RepositoryRoot, "contracts/scripts/verify-contracts.mjs"),
                "status", "--root", root, "--state", state, "--source", "after"
            ], ProcessRunner.RepositoryRoot, context.CancellationToken);
            Assert.AreEqual(2, result.ExitCode, result.Output + result.Error);
            Assert.Contains("CONTRACT STALE exit=2", result.Output);
            Assert.Contains("sources have changed", result.Output);
        }
    }

    [TestMethod]
    public async Task ContractStatus_Query_IsBoundedAndPreservesSignalExitCode()
    {
        var (data, root, state) = await RunAsync("add");
        using (data)
        {
            var result = await ProcessRunner.RunAsync("node",
            [
                Path.Combine(ProcessRunner.RepositoryRoot, "contracts/scripts/verify-contracts.mjs"),
                "status", "--root", root, "--state", state, "--limit", "1"
            ], ProcessRunner.RepositoryRoot, context.CancellationToken);
            Assert.AreEqual(1, result.ExitCode, result.Output + result.Error);
            Assert.Contains("CONTRACT GENERATION_REQUIRED exit=1", result.Output);
            Assert.Contains("wire=ADDITIONS", result.Output);
            Assert.Contains("More:", result.Output);
            Assert.DoesNotContain("wire added:", result.Output);
        }
    }
}
