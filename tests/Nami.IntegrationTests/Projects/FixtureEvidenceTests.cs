using System.IO.Compression;
using System.Xml.Linq;

namespace Nami.IntegrationTests.Projects;

[TestClass]
public sealed class FixtureEvidenceTests(TestContext context)
{
    private const string Restore = "restore";
    private const string Pack = "pack";
    private const string Publish = "publish";

    [TestMethod]
    public async Task BuildAsync_CompilerFailure_PreservesInputsBinlogAndInitialPropertiesAfterDisposal()
    {
        const string Source = "public class Consumer { public MissingType Value; }";
        const string SourcePath = "Nested Folder/Con;sumer.cs";
        var project = new FixtureProject("FailedEvidence", context: context);
        var workspace = project.DirectoryPath;
        FixtureBuildResult result;
        await using (project)
        {
            project.WithProperty("OutputType", "Library")
                .WithProperty("Proof", "<original> & value").RecordProperties("Proof", "TargetFramework")
                .AddSource(SourcePath, Source);
            result = await project.BuildAsync(context.CancellationToken);
        }

        var evidence = result.Evidence;

        Assert.AreNotEqual(0, result.ExitCode);
        Assert.IsEmpty(result.RecordedProperties);
        Assert.IsFalse(Directory.Exists(workspace));
        Assert.IsNotNull(evidence);
        Assert.IsTrue(evidence.Files.All(File.Exists));
        Assert.AreEqual(Source, FixtureEvidenceReader.ReadInput(evidence.InputsPath, SourcePath));
        var properties = XDocument.Load(evidence.PropertiesPath);
        var snapshot = Assert.ContainsSingle(properties.Descendants("Snapshot"));
        Assert.AreEqual("BeforeBuild", (string?)snapshot.Attribute("Phase"));
        Assert.AreEqual("net11.0", (string?)snapshot.Attribute("TargetFramework"));
        Assert.AreEqual("<original> & value",
            snapshot.Elements("Property").Single(item => (string?)item.Attribute("Name") == "Proof").Value);
        Assert.IsNotNull(evidence.BinlogPath);
        var log = FixtureEvidenceReader.ReadBinlog(evidence.BinlogPath, context.CancellationToken);
        Assert.Contains("CS0246", log.ErrorCodes);
        Assert.AreEqual(Source,
            log.Files.Single(item => item.Key.EndsWith("/" + SourcePath, StringComparison.Ordinal)).Value);
        Assert.Contains("&lt;original&gt; &amp; value",
            log.Files.Single(item => item.Key.EndsWith("/FailedEvidence.csproj", StringComparison.Ordinal)).Value);
        Assert.Contains(item => item.Key.EndsWith("/fixture-invocation.xml", StringComparison.Ordinal), log.Files);
        var initial = Assert.ContainsSingle(log.Files.Where(item =>
            item.Key.EndsWith("/fixture-initial-properties.net11.0.txt", StringComparison.Ordinal)));
        Assert.Contains("Proof=<original> & value", initial.Value);
    }

    [TestMethod]
    public async Task BuildAsync_MultipleFrameworks_PreservesSeparateInitialAndFinalSnapshots()
    {
        await using var project = Library("FrameworkEvidence").WithTargetFrameworks("net9.0", "net10.0")
            .WithProperty("Proof", "nine", "'$(TargetFramework)' == 'net9.0'")
            .WithProperty("Proof", "ten", "'$(TargetFramework)' == 'net10.0'")
            .RecordProperties("Proof", "TargetFramework");
        ConfigureLibrary(project);

        var result = await project.BuildAsync(context.CancellationToken);

        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        Assert.IsNotNull(result.Evidence);
        var snapshots = XDocument.Load(result.Evidence.PropertiesPath).Descendants("Snapshot").ToArray();
        Assert.HasCount(4, snapshots);
        Assert.AreSequenceEqual<(string?, string?, string)>(
            [
                ("BeforeBuild", "net9.0", "nine"), ("AfterBuild", "net9.0", "nine"),
                ("BeforeBuild", "net10.0", "ten"), ("AfterBuild", "net10.0", "ten")
            ],
            snapshots.Select(snapshot => ((string?)snapshot.Attribute("Phase"),
                (string?)snapshot.Attribute("TargetFramework"),
                snapshot.Elements("Property").Single(property => (string?)property.Attribute("Name") == "Proof")
                    .Value)),
            SequenceOrder.InAnyOrder);
        Assert.IsNotNull(result.Evidence.BinlogPath);
        var log = FixtureEvidenceReader.ReadBinlog(result.Evidence.BinlogPath, context.CancellationToken);
        Assert.Contains("Proof=nine",
            log.Files.Single(file => file.Key.EndsWith("/fixture-properties.net9.0.txt", StringComparison.Ordinal))
                .Value);
        Assert.Contains("Proof=ten",
            log.Files.Single(file => file.Key.EndsWith("/fixture-properties.net10.0.txt", StringComparison.Ordinal))
                .Value);
    }

