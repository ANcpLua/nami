using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public sealed class ToolchainPinTests(TestContext context)
{
    [TestMethod]
    public async Task ToolchainPins_PythonSuite_PassesThroughTheRepositoryGate()
    {
        var result = await ProcessRunner.RunAsync("python3",
            ["-m", "unittest", "discover", "-s", "eng/scripts/tests", "-p", "test_toolchain_pins.py", "-v"],
            ProcessRunner.RepositoryRoot, context.CancellationToken);
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        Assert.Contains("test_toolchain_pins_agree", result.Error);
        Assert.MatchesRegex(@"Ran [1-9][0-9]* tests", result.Error);
    }
}
