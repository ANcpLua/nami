using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public sealed class AgentControlTests(TestContext context)
{
    [ClassInitialize]
    public static async Task Prepare(TestContext context)
    {
        var result = await ProcessRunner.RunAsync("bash", [Path.Combine(ProcessRunner.RepositoryRoot, "eng/agent.sh"), "prepare"],
            ProcessRunner.RepositoryRoot, context.CancellationToken);
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
    }

    [TestCleanup]
    public void Cleanup() => InspectionFixture.Cleanup(context);

    private async Task<ProcessResult> RunAsync(InspectionFixture fixture, params string[] args) =>
        await ProcessRunner.RunAsync("bash", [Path.Combine(ProcessRunner.RepositoryRoot, "eng/agent.sh"), .. args, "--root", fixture.Root],
            ProcessRunner.RepositoryRoot, context.CancellationToken, fixture.Environment);

    private async Task<string> PlanAsync(InspectionFixture fixture)
    {
        var result = await RunAsync(fixture, "plan");
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        Assert.Contains("AGENT planned", result.Output);
        return result.Output.Split('\n').Single(line => line.StartsWith("Plan: ", StringComparison.Ordinal))[6..];
    }

    private async Task WriteArtifactAsync(InspectionFixture fixture, string path, string text)
    {
        var file = Path.Combine(fixture.Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, text, context.CancellationToken);
    }

    private async Task<string> TypeSpecFixtureAsync(InspectionFixture fixture, bool invalid = false)
    {
        Directory.CreateDirectory(Path.Combine(fixture.Root, "contracts"));
        Directory.CreateSymbolicLink(Path.Combine(fixture.Root, "node_modules"), Path.Combine(ProcessRunner.RepositoryRoot, "contracts/node_modules"));
        var source = "namespace Nami.Api.Contracts.FixProbe;\nmodel Example {\n  badName: string;\n" +
                     (invalid ? "  unknown: MissingType;\n" : "") + "}\n";
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "contracts/main.tsp"), source, context.CancellationToken);
        var rulePath = "@ancplua/typespec-nami-lint/all";
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "contracts/tspconfig.yaml"),
            "linter:\n  extends:\n    - \"" + rulePath + "\"\n", context.CancellationToken);
        return source;
    }

    private async Task<string> NativePlanAsync(InspectionFixture fixture)
    {
        var result = await RunAsync(fixture, "tsp-plan", "contracts/main.tsp",
            "@ancplua/typespec-nami-lint/wire-name-snake-case", "nami-add-wire-name");
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        return result.Output.Split('\n').Single(line => line.StartsWith("Plan: ", StringComparison.Ordinal))[6..];
    }

    [TestMethod]
    public async Task AgentFix_TypeSpecCodeFix_AppliesAndTypeInfoIsReadOnly()
    {
        var fixture = await InspectionFixture.CreateAsync(context);
        var original = await TypeSpecFixtureAsync(fixture);
        var id = await NativePlanAsync(fixture);
        Assert.AreEqual(original, await File.ReadAllTextAsync(Path.Combine(fixture.Root, "contracts/main.tsp"), context.CancellationToken));
        var result = await RunAsync(fixture, "apply", id);
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        Assert.Contains("AGENT verified", result.Output);
        var updated = await File.ReadAllTextAsync(Path.Combine(fixture.Root, "contracts/main.tsp"), context.CancellationToken);
        Assert.Contains("@encodedName(\"application/json\", \"bad_name\")", updated);
        var info = await RunAsync(fixture, "type-info", "Nami.Api.Contracts.FixProbe.Example.badName");
        Assert.AreEqual(0, info.ExitCode, info.Output + info.Error);
        Assert.Contains("JSON key: `bad_name`", info.Output);
        Assert.AreEqual(updated, await File.ReadAllTextAsync(Path.Combine(fixture.Root, "contracts/main.tsp"), context.CancellationToken));
    }

    [TestMethod]
    public async Task AgentFix_TypeSpecValidationFailure_RollsBackSource()
    {
        var fixture = await InspectionFixture.CreateAsync(context);
        var original = await TypeSpecFixtureAsync(fixture);
        var id = await NativePlanAsync(fixture);
        var result = await ProcessRunner.RunAsync("bun",
            ["run", Path.Combine(ProcessRunner.RepositoryRoot, "tests/Nami.IntegrationTests/Fixtures/agent-rollback.ts"), fixture.Root, id, "compiler"],
            ProcessRunner.RepositoryRoot, context.CancellationToken, fixture.Environment);
        Assert.AreEqual(0, result.ExitCode, result.Error);
        using var data = JsonDocument.Parse(result.Output);
        Assert.AreEqual("rolledBack", data.RootElement.GetProperty("phase").GetString());
        Assert.AreEqual(2, data.RootElement.GetProperty("exitCode").GetInt32());
        Assert.AreEqual(original, await File.ReadAllTextAsync(Path.Combine(fixture.Root, "contracts/main.tsp"), context.CancellationToken));
    }

    [TestMethod]
    public async Task AgentFix_SuppressionIsNotAnExecutableFix()
    {
        var fixture = await InspectionFixture.CreateAsync(context);
        var original = await TypeSpecFixtureAsync(fixture);
        var result = await RunAsync(fixture, "tsp-plan", "contracts/main.tsp",
            "@ancplua/typespec-nami-lint/wire-name-snake-case", "suppress");
        Assert.AreEqual(2, result.ExitCode, result.Output + result.Error);
        Assert.Contains("unsafe-target", result.Output);
        Assert.AreEqual(original, await File.ReadAllTextAsync(Path.Combine(fixture.Root, "contracts/main.tsp"), context.CancellationToken));
    }

    [TestMethod]
    public async Task AgentPolicy_LeanKernelChecksProofs()
    {
        var fixture = await InspectionFixture.CreateAsync(context);
        var result = await RunAsync(fixture, "proof");
        Assert.AreEqual(0, result.ExitCode, result.Error);
        Assert.Contains("checked: apply ownership/revision", result.Output);
        Assert.Contains("rollback restoration", result.Output);
    }

    [TestMethod]
    public async Task AgentFix_VerifiedDeletion_PreservesSourceAndRejectsRepeat()
    {
        var fixture = await InspectionFixture.CreateAsync(context);
        await WriteArtifactAsync(fixture, "artifacts/logs/old.log", "old diagnostic");
        var source = await File.ReadAllTextAsync(Path.Combine(fixture.Root, "src/Example.cs"), context.CancellationToken);
        var id = await PlanAsync(fixture);
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Root, "artifacts/logs/old.log")));
        var applied = await RunAsync(fixture, "apply", id);
        Assert.AreEqual(0, applied.ExitCode, applied.Output + applied.Error);
        Assert.Contains("AGENT verified", applied.Output);
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Root, "artifacts/logs")));
        Assert.AreEqual(source, await File.ReadAllTextAsync(Path.Combine(fixture.Root, "src/Example.cs"), context.CancellationToken));
        var repeat = await RunAsync(fixture, "apply", id);
        Assert.AreEqual(2, repeat.ExitCode);
        Assert.DoesNotContain("AGENT verified", repeat.Output);
    }

    [TestMethod]
    [DataRow("source")]
    [DataRow("artifact")]
    [DataRow("ignored-model")]
    public async Task AgentFix_ChangedInput_RefusesWithoutDeleting(string kind)
    {
        var fixture = await InspectionFixture.CreateAsync(context);
        await WriteArtifactAsync(fixture, "artifacts/logs/old.log", "old");
        if (kind == "ignored-model")
        {
            await WriteArtifactAsync(fixture, "contracts/ignored.tsp", "model Before {}");
            await File.AppendAllTextAsync(Path.Combine(fixture.Root, ".gitignore"), "\nignored.tsp\n", context.CancellationToken);
        }
        var id = await PlanAsync(fixture);
        var path = Path.Combine(fixture.Root, kind == "source" ? "src/Example.cs" : kind == "ignored-model" ? "contracts/ignored.tsp" : "artifacts/logs/old.log");
        await File.WriteAllTextAsync(path, "new content", context.CancellationToken);
        var result = await RunAsync(fixture, "apply", id);
        Assert.AreEqual(2, result.ExitCode, result.Output + result.Error);
        Assert.Contains("@ancplua/nami-agent-control/stale-plan", result.Output);
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Root, "artifacts/logs/old.log")));
        Assert.AreEqual("new content", await File.ReadAllTextAsync(path, context.CancellationToken));
    }

    [TestMethod]
    public async Task AgentFix_TamperedPlan_CannotDeleteSource()
    {
        var fixture = await InspectionFixture.CreateAsync(context);
        await WriteArtifactAsync(fixture, "artifacts/logs/old.log", "old");
        var id = await PlanAsync(fixture);
        var plan = Directory.EnumerateFiles(fixture.Environment["NAMI_DIAGNOSTICS_HOME"]!, "plan.json", SearchOption.AllDirectories).Single();
        var text = await File.ReadAllTextAsync(plan, context.CancellationToken);
        await File.WriteAllTextAsync(plan, text.Replace("artifacts/logs", "src", StringComparison.Ordinal), context.CancellationToken);
        var result = await RunAsync(fixture, "apply", id);
        Assert.AreEqual(2, result.ExitCode, result.Output + result.Error);
        Assert.Contains("stale-plan", result.Output);
        Assert.AreEqual("class Example {}", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "src/Example.cs"), context.CancellationToken));
    }

    [TestMethod]
    public async Task AgentFix_ExternalArtifactLink_IsNotOwned()
    {
        var fixture = await InspectionFixture.CreateAsync(context);
        var external = Path.Combine(InspectionFixture.Temporary(context), "external");
        Directory.CreateDirectory(Path.Combine(external, "logs"));
        var protectedFile = Path.Combine(external, "logs/keep.log");
        await File.WriteAllTextAsync(protectedFile, "external data", context.CancellationToken);
        Directory.CreateSymbolicLink(Path.Combine(fixture.Root, "artifacts"), external);
        var id = await PlanAsync(fixture);
        var result = await RunAsync(fixture, "apply", id);
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        Assert.Contains("Owned targets: 0", result.Output);
        Assert.AreEqual("external data", await File.ReadAllTextAsync(protectedFile, context.CancellationToken));
    }

    [TestMethod]
    public async Task AgentFix_FilesystemFailure_RestoresDeletedArtifact()
    {
        var fixture = await InspectionFixture.CreateAsync(context);
        await WriteArtifactAsync(fixture, "artifacts/logs/old.log", "first");
        await WriteArtifactAsync(fixture, "artifacts/binlogs/old.binlog", "second");
        var id = await PlanAsync(fixture);
        var result = await ProcessRunner.RunAsync("bun",
            ["run", Path.Combine(ProcessRunner.RepositoryRoot, "tests/Nami.IntegrationTests/Fixtures/agent-rollback.ts"), fixture.Root, id],
            ProcessRunner.RepositoryRoot, context.CancellationToken, fixture.Environment);
        Assert.AreEqual(0, result.ExitCode, result.Error);
        using var data = JsonDocument.Parse(result.Output);
        Assert.AreEqual("rolledBack", data.RootElement.GetProperty("phase").GetString());
        Assert.AreEqual(2, data.RootElement.GetProperty("exitCode").GetInt32());
        Assert.AreEqual("first", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "artifacts/logs/old.log"), context.CancellationToken));
        Assert.AreEqual("second", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "artifacts/binlogs/old.binlog"), context.CancellationToken));
    }
}