    [TestMethod]
    public async Task BuildAsync_RepeatedInvocations_PreservesEachInputAndPropertyValue()
    {
        var project = Library("RepeatedEvidence");
        FixtureBuildResult first;
        FixtureBuildResult second;
        await using (project)
        {
            ConfigureLibrary(project);
            project.WithProperty("Proof", "first").RecordProperties("Proof");
            first = await project.BuildAsync(context.CancellationToken);
            project.WithProperty("Proof", "second");
            second = await project.BuildAsync(context.CancellationToken);
        }

        Assert.AreEqual(0, first.ExitCode, first.Output + first.Error);
        Assert.AreEqual(0, second.ExitCode, second.Output + second.Error);
        Assert.IsNotNull(first.Evidence);
        Assert.IsNotNull(second.Evidence);
        Assert.AreNotEqual(first.Evidence.DirectoryPath, second.Evidence.DirectoryPath);
        Assert.Contains("<Proof>first</Proof>",
            FixtureEvidenceReader.ReadInput(first.Evidence.InputsPath, "RepeatedEvidence.csproj"));
        Assert.Contains("<Proof>second</Proof>",
            FixtureEvidenceReader.ReadInput(second.Evidence.InputsPath, "RepeatedEvidence.csproj"));
        Assert.AreSequenceEqual(["first", "second"], new[] { first.Evidence, second.Evidence }.Select(evidence =>
            XDocument.Load(evidence.PropertiesPath).Descendants("Snapshot")
                .Single(snapshot => (string?)snapshot.Attribute("Phase") == "AfterBuild")
                .Element("Property")!.Value));
        Assert.HasCount(10, project.EvidenceFiles);
    }

    [TestMethod]
    public async Task BuildAsync_FailedRebuild_PreservesCurrentInputsWithoutPreviousFinalProperties()
    {
        await using var project = Library("ChangedEvidence").WithProperty("Proof", "first").RecordProperties("Proof")
            .WithCustomTarget(new FixtureTarget("RejectBuild", "CoreCompile")
                .Error("EVIDENCE001", "requested failure", "'$(Reject)' == 'true'"));
        ConfigureLibrary(project);
        var first = await project.BuildAsync(context.CancellationToken);
        Assert.AreEqual(0, first.ExitCode, first.Output + first.Error);
        project.WithProperty("Proof", "second").WithProperty("Reject", "true");

        var failed = await project.BuildAsync(context.CancellationToken);

        Assert.AreNotEqual(0, failed.ExitCode);
        Assert.IsEmpty(failed.RecordedProperties);
        Assert.IsNotNull(failed.Evidence);
        var snapshot = Assert.ContainsSingle(XDocument.Load(failed.Evidence.PropertiesPath).Descendants("Snapshot"));
        Assert.AreEqual("BeforeBuild", (string?)snapshot.Attribute("Phase"));
        Assert.AreEqual("second", snapshot.Element("Property")!.Value);
        Assert.IsNotNull(first.Evidence);
        Assert.AreEqual("first", XDocument.Load(first.Evidence.PropertiesPath).Descendants("Snapshot")
            .Single(item => (string?)item.Attribute("Phase") == "AfterBuild").Element("Property")!.Value);
    }

