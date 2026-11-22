using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public sealed class BranchHousekeepingTests(TestContext context)
{
    [TestMethod]
    public async Task BranchSelection_KeepListPatternsAndMergedTips_PassThroughTheRepositoryGate()
    {
        var result = await ProcessRunner.RunAsync("python3",
            ["-m", "unittest", "discover", "-s", "eng/scripts/tests", "-p", "test_branch_housekeeping.py", "-v"],
            ProcessRunner.RepositoryRoot, context.CancellationToken);
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        Assert.Contains("test_keep_list_wins_over_main_and_over_the_list", result.Error);
        Assert.MatchesRegex(@"Ran [1-9][0-9]* tests? in ", result.Error);
    }
}
