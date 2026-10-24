using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public sealed class InspectionWorkflowTests(TestContext context)
{
    [TestCleanup]
    public void Cleanup() => InspectionFixture.Cleanup(context);

    [TestMethod]
    public async Task Diagnostics_Cleanup_RemovesReportsAndPreservesSourceAndBuilds()
    {
        var fixture = await InspectionFixture.CreateAsync(context);
        string[] discarded = ["artifacts/inspection/old/report.sarif", "artifacts/test-results/old/result.trx",
            "artifacts/binlogs/old.binlog", "artifacts/logs/old.log", ".fallout/temp/build.log",
            "artifacts/bin/Nami.IntegrationTests/release/Mock_00000000000000000000000000000000/report.json"];
        string[] preserved = ["src/Example.cs", "artifacts/bin/Example.dll", "artifacts/obj/project.assets.json",
            "artifacts/publish/nami", ".fallout/parameters.json", ".agents/guide.md",
            "artifacts/bin/Nami.IntegrationTests/release/Nami.IntegrationTests.dll"];
        foreach (var path in discarded.Concat(preserved))
        {
            var full = Path.Combine(fixture.Root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, "preserved content", context.CancellationToken);
        }
        var result = await ProcessRunner.RunAsync("python3",
            [Path.Combine(fixture.Root, "eng/scripts/diagnostics.py"), "clean", fixture.Root],
            fixture.Root, context.CancellationToken);
        Assert.AreEqual(0, result.ExitCode, result.Error);
        foreach (var path in discarded) Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, path)), path);
        foreach (var path in preserved)
            Assert.AreEqual("preserved content", await File.ReadAllTextAsync(Path.Combine(fixture.Root, path), context.CancellationToken), path);
    }

    [TestMethod]
    [DataRow(".")]
    [DataRow("diagnostic-data")]
    public async Task Diagnostics_InternalStorage_IsRejected(string path)
    {
        var fixture = await InspectionFixture.CreateAsync(context);
        fixture.Environment["NAMI_DIAGNOSTICS_HOME"] = Path.Combine(fixture.Root, path);
        var result = await fixture.RunAsync([]);
        Assert.AreEqual(2, result.ExitCode, result.Output + result.Error);
        Assert.Contains("outside the repository", result.Error);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "artifacts/commands.jsonl")));
    }

    [TestMethod]
    public async Task Inspection_PinsToolAndLoadsSolutionForProject()
    {
        var fixture = await InspectionFixture.CreateAsync(context);
        var result = await fixture.RunAsync(["--project", "Example"]);
        Assert.IsFalse(fixture.Root.StartsWith(ProcessRunner.RepositoryRoot, StringComparison.Ordinal));
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        var commands = fixture.Commands();
        var inspection = commands.Single(command => command.Contains("inspectcode"));
        Assert.Contains("tool", inspection);
        Assert.Contains("run", inspection);
        Assert.Contains("jb", inspection);
        Assert.Contains(Path.Combine(fixture.Root, "nami.slnx"), inspection);
        Assert.Contains("--project=Example", inspection);
        Assert.Contains("--dotnetcoresdk=11.0.100-test", inspection);
        Assert.Contains("--no-build", inspection);
        Assert.Contains("--jobs=2", inspection);
        Assert.Contains("--disable-settings-layers=GlobalAll;GlobalPerProduct;SolutionPersonal;ProjectPersonal", inspection);
        Assert.Contains("SDK 11.0.100-test; InspectCode 2026.3.0-eap07", result.Output);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "global-jb-called")));
    }

    [TestMethod]
    public async Task Inspection_BoundedOutputAndQuery_PreserveAllFindings()
    {
        var fixture = await InspectionFixture.CreateAsync(context);
        fixture.Environment["FAKE_FINDINGS"] = "30";
        fixture.Environment["FAKE_MIXED_RULES"] = "1";
        var result = await fixture.RunAsync(["--file", "src/Example.cs", "--limit", "2"]);
        Assert.AreEqual(1, result.ExitCode, result.Output + result.Error);
        Assert.Contains("Findings: 30", result.Output);
        Assert.AreEqual(2, result.Output.Split('\n').Count(line => line.StartsWith("WARNING Rule", StringComparison.Ordinal)));
        Assert.Contains("src/Example.cs:1:1", result.Output);
        Assert.DoesNotContain("src/Example.cs:3:1", result.Output);
        var run = fixture.LatestRun();
        Assert.AreEqual(30, File.ReadAllLines(Path.Combine(run, "findings.jsonl")).Length);
        var before = fixture.Commands().Length;
        var query = await fixture.RunAsync(["--results", run, "--rule", "RuleA", "--offset", "1", "--limit", "2"]);
        Assert.AreEqual(1, query.ExitCode, query.Output + query.Error);
        Assert.Contains("src/Example.cs:3:1", query.Output);
        Assert.Contains("src/Example.cs:5:1", query.Output);
        Assert.DoesNotContain("WARNING RuleB", query.Output);
        Assert.DoesNotContain("src/Example.cs:1:1", query.Output);
        Assert.AreEqual(before, fixture.Commands().Length);
    }

    [TestMethod]
    public async Task Inspection_NoBuild_ReusesOnlyPreparedInputs()
    {
        var fixture = await InspectionFixture.CreateAsync(context);
        Assert.AreEqual(0, (await fixture.RunAsync([])).ExitCode);
        Assert.AreEqual(0, (await fixture.RunAsync(["--no-build"])).ExitCode);
        Assert.AreEqual(1, fixture.Commands().Count(command => command[0] == "build"));
        Assert.AreEqual(2, fixture.Commands().Count(command => command.Contains("inspectcode")));
    }

    [TestMethod]
    [DataRow("source")]
    [DataRow("output")]
    [DataRow("missing")]
    public async Task Inspection_NoBuild_RejectsChangedOrMissingState(string change)
    {
        var fixture = await InspectionFixture.CreateAsync(context);
        Assert.AreEqual(0, (await fixture.RunAsync([])).ExitCode);
        var run = fixture.LatestRun();
        var path = change == "source" ? Path.Combine(fixture.Root, "src/Example.cs") : fixture.Output;
        if (change == "missing") File.Delete(path);
        else await File.AppendAllTextAsync(path, "changed", context.CancellationToken);
        var result = await fixture.RunAsync(["--no-build"]);
        Assert.AreEqual(2, result.ExitCode, result.Output + result.Error);
        Assert.Contains("No matching prepared build", result.Output);
        Assert.AreEqual(1, fixture.Commands().Count(command => command.Contains("inspectcode")));
        if (change == "source")
        {
            var query = await fixture.RunAsync(["--results", run]);
            Assert.AreEqual(2, query.ExitCode);
            Assert.Contains("INSPECT SETUP_FAILED", query.Output);
        }
    }

    [TestMethod]
    [DataRow("source")]
    [DataRow("output")]
    [DataRow("ignored source")]
    public async Task Inspection_ConcurrentMutation_InvalidatesResult(string change)
    {
        var fixture = await InspectionFixture.CreateAsync(context);
        fixture.Environment["FAKE_MUTATION"] = change;
        var result = await fixture.RunAsync([]);
        Assert.AreEqual(2, result.ExitCode, result.Output + result.Error);
        Assert.Contains("INSPECT ANALYSIS_FAILED", result.Output);
        Assert.Contains("changed during analysis", result.Output);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Diagnostics, "inspection/dotnet/prepared-Release.json")));
    }

    [TestMethod]
    public async Task Inspection_SdkMismatch_DoesNotBuild()
    {
        var fixture = await InspectionFixture.CreateAsync(context);
        fixture.Environment["FAKE_SDK"] = "10.0.999";
        var result = await fixture.RunAsync([]);
        Assert.AreEqual(2, result.ExitCode, result.Output + result.Error);
        Assert.Contains("SDK mismatch", result.Output);
        Assert.IsFalse(fixture.Commands().Any(command => command[0] == "build"));
    }

    [TestMethod]
    [DataRow("--project", "Unknown")]
    [DataRow("--file", "global.json")]
    public async Task Inspection_UnknownScope_DoesNotPretendToBeClean(string option, string value)
    {
        var fixture = await InspectionFixture.CreateAsync(context);
        var result = await fixture.RunAsync([option, value]);
        Assert.AreEqual(2, result.ExitCode, result.Output + result.Error);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "artifacts/commands.jsonl")));
        Assert.DoesNotContain("INSPECT CLEAN", result.Output);
    }

    [TestMethod]
    public async Task Inspection_SuccessfulBuildWarning_RemainsSeparateFromCleanAnalysis()
    {
        var fixture = await InspectionFixture.CreateAsync(context);
        fixture.Environment["FAKE_WARNING"] = "1";
        var result = await fixture.RunAsync([]);
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        Assert.Contains("BUILD: src/Example.cs(1,1): warning CS0168", result.Output);
        Assert.Contains("INSPECT CLEAN", result.Output);
        Assert.Contains("warnings=0", result.Output);
    }

    [TestMethod]
    public async Task Inspection_BuildFailure_ReportsCompilerDiagnosticWithoutInspecting()
    {
        var fixture = await InspectionFixture.CreateAsync(context);
        fixture.Environment["FAKE_BUILD_FAIL"] = "1";
        var result = await fixture.RunAsync([]);
        Assert.AreEqual(23, result.ExitCode, result.Output + result.Error);
        Assert.Contains("INSPECT BUILD_FAILED", result.Output);
        Assert.Contains("error CS7036", result.Output);
        Assert.IsFalse(fixture.Commands().Any(command => command.Contains("inspectcode")));
    }

    [TestMethod]
    [DataRow("Warning: Unable to resolve reference Example: output assembly was not found")]
    [DataRow("Referenced project 'Example' not found in the solution")]
    [DataRow("[MsBuild] Example.cs (1,1) CS7036: missing value")]
    public async Task Inspection_InvalidAnalysisLog_RejectsCleanSarif(string log)
    {
        var fixture = await InspectionFixture.CreateAsync(context);
        fixture.Environment["FAKE_LOG"] = log;
        var result = await fixture.RunAsync([]);
        Assert.AreEqual(2, result.ExitCode, result.Output + result.Error);
        Assert.Contains("INSPECT ANALYSIS_FAILED", result.Output);
        Assert.Contains(log, result.Output);
    }

    [TestMethod]
    public async Task Inspection_WorkspaceLock_RejectsOverlapAndAllowsNestedEntry()
    {
        var fixture = await InspectionFixture.CreateAsync(context);
        fixture.Environment["FAKE_WAIT"] = "1";
        var running = fixture.RunAsync([]);
        var started = Path.Combine(fixture.Root, "artifacts/started");
        while (!File.Exists(started))
        {
            if (running.IsCompleted) Assert.Fail((await running).Output);
            await Task.Delay(20, context.CancellationToken);
        }
        try
        {
            var overlap = await fixture.RunAsync([]);
            Assert.AreEqual(2, overlap.ExitCode, overlap.Output + overlap.Error);
            Assert.Contains("VERIFICATION BUSY", overlap.Error);
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "artifacts/release"), "go", context.CancellationToken);
        }
        Assert.AreEqual(0, (await running).ExitCode);
        fixture.Environment.Remove("FAKE_WAIT");
        var nested = await ProcessRunner.RunAsync("python3",
            [Path.Combine(fixture.Root, "eng/scripts/workspace_lock.py"), fixture.Root, "/bin/bash",
                Path.Combine(fixture.Root, "eng/inspect.sh"), "--no-build"],
            fixture.Root, context.CancellationToken, fixture.Environment);
        Assert.AreEqual(0, nested.ExitCode, nested.Output + nested.Error);
    }
}