    [TestMethod]
    [DataRow(Restore)]
    [DataRow(Pack)]
    [DataRow(Publish)]
    public async Task ExecuteAsync_OtherSdkCommands_PreservesCommandInputsAndBinlog(string command)
    {
        await using var project = Library("CommandEvidence").WithProperty("IsPackable", "true");
        ConfigureLibrary(project);

        var result = command switch
        {
            Restore => await project.RestoreAsync(context.CancellationToken),
            Pack => await project.PackAsync(context.CancellationToken),
            Publish => await project.PublishAsync(false, context.CancellationToken),
            _ => throw new InvalidOperationException("Unexpected command.")
        };

        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        Assert.IsNotNull(result.Evidence);
        Assert.IsNotNull(result.Evidence.BinlogPath);
        var manifest = XDocument.Load(result.Evidence.ManifestPath);
        Assert.AreEqual(command, (string?)manifest.Root!.Attribute("Command"));
        Assert.AreEqual(command, manifest.Descendants("Argument").First().Value);
        Assert.IsTrue(File.Exists(result.Evidence.BinlogPath));
        var archived =
            XDocument.Parse(FixtureEvidenceReader.ReadInput(result.Evidence.InputsPath, "fixture-invocation.xml"));
        Assert.AreEqual(command, archived.Descendants("Argument").First().Value);
    }

    [TestMethod]
    public async Task PackAsync_DiagnosticManifest_RemainsOutsideTheProducedPackage()
    {
        await using var project = Library("PackagedEvidence").WithProperty("IsPackable", "true");
        ConfigureLibrary(project);

        var result = await project.PackAsync(context.CancellationToken);

        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        await using var package = await ZipFile.OpenReadAsync(
            Assert.ContainsSingle(Directory.GetFiles(result.OutputDirectory, "*.nupkg")), context.CancellationToken);
        Assert.IsNotNull(package.GetEntry("lib/net11.0/PackagedEvidence.dll"));
        Assert.IsEmpty(package.Entries.Where(entry => entry.Name == "fixture-invocation.xml"));
        Assert.IsNotNull(result.Evidence);
        Assert.Contains("<FixtureInvocation",
            FixtureEvidenceReader.ReadInput(result.Evidence.InputsPath, "fixture-invocation.xml"));
    }

    [TestMethod]
    public async Task BuildAsync_PreCancelledToken_CreatesNeitherWorkspaceNorEvidence()
    {
        await using var project = Library("CancelledEvidence");
        ConfigureLibrary(project);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var token = cancelled.Token;

        _ = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => project.BuildAsync(token));

        Assert.IsFalse(Directory.Exists(project.DirectoryPath));
        Assert.IsEmpty(project.EvidenceFiles);
    }

    [TestMethod]
    public async Task BuildAsync_CancelledAfterMaterialization_PreservesInputsAndCancellation()
    {
        var project = Library("InterruptedEvidence");
        using var cancelled = new CancellationTokenSource();
        await using (project)
        {
            ConfigureLibrary(project);
            var build = project.BuildAsync(cancelled.Token);
            Assert.IsTrue(Directory.Exists(project.DirectoryPath));
            await cancelled.CancelAsync();
            _ = await Assert.ThrowsAsync<OperationCanceledException>(() => build);
        }

        Assert.IsFalse(Directory.Exists(project.DirectoryPath));
        var files = project.EvidenceFiles.ToArray();
        Assert.IsTrue(files.All(File.Exists));
        var inputs = files.Single(path => Path.GetFileName(path) == "inputs.zip");
        Assert.AreEqual("public class Consumer { }", FixtureEvidenceReader.ReadInput(inputs, "Consumer.cs"));
        var manifest = XDocument.Load(files.Single(path => Path.GetFileName(path) == "invocation.xml"));
        Assert.AreEqual("Interrupted", (string?)manifest.Root!.Element("Outcome")!.Attribute("Status"));
    }

    [TestMethod]
    public async Task Materialize_WithoutSdkExecution_PreservesInputEvidenceAfterDisposal()
    {
        var project = Library("MaterializedEvidence");
        string[] files;
        await using (project)
        {
            ConfigureLibrary(project);
            project.Materialize();
            files = [.. project.EvidenceFiles];
        }

        Assert.IsFalse(Directory.Exists(project.DirectoryPath));
        Assert.HasCount(4, files);
        Assert.IsTrue(files.All(File.Exists));
        var manifest = XDocument.Load(files.Single(path => Path.GetFileName(path) == "invocation.xml"));
        Assert.AreEqual("Materialized", (string?)manifest.Root!.Element("Outcome")!.Attribute("Status"));
    }

    private FixtureProject Library(string name)
    {
        return new FixtureProject(name, context: context);
    }

    private static void ConfigureLibrary(FixtureProject project)
    {
        project.WithProperty("OutputType", "Library")
            .AddSource("Consumer.cs", "public class Consumer { }");
    }
}
