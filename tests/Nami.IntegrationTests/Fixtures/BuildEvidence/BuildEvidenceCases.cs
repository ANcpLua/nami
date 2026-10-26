using Nami.Tests;

namespace Nami.IntegrationTests.Probes;

[UsesFixture<BuildEvidenceFixture>]
public sealed class BuildEvidenceCases(TestContext context)
{
    [TestMethod]
    [DataRow(BuildEvidenceContract.FirstRow)]
    [DataRow(BuildEvidenceContract.SecondRow)]
    public async Task BuildAsync_InvalidConsumer_FailsWithRetainedEvidence(string proof)
    {
        var project = FixtureContext.Current<BuildEvidenceFixture>().WithProperty("Proof", proof);
        context.WriteLine(BuildEvidenceContract.DirectoryMarker + "|" + project.DirectoryPath);
        context.WriteLine(BuildEvidenceContract.RowMarker + "|" + proof);

        var result = await project.BuildAsync(context.CancellationToken);

        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
    }
}

public sealed class BuildEvidenceFixture : IProjectFixture
{
    private BuildEvidenceFixture() { }
    public static string Name => "AttributedEvidence";
    public static void Configure(FixtureProject project) => project.WithProperty("OutputType", "Library")
        .RecordProperties("Proof", "TargetFramework").AddSource("Consumer.cs", BuildEvidenceContract.Source);
}
