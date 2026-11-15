using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public sealed class GateTests(TestContext context)
{
    [TestMethod]
    public async Task GateChecks_PythonSuite_PassesThroughTheRepositoryGate()
    {
        var result = await ProcessRunner.RunAsync("python3",
            ["-m", "unittest", "discover", "-s", "eng/scripts/tests", "-p", "test_gate.py", "-v"],
            ProcessRunner.RepositoryRoot, context.CancellationToken);
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        Assert.Contains("test_suite_floors_match_recorded_baseline", result.Error);
        Assert.Contains("test_every_python_test_file_has_a_wrapper", result.Error);
        Assert.Contains("test_vendored_protos_match_their_lock_files", result.Error);
        Assert.Contains("test_registry_check_uses_cached_pin_without_network", result.Error);
        Assert.MatchesRegex(@"Ran [1-9][0-9]* tests", result.Error);
    }
}
