using System.Xml.Linq;
using Nami.IntegrationTests.Projects;

namespace Nami.IntegrationTests.Probes;

[TestClass]
public sealed class FixtureEvidenceContractTests(TestContext context)
{
    private static readonly XNamespace s_trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task FixtureAttribute_FailedBuildRows_RetainReadableMstestAttachmentsAfterCleanup()
    {
        var resultsDirectory = Path.Combine(context.TestTempDirectory!, "child-results");
        var start = new ProcessStartInfo("dotnet");
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "fixtures", "BuildEvidenceProbe",
            "BuildEvidenceProbe.dll"));
        foreach (var argument in new[]
                 {
                     "--report-trx", "--results-directory", resultsDirectory,
                     "--filter", "ClassName=Nami.IntegrationTests.Probes.BuildEvidenceCases"
                 })
            start.ArgumentList.Add(argument);

        var run = await ChildProcess.RunAsync(start, "the failing evidence probe", context.CancellationToken);

        Assert.AreEqual(2, run.ExitCode, run.Output + run.Error);
        var reportPath = Assert.ContainsSingle(Directory.GetFiles(resultsDirectory, "*.trx"));
        context.AddResultFile(reportPath);
        var report = XDocument.Load(reportPath);
        var deployment = (string?)report.Descendants(s_trx + "Deployment").Single().Attribute("runDeploymentRoot");
        Assert.IsNotNull(deployment);
        var rows = report.Descendants(s_trx + "UnitTestResult").ToArray();
        Assert.HasCount(2, rows);
        Assert.AreSequenceEqual(["Failed", "Failed"], rows.Select(row => (string?)row.Attribute("outcome")));
        var attachedFiles = new List<string>();
        var proofs = new List<string>();
        foreach (var row in rows)
        {
            Assert.Contains("CS0246", row.Value);
            var workspace = Marker(row, BuildEvidenceContract.DirectoryMarker);
            var proof = Marker(row, BuildEvidenceContract.RowMarker);
            proofs.Add(proof);
            Assert.IsFalse(Directory.Exists(workspace));
            var relative = (string?)row.Attribute("relativeResultsDirectory");
            Assert.IsNotNull(relative);
            var files = row.Descendants(s_trx + "ResultFile").Select(file => (string)file.Attribute("path")!)
                .Select(path =>
                    Path.IsPathRooted(path) ? path : Path.Combine(resultsDirectory, deployment, "In", relative, path))
                .ToArray();
            Assert.HasCount(5, files);
            Assert.IsTrue(files.All(File.Exists));
            attachedFiles.AddRange(files);
            var inputs = files.Single(path => Path.GetFileName(path) == "inputs.zip");
            Assert.AreEqual(BuildEvidenceContract.Source, FixtureEvidenceReader.ReadInput(inputs, "Consumer.cs"));
            var manifest = XDocument.Load(files.Single(path => Path.GetFileName(path) == "invocation.xml"));
            Assert.AreEqual(workspace, (string?)manifest.Root!.Attribute("Workspace"));
            Assert.AreEqual("Failed", (string?)manifest.Root.Element("Outcome")!.Attribute("Status"));
            Assert.AreEqual(proof, manifest.Descendants("Proof").Single().Value);
            var properties = XDocument.Load(files.Single(path => Path.GetFileName(path) == "properties.xml"));
            var snapshot = Assert.ContainsSingle(properties.Descendants("Snapshot"));
            Assert.AreEqual("BeforeBuild", (string?)snapshot.Attribute("Phase"));
            Assert.AreEqual(proof,
                snapshot.Elements("Property").Single(property => (string?)property.Attribute("Name") == "Proof").Value);
            var log = FixtureEvidenceReader.ReadBinlog(files.Single(path => Path.GetFileName(path) == "build.binlog"),
                context.CancellationToken);
            Assert.Contains("CS0246", log.ErrorCodes);
            Assert.AreEqual(BuildEvidenceContract.Source,
                log.Files.Single(file => file.Key.EndsWith("/Consumer.cs", StringComparison.Ordinal)).Value);
        }

        Assert.AreSequenceEqual([BuildEvidenceContract.FirstRow, BuildEvidenceContract.SecondRow], proofs,
            SequenceOrder.InAnyOrder);
        Assert.AreAllDistinct(attachedFiles);
    }

    private static string Marker(XElement result, string marker)
    {
        return result.Value.Split(marker + "|")[1].Split('\r', '\n')[0];
    }
}
