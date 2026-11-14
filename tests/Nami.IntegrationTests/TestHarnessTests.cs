using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public sealed class TestHarnessTests(TestContext context)
{
    [TestMethod]
    public async Task PackageCompleteness_FinderFilesRecordedByRestore_AreNotReportedAsMissing()
    {
        var result = await ProcessRunner.RunAsync("python3",
            ["-m", "unittest", "discover", "-s", "eng/scripts/tests", "-p", "test_run_tests.py", "-v"],
            ProcessRunner.RepositoryRoot, context.CancellationToken);
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        Assert.Contains("test_finder_file_recorded_by_restore_is_not_a_missing_package_file", result.Error);
        Assert.Contains("test_missing_package_file_is_still_reported", result.Error);
        Assert.MatchesRegex(@"Ran [1-9][0-9]* tests", result.Error);
    }
}
