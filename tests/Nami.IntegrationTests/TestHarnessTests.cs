using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public sealed class TestHarnessTests(TestContext context)
{
    [TestMethod]
    public async Task RunnerChecks_PythonSuite_PassesThroughTheRepositoryGate()
    {
        var result = await ProcessRunner.RunAsync("python3",
            ["-m", "unittest", "discover", "-s", "eng/scripts/tests", "-p", "test_run_tests.py", "-v"],
            ProcessRunner.RepositoryRoot, context.CancellationToken);
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        Assert.Contains("test_finder_file_recorded_by_restore_is_not_a_missing_package_file", result.Error);
        Assert.Contains("test_missing_package_file_is_still_reported", result.Error);
        Assert.Contains("test_normal_run_leaves_out_every_lane_of_its_suite_and_keeps_the_suite_floor", result.Error);
        Assert.Contains("test_lane_run_selects_its_category_against_its_own_floor_and_timeout", result.Error);
        Assert.Contains("test_filtered_run_keeps_the_lane_exclusion_and_proves_no_floor", result.Error);
        Assert.Contains("test_lane_without_a_known_suite_or_a_plain_category_is_refused", result.Error);
        Assert.MatchesRegex(@"Ran [1-9][0-9]* tests", result.Error);
    }
}
