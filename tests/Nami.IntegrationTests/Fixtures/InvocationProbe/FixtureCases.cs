using Nami.Tests;

namespace Nami.IntegrationTests.Probes;

[UsesFixture<CaptureFixture>]
public sealed class FixtureCases(TestContext context)
{
    [TestMethod]
    [DataRow(FixtureContract.FirstRow)]
    [DataRow(FixtureContract.SecondRow)]
    public void Fixture_EachDataRow_KeepsItsOwnProject(string row)
    {
        RecordDirectory(row);
    }

    [TestMethod]
    public void Fixture_AssertFail_KeepsTheOriginalAssertion()
    {
        RecordDirectory("assertion");
        Assert.Fail(FixtureContract.AssertionMessage);
    }

    [TestMethod]
    public void Fixture_Exception_KeepsTheOriginalException()
    {
        RecordDirectory("exception");
        throw new InvalidOperationException(FixtureContract.ExceptionMessage);
    }

    [TestMethod]
    public void Fixture_Inconclusive_KeepsTheOriginalResult()
    {
        RecordDirectory("inconclusive");
        Assert.Inconclusive(FixtureContract.InconclusiveMessage);
    }

    private void RecordDirectory(string value)
    {
        var project = FixtureContext.Current<CaptureFixture>();
        project.Materialize();
        File.WriteAllText(Path.Combine(project.DirectoryPath, "owned.txt"), value);
        context.WriteLine(FixtureContract.DirectoryMarker + "|" + project.DirectoryPath);
        context.WriteLine(FixtureContract.RowMarker + "|" + value);
    }
}
