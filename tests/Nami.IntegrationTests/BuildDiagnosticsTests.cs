using System.Text.Json.Nodes;
using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public sealed class BuildDiagnosticsTests(TestContext context)
{
    private const string ValidReport = """{"version":"2.1.0","runs":[{"tool":{"driver":{"name":"InspectCode"}},"invocations":[{"executionSuccessful":true}],"results":[]}]}""";
    private string Temporary => context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");

    private async Task<ProcessResult> ReportAsync(string? report = ValidReport, string log = "", int exitCode = 0)
    {
        var reportPath = Path.Combine(Temporary, "report.sarif");
        var logPath = Path.Combine(Temporary, "tool.log");
        if (report is not null) await File.WriteAllTextAsync(reportPath, report, context.CancellationToken);
        await File.WriteAllTextAsync(logPath, log, context.CancellationToken);
        return await ProcessRunner.RunAsync("python3",
            [Path.Combine(ProcessRunner.RepositoryRoot, "eng/scripts/inspect_report.py"), reportPath, logPath, exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            Temporary, context.CancellationToken);
    }

    [TestMethod]
    public async Task InspectReport_EmptySuccessfulInvocation_IsClean()
    {
        var result = await ReportAsync();
        Assert.AreEqual(0, result.ExitCode, result.Error);
        Assert.Contains("INSPECTCODE CLEAN: total=0", result.Output);
    }

    [TestMethod]
    public async Task InspectReport_Warning_ShowsRuleLocationAndFails()
    {
        var document = JsonNode.Parse(ValidReport)!;
        document["runs"]![0]!["results"] = JsonNode.Parse("""[{"ruleId":"MSTEST0052","level":"warning","message":{"text":"Remove DynamicDataSourceType"},"locations":[{"physicalLocation":{"artifactLocation":{"uri":"tests/HostTransportTests.cs"},"region":{"startLine":97,"startColumn":6}}}]}]""");
        var result = await ReportAsync(document.ToJsonString());
        Assert.AreEqual(1, result.ExitCode);
        Assert.Contains("WARNING MSTEST0052 x1 tests/HostTransportTests.cs:97:6: Remove DynamicDataSourceType", result.Output);
        Assert.Contains("total=1 errors=0 warnings=1 notes=0", result.Output);
    }

    [TestMethod]
    public async Task InspectReport_Note_IsAVisibleFinding()
    {
        var document = JsonNode.Parse(ValidReport)!;
        document["runs"]![0]!["results"] = JsonNode.Parse("""[{"ruleId":"UnusedMember.Global","level":"note","message":{"text":"Unused member"}}]""");
        var result = await ReportAsync(document.ToJsonString());
        Assert.AreEqual(1, result.ExitCode);
        Assert.Contains("INSPECTCODE FINDINGS", result.Output);
        Assert.Contains("notes=1", result.Output);
    }

    [TestMethod]
    public async Task InspectReport_RoslynLoadFailure_RejectsSuccessfulReport()
    {
        var result = await ReportAsync(log: "Roslyn error AssemblyLoading: missing Microsoft.CodeAnalysis.Features.ExternalAccess\n");
        Assert.AreEqual(2, result.ExitCode);
        Assert.Contains("TOOL ERROR: Roslyn error AssemblyLoading", result.Output);
        Assert.Contains("INSPECTCODE FAILED", result.Output);
    }

    [TestMethod]
    public async Task InspectReport_SourceGeneratorFailure_IsIncomplete()
    {
        var result = await ReportAsync(log: "Warning: Roslyn Source Generator error from RegexGenerator\n");
        Assert.AreEqual(2, result.ExitCode);
        Assert.Contains("TOOL ERROR: Warning: Roslyn Source Generator error", result.Output);
    }

    [TestMethod]
    public async Task InspectReport_FailedInvocation_IsNotClean()
    {
        var result = await ReportAsync(ValidReport.Replace("true", "false", StringComparison.Ordinal));
        Assert.AreEqual(2, result.ExitCode);
        Assert.Contains("successful analysis invocation", result.Output);
    }

    [TestMethod]
    public async Task InspectReport_MissingReport_IsNotSuccessful()
    {
        var result = await ReportAsync(report: null);
        Assert.AreEqual(2, result.ExitCode);
        Assert.Contains("Invalid or missing report", result.Output);
    }

    [TestMethod]
    public async Task InspectReport_NonzeroToolExit_IsPreserved()
    {
        var result = await ReportAsync(exitCode: 37);
        Assert.AreEqual(37, result.ExitCode);
        Assert.Contains("TOOL EXIT: 37", result.Output);
    }

    private async Task<ProcessResult> QuietAsync(string command)
    {
        var name = "diagnostics-regression-" + Guid.NewGuid().ToString("N");
        var result = await ProcessRunner.RunAsync("bash",
            [Path.Combine(ProcessRunner.RepositoryRoot, "eng/scripts/quiet.sh"), name, "--", "bash", "-c", command],
            Temporary, context.CancellationToken, new Dictionary<string, string?>
            {
                ["NAMI_VERBOSE"] = "0", ["NAMI_DIAGNOSTICS_ROOT"] = Path.Combine(Temporary, "diagnostics")
            });
        File.Delete(Path.Combine(Temporary, "diagnostics/build/current", name + ".log"));
        return result;
    }

    [TestMethod]
    public async Task QuietOutput_SuccessfulBuildWarning_RemainsVisible()
    {
        var result = await QuietAsync("printf 'tests/HostTransportTests.cs(97,6): warning MSTEST0052: Remove DynamicDataSourceType\\n'");
        Assert.AreEqual(0, result.ExitCode);
        Assert.Contains("warning MSTEST0052", result.Output);
        Assert.Contains("PASS WITH DIAGNOSTICS", result.Output);
    }

    [TestMethod]
    public async Task QuietOutput_FailedCommand_PreservesExitAndError()
    {
        var result = await QuietAsync("printf 'project.csproj : error MSB4019: Missing import\\n'; exit 23");
        Assert.AreEqual(23, result.ExitCode);
        Assert.Contains("error MSB4019: Missing import", result.Output);
        Assert.Contains("FAIL diagnostics-regression", result.Output);
    }

    [TestMethod]
    public async Task QuietOutput_UncodedToolError_RemainsVisible()
    {
        var result = await QuietAsync("printf '12:34:56 [DBG]   xcode-select: error: tool requires Xcode\\n'");
        Assert.AreEqual(0, result.ExitCode);
        Assert.Contains("xcode-select: error: tool requires Xcode", result.Output);
        Assert.Contains("PASS WITH DIAGNOSTICS", result.Output);
    }

    [TestMethod]
    public async Task InspectWrapper_ForeignDirectory_DoesNotReusePreviousReport()
    {
        var fixture = await InspectionFixture.CreateAsync(context);
        var first = await fixture.RunAsync([]);
        Assert.AreEqual(0, first.ExitCode, first.Output + first.Error);
        var firstRun = fixture.LatestRun();
        fixture.Environment["FAKE_OMIT_REPORT"] = "1";
        var second = await fixture.RunAsync(["--no-build"]);
        Assert.AreEqual(2, second.ExitCode, second.Output + second.Error);
        Assert.Contains("INSPECT ANALYSIS_FAILED", second.Output);
        Assert.AreEqual(firstRun, fixture.LatestRun());
        Assert.IsFalse(File.Exists(Path.Combine(firstRun, "inspectcode.sarif")));
    }

    [TestMethod]
    public async Task TestEntry_IgnoredMtpExitCodes_IsRefused()
    {
        var result = await ProcessRunner.RunAsync("bash",
            [Path.Combine(ProcessRunner.RepositoryRoot, "eng/test.sh"), "--suite", "Unit", "--no-build"],
            Temporary, context.CancellationToken, new Dictionary<string, string?> { ["TESTINGPLATFORM_EXITCODE_IGNORE"] = "2;8" });
        Assert.AreEqual(10, result.ExitCode, result.Output + result.Error);
        Assert.Contains("TESTINGPLATFORM_EXITCODE_IGNORE", result.Error);
    }

    private async Task<ProcessResult> RunWithRestoreStateAsync(bool includeImports, bool completePackage)
    {
        var root = Path.Combine(Temporary, "checkout");
        var extensions = Path.Combine(root, "obj");
        var packageRoot = Path.Combine(root, "packages");
        var package = Path.Combine(packageRoot, "example", "1.0");
        var assembly = Path.Combine(root, "Nami.UnitTests.dll");
        Directory.CreateDirectory(Path.Combine(root, "eng"));
        Directory.CreateDirectory(extensions);
        Directory.CreateDirectory(package);
        File.Copy(Path.Combine(ProcessRunner.RepositoryRoot, "eng/test-suites.json"), Path.Combine(root, "eng/test-suites.json"));
        await File.WriteAllTextAsync(assembly, "fixture", context.CancellationToken);
        if (includeImports)
        {
            await File.WriteAllTextAsync(Path.Combine(extensions, "Nami.UnitTests.csproj.nuget.g.props"), "<Project />", context.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(extensions, "Nami.UnitTests.csproj.nuget.g.targets"), "<Project />", context.CancellationToken);
        }
        if (completePackage)
            await File.WriteAllTextAsync(Path.Combine(package, "example.nuspec"), "fixture", context.CancellationToken);
        var assets = new JsonObject
        {
            ["packageFolders"] = new JsonObject { [packageRoot] = new JsonObject() },
            ["libraries"] = new JsonObject
            {
                ["Example/1.0"] = new JsonObject
                {
                    ["type"] = "package", ["path"] = "example/1.0", ["files"] = new JsonArray("example.nuspec")
                }
            }
        };
        var assetsPath = Path.Combine(extensions, "project.assets.json");
        await File.WriteAllTextAsync(assetsPath, assets.ToJsonString(), context.CancellationToken);
        var evaluation = new JsonObject
        {
            ["Properties"] = new JsonObject
            {
                ["IsTestingPlatformApplication"] = "true", ["TargetPath"] = assembly,
                ["ProjectAssetsFile"] = assetsPath, ["MSBuildProjectExtensionsPath"] = extensions
            }
        };
        var dotnet = Path.Combine(root, "dotnet");
        await File.WriteAllTextAsync(dotnet, $$"""
        #!/bin/sh
        [ "$1" = "msbuild" ] || { echo 'Unexpected test discovery' >&2; exit 99; }
        cat <<'NAMI_PROPERTIES'
        {{evaluation.ToJsonString()}}
        NAMI_PROPERTIES
        """, context.CancellationToken);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(dotnet, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return await ProcessRunner.RunAsync("python3",
        [
            Path.Combine(ProcessRunner.RepositoryRoot, "eng/scripts/run_tests.py"), "--root", root, "--dotnet", dotnet,
            "--suite", "Unit", "--no-build"
        ], Temporary, context.CancellationToken,
            new Dictionary<string, string?> { ["NAMI_DIAGNOSTICS_HOME"] = Path.Combine(Temporary, "diagnostics") });
    }

    [TestMethod]
    public async Task TestEntry_MissingNuGetImports_IsExplicitSetupFailure()
    {
        var result = await RunWithRestoreStateAsync(includeImports: false, completePackage: true);
        Assert.AreEqual(10, result.ExitCode, result.Output + result.Error);
        Assert.Contains("Missing restore/build state", result.Error);
        Assert.Contains("Nami.UnitTests.csproj.nuget.g.props", result.Error);
        Assert.DoesNotContain("Unexpected test discovery", result.Error);
    }

    [TestMethod]
    public async Task TestEntry_IncompleteGlobalPackages_IsExplicitSetupFailure()
    {
        var result = await RunWithRestoreStateAsync(includeImports: true, completePackage: false);
        Assert.AreEqual(10, result.ExitCode, result.Output + result.Error);
        Assert.Contains("Global package Example/1.0 is incomplete", result.Error);
        Assert.Contains("example.nuspec", result.Error);
        Assert.DoesNotContain("Unexpected test discovery", result.Error);
    }

    [TestMethod]
    public async Task TestEntry_EmptySelection_PreservesMinimumFailure()
    {
        var result = await ProcessRunner.RunAsync("bash",
            [Path.Combine(ProcessRunner.RepositoryRoot, "eng/test.sh"), "--suite", "Integration", "--no-build", "--filter", "FullyQualifiedName~Nami.NoSuchCase"],
            Temporary, context.CancellationToken);
        Assert.AreEqual(9, result.ExitCode, result.Output + result.Error);
        Assert.Contains("minimum", result.Output.ToLowerInvariant());
    }

    [TestMethod]
    public async Task TestStatus_DiagnosticEvents_NamesUnfinishedCase()
    {
        await File.WriteAllTextAsync(Path.Combine(Temporary, "progress.diag"),
            "mstest-trace INFORMATION Executing test FinishedCase\nmstest-trace INFORMATION Executed test FinishedCase\nmstest-trace INFORMATION Executing test PendingCase\n", context.CancellationToken);
        var result = await ProcessRunner.RunAsync("bash",
            [Path.Combine(ProcessRunner.RepositoryRoot, "eng/test.sh"), "--status", Temporary],
            Temporary, context.CancellationToken);
        Assert.AreEqual(0, result.ExitCode, result.Error);
        Assert.Contains("completed=1 pending=PendingCase", result.Output);
    }
}
