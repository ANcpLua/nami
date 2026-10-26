namespace Nami.IntegrationTests.Probes;

[TestClass]
public sealed class ArtifactFingerprintTests(TestContext context)
{
    [TestMethod]
    public async Task ArtifactFingerprint_UnchangedBuildInputs_PreservesTheCacheIdentity()
    {
        var target = Path.Combine(context.TestTempDirectory!, "Directory.Build.targets");
        await File.WriteAllTextAsync(target, "<Project />", context.CancellationToken);
        var original = ArtifactFingerprint.Files([target], "publish --self-contained true");
        Assert.AreEqual(original, ArtifactFingerprint.Files([target], "publish --self-contained true"));
    }

    [TestMethod]
    [DataRow("Directory.Build.targets")]
    [DataRow("NuGet.Config")]
    public async Task ArtifactFingerprint_BuildInputChanges_InvalidatesTheCacheIdentity(string fileName)
    {
        var input = Path.Combine(context.TestTempDirectory!, fileName);
        await File.WriteAllTextAsync(input, "before", context.CancellationToken);
        var original = ArtifactFingerprint.Files([input], "publish");
        await File.WriteAllTextAsync(input, "after", context.CancellationToken);
        Assert.AreNotEqual(original, ArtifactFingerprint.Files([input], "publish"));
    }

    [TestMethod]
    public void ArtifactFingerprint_PublicationRecipeChanges_InvalidatesTheCacheIdentity()
    {
        var original = ArtifactFingerprint.Files([], "publish --self-contained true");
        Assert.AreNotEqual(original, ArtifactFingerprint.Files([], "publish --self-contained false"));
    }
}