internal sealed class InspectionFixture(string root, TestContext context)
{
    private static readonly ConditionalWeakTable<TestContext, DirectoryInfo> TemporaryDirectories = new();

    internal static string Temporary(TestContext context) => TemporaryDirectories.GetValue(context, _ =>
        Directory.CreateDirectory(Path.Combine(System.Environment.GetEnvironmentVariable("NAMI_DIAGNOSTICS_ROOT")
                                               ?? Path.GetTempPath(), "test-scratch-" + Guid.NewGuid().ToString("N")))).FullName;

    internal static void Cleanup(TestContext context)
    {
        if (!TemporaryDirectories.TryGetValue(context, out var directory)) return;
        if (directory.Exists) directory.Delete(true);
        TemporaryDirectories.Remove(context);
    }

    internal string Root => root;
    internal string Output => Path.Combine(root, "artifacts/bin/Example/release/Example.dll");
    internal Dictionary<string, string?> Environment { get; } = new();
    internal Task<ProcessResult> RunAsync(string[] arguments) => ProcessRunner.RunAsync("bash",
        [Path.Combine(root, "eng/inspect.sh"), .. arguments], Path.GetTempPath(), context.CancellationToken, Environment);
    internal string Diagnostics => Directory.GetDirectories(Environment["NAMI_DIAGNOSTICS_HOME"]!).Single();
    internal string LatestRun() => Path.Combine(Diagnostics, "inspection/dotnet/current");
    internal string[][] Commands() => File.ReadAllLines(Path.Combine(root, "artifacts/commands.jsonl"))
        .Select(ReadCommand).ToArray();

