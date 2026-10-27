using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace Nami.IntegrationTests.Probes;

[TestClass]
public sealed class InvocationContractTests(TestContext context)
{
    private static readonly XNamespace s_trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
    private static string FixtureDirectory => Path.Combine(AppContext.BaseDirectory, "fixtures", "InvocationProbe");
    private static string ProbeAssembly => Path.Combine(FixtureDirectory, "InvocationProbe.dll");

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task TraceScope_ExecutorRowsFailuresAndInconclusiveResults_SurviveUnchanged()
    {
        var (exitCode, error, results) = await RunReportedAsync("ClassName=Nami.IntegrationTests.Probes.ProbeCases");
        Assert.AreEqual(2, exitCode, error);
        Assert.HasCount(5, results);
        Assert.AreAllDistinct(results.Select(item => (string?)item.Attribute("testId")));
        var passed = results.Where(item => (string?)item.Attribute("outcome") == "Passed").ToArray();
        Assert.HasCount(2, passed);
        Assert.AreSequenceEqual([ProbeContract.FirstRow, ProbeContract.SecondRow],
            passed.Select(item => item.Value.Split(ProbeContract.RowMarker + "|")[1].Split('|')[0]),
            SequenceOrder.InAnyOrder);
        Assert.AreSequenceEqual([1, 1],
            passed.Select(item => item.Value.Split(ProbeContract.ExecutorMarker).Length - 1));

        var rowIdentities = passed.Select(item => item.Value.Split(ProbeContract.RowMarker + "|")[1]
            .Split('|')[1].Split('\r', '\n')[0]).ToArray();
        Assert.AreAllDistinct(rowIdentities);
        Assert.AreSequenceEqual([32, 32], rowIdentities.Select(identity => identity.Length));
        var assertion = Assert.ContainsSingle(ResultsFor(results, ProbeContract.AssertionMethod));
        Assert.AreEqual("Failed", (string?)assertion.Attribute("outcome"));
        Assert.Contains(ProbeContract.AssertionMessage, assertion.Value);
        var exception = Assert.ContainsSingle(ResultsFor(results, ProbeContract.ExceptionMethod));
        Assert.AreEqual("Failed", (string?)exception.Attribute("outcome"));
        Assert.Contains(ProbeContract.ExceptionMessage, exception.Value);
        var inconclusive = Assert.ContainsSingle(ResultsFor(results, ProbeContract.InconclusiveMethod));
        Assert.AreEqual("NotExecuted", (string?)inconclusive.Attribute("outcome"));
        Assert.Contains(ProbeContract.InconclusiveMessage, inconclusive.Value);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task TraceScopePolicies_ViolationsOrMissingEvidence_FailWhileOriginalOutcomesStay()
    {
        var (exitCode, error, results) = await RunReportedAsync("FullyQualifiedName~PolicyCases");
        Assert.AreEqual(2, exitCode, error);
        Assert.HasCount(6, results);
        Assert.HasCount(2,
            ResultsFor(results, PolicyContract.SatisfiedMethod)
                .Where(item => (string?)item.Attribute("outcome") == "Passed"));
        var wrapped = Assert.ContainsSingle(ResultsFor(results, PolicyContract.WrappedMethod));
        Assert.AreEqual("Failed", (string?)wrapped.Attribute("outcome"));
        Assert.Contains("Telemetry policy violated (1):", wrapped.Value);
        Assert.Contains($"[NoDuplicateSpans] '{PolicyContract.Operation}' (Server, policy-probe, policy-",
            wrapped.Value);
        Assert.Contains("repeats its parent", wrapped.Value);
        var evidence = Assert.ContainsSingle(wrapped.Descendants(s_trx + "ResultFile"));
        Assert.EndsWith("telemetry-evidence.txt", (string?)evidence.Attribute("path"));
        var missing = Assert.ContainsSingle(ResultsFor(results, PolicyContract.MissingMethod));
        Assert.AreEqual("Failed", (string?)missing.Attribute("outcome"));
        Assert.Contains("Telemetry policy violated (2):", missing.Value);
        Assert.Contains("[ConnectedTrace] No trace was published.", missing.Value);
        Assert.Contains("[ExportsCleanly] No trace was published.", missing.Value);
        var original = Assert.ContainsSingle(ResultsFor(results, PolicyContract.OriginalMethod));
        Assert.AreEqual("Failed", (string?)original.Attribute("outcome"));
        Assert.Contains(PolicyContract.OriginalMessage, original.Value);
        Assert.DoesNotContain("Telemetry policy violated", original.Value);
        var unverified = Assert.ContainsSingle(ResultsFor(results, PolicyContract.UnverifiedMethod));
        Assert.AreEqual("Failed", (string?)unverified.Attribute("outcome"));
        Assert.Contains("Nami.IntegrationTests.Probes.UnverifiedPolicyCases "
                        + "declares [ConnectedTrace], but only a [TracePerTest] class verifies telemetry policies.",
            unverified.Value);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task Discovery_EachDataRow_ReportsTheOriginalMethodSource()
    {
        var result = await RunAsync("--list-tests", "json");
        Assert.AreEqual(0, result.ExitCode, result.Error);
        using var discovery = JsonDocument.Parse(result.Output);
        var rows = discovery.RootElement.GetProperty("tests").EnumerateArray().Where(item =>
            item.GetProperty("type").GetProperty("methodName").GetString() == ProbeContract.RowMethod).ToArray();
        Assert.HasCount(2, rows);
        var expectedLine = await ProbeMethodLineAsync();
        Assert.IsGreaterThan(0, expectedLine);
        Assert.AreSequenceEqual(
            Enumerable.Repeat(((string?)ProbeContract.CasesSourcePath, expectedLine, expectedLine), 2),
            rows.Select(row => row.GetProperty("location")).Select(location =>
                (location.GetProperty("file").GetString(), location.GetProperty("lineStart").GetInt32(),
                    location.GetProperty("lineEnd").GetInt32())));
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task FixtureAttribute_FailuresRowsAndInconclusive_DisposesProjectsWithoutChangingOutcomes()
    {
        var (exitCode, error, results) = await RunReportedAsync("FullyQualifiedName~FixtureCases");
        Assert.AreEqual(2, exitCode, error);
        Assert.HasCount(5, results);
        var passed = results.Where(result => (string?)result.Attribute("outcome") == "Passed").ToArray();
        Assert.HasCount(2, passed);
        Assert.AreSequenceEqual([FixtureContract.FirstRow, FixtureContract.SecondRow],
            passed.Select(result => result.Value.Split(FixtureContract.RowMarker + "|")[1].Split('\r', '\n')[0]),
            SequenceOrder.InAnyOrder);
        var assertion = Assert.ContainsSingle(ResultsFor(results, FixtureContract.AssertionMethod));
        Assert.AreEqual("Failed", (string?)assertion.Attribute("outcome"));
        Assert.Contains(FixtureContract.AssertionMessage, assertion.Value);
        var exception = Assert.ContainsSingle(ResultsFor(results, FixtureContract.ExceptionMethod));
        Assert.AreEqual("Failed", (string?)exception.Attribute("outcome"));
        Assert.Contains(FixtureContract.ExceptionMessage, exception.Value);
        var inconclusive = Assert.ContainsSingle(ResultsFor(results, FixtureContract.InconclusiveMethod));
        Assert.AreEqual("NotExecuted", (string?)inconclusive.Attribute("outcome"));
        Assert.Contains(FixtureContract.InconclusiveMessage, inconclusive.Value);
        var directories = results.Select(result => result.Value.Split(FixtureContract.DirectoryMarker + "|")[1]
            .Split('\r', '\n')[0]).ToArray();
        Assert.AreAllDistinct(directories);
        Assert.IsEmpty(directories.Where(Directory.Exists));
    }

    private static IEnumerable<XElement> ResultsFor(IEnumerable<XElement> results, string method)
    {
        return results.Where(item => ((string?)item.Attribute("testName"))!.Contains(method, StringComparison.Ordinal));
    }

    private async Task<int> ProbeMethodLineAsync()
    {
        var source = await File.ReadAllLinesAsync(Path.Combine(FixtureDirectory, ProbeContract.CasesFileName),
            context.CancellationToken);
        return Array.FindIndex(source, line => line.Trim() == "[ProbeMethod]") + 1;
    }

    private async Task<(int ExitCode, string Error, XElement[] Results)> RunReportedAsync(string filter)
    {
        var root = context.TestTempDirectory ??
                   throw new InvalidOperationException("The runner did not provide a test directory.");
        var directory = Path.Combine(root, "probe-results");
        Directory.CreateDirectory(directory);
        var result = await RunAsync("--report-trx", "--results-directory", directory, "--filter", filter);
        var path = Assert.ContainsSingle(Directory.GetFiles(directory, "*.trx"));
        // Deliberately failing child runs are evidence, not additional host-suite results.
        // Keep their TRX and referenced files together, and verify the attached artifact itself.
        var evidence = Path.Combine(root, "invocation-probe-evidence.zip");
        ZipFile.CreateFromDirectory(directory, evidence, CompressionLevel.Fastest, false);
        context.AddResultFile(evidence);
        using var archive = ZipFile.OpenRead(evidence);
        var report = Assert.ContainsSingle(entry => entry.FullName == Path.GetFileName(path), archive.Entries);
        using var stream = report.Open();
        return (result.ExitCode, result.Error, [.. XDocument.Load(stream).Descendants(s_trx + "UnitTestResult")]);
    }

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet");
        start.ArgumentList.Add(ProbeAssembly);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return await ChildProcess.RunAsync(start, "the managed MTP invocation probe", context.CancellationToken);
    }
}
