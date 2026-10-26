using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public sealed class InspectionPackageTests(TestContext context)
{
    [TestMethod]
    public async Task PackageIdentity_WorkspaceLinksAndExternalPins_AreValidated()
    {
        var result = await ProcessRunner.RunAsync("python3",
            [Path.Combine(ProcessRunner.RepositoryRoot, "eng/scripts/tests/test_inspection_packages.py")],
            ProcessRunner.RepositoryRoot, context.CancellationToken);
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
    }
}