    private static string[] ReadCommand(string line)
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.EnumerateArray()
            .Select(value => value.GetString() ?? throw new InvalidDataException()).ToArray();
    }

    internal static async Task<InspectionFixture> CreateAsync(TestContext context)
    {
        var root = Path.Combine(Temporary(context), "checkout");
        Directory.CreateDirectory(Path.Combine(root, "eng/scripts"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, ".config"));
        foreach (var file in new[] { "eng/inspect.sh", "eng/scripts/inspect_code.py", "eng/scripts/inspect_report.py",
                     "eng/scripts/workspace_lock.py", "eng/scripts/diagnostics.py", "eng/scripts/toolchain.py", "eng/scripts/limits.sh", ".config/dotnet-tools.json" })
            File.Copy(Path.Combine(ProcessRunner.RepositoryRoot, file), Path.Combine(root, file));
        await File.WriteAllTextAsync(Path.Combine(root, ".gitignore"), "artifacts/\n__pycache__/\nignored.cs\n", context.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "global.json"), """{"sdk":{"version":"11.0.100-test"}}""", context.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "mise.toml"), "[tools]\ndotnet = \"11.0.100-test\"\n", context.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "nami.slnx"), """<Solution><Project Path="src/Example.csproj"/></Solution>""", context.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "src/Example.csproj"), "<Project />", context.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "src/Example.cs"), "class Example {}", context.CancellationToken);
        var dotnet = Path.Combine(root, "dotnet");
        await File.WriteAllTextAsync(dotnet, """
            #!/usr/bin/env python3
            import json, os, sys, time
            from pathlib import Path
            args = sys.argv[1:]
            root = Path(__file__).parent
            (root/'artifacts').mkdir(exist_ok=True)
            with (root/'artifacts/commands.jsonl').open('a') as f: f.write(json.dumps(args)+'\n')
            if args == ['--version']:
                print(os.environ.get('FAKE_SDK', '11.0.100-test')); sys.exit(0)
            if args[:2] == ['tool','restore']: sys.exit(0)
            output = root/'artifacts/bin/Example/release/Example.dll'
            if args[0] == 'build':
                if os.environ.get('FAKE_BUILD_FAIL'):
                    print('src/Example.cs(1,1): error CS7036: missing argument'); sys.exit(23)
                if os.environ.get('FAKE_WARNING'): print('src/Example.cs(1,1): warning CS0168: unused variable')
                output.parent.mkdir(parents=True, exist_ok=True); output.write_text('assembly'); sys.exit(0)
            if args[:5] != ['tool','run','jb','--','inspectcode']: sys.exit(99)
            if os.environ.get('FAKE_WAIT'):
                (root/'artifacts/started').touch()
                while not (root/'artifacts/release').exists(): time.sleep(.01)
            if os.environ.get('FAKE_MUTATION') == 'source': (root/'src/Example.cs').write_text('class Changed {}')
            if os.environ.get('FAKE_MUTATION') == 'output': output.write_text('different assembly')
            if os.environ.get('FAKE_MUTATION') == 'ignored source': (root/'src/ignored.cs').write_text('class Ignored {}')
            if os.environ.get('FAKE_OMIT_REPORT'): sys.exit(0)
            print(os.environ.get('FAKE_LOG', ''))
            findings = [{'ruleId':'RuleA','level':'warning','message':{'text':'Fix this'},'locations':[{'physicalLocation':{'artifactLocation':{'uri':'src/Example.cs'},'region':{'startLine':i+1,'startColumn':1}}}]} for i in range(int(os.environ.get('FAKE_FINDINGS','0')))]
            if os.environ.get('FAKE_MIXED_RULES'):
                for i, finding in enumerate(findings): finding['ruleId'] = 'RuleA' if i % 2 == 0 else 'RuleB'
                findings.append({'ruleId':'OtherFile','level':'error','message':{'text':'Outside requested file'},'locations':[{'physicalLocation':{'artifactLocation':{'uri':'src/Other.cs'}}}]})
            report = {'version':'2.1.0','runs':[{'tool':{'driver':{'name':'InspectCode'}},'invocations':[{'executionSuccessful':True}],'results':findings}]}
            Path(next(a.split('=',1)[1] for a in args if a.startswith('--output='))).write_text(json.dumps(report))
            """, context.CancellationToken);
        var jb = Path.Combine(root, "jb");
        await File.WriteAllTextAsync(jb, "#!/bin/sh\ntouch global-jb-called\nexit 99\n", context.CancellationToken);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(dotnet, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(jb, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        foreach (var args in new[] { new[] { "init", "-q" }, new[] { "add", "." },
                     new[] { "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "-qm", "fixture" } })
        {
            var result = await ProcessRunner.RunAsync("git", args, root, context.CancellationToken);
            Assert.AreEqual(0, result.ExitCode, result.Error);
        }
        var fixture = new InspectionFixture(root, context);
        fixture.Environment["DOTNET_EXE"] = dotnet;
        fixture.Environment["NAMI_DIAGNOSTICS_HOME"] = Path.Combine(Temporary(context), "diagnostics");
        fixture.Environment["PATH"] = root + Path.PathSeparator + System.Environment.GetEnvironmentVariable("PATH");
        return fixture;
    }
}
