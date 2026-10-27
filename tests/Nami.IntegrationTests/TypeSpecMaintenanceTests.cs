using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public sealed class TypeSpecMaintenanceTests(TestContext context)
{
    [TestMethod]
    public async Task UpdateScript_VerifiesMetadataCompatibilityAndRollback()
    {
        var result = await ProcessRunner.RunAsync("python3",
            ["-W", "error::ResourceWarning", "eng/scripts/tests/test_update_typespec.py"],
            ProcessRunner.RepositoryRoot, context.CancellationToken);
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
    }
}
