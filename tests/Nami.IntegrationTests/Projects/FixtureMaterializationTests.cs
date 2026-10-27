using System.Xml.Linq;

namespace Nami.IntegrationTests.Projects;

[TestClass]
[TestCategory("Integration")]
public sealed class FixtureMaterializationTests
{
    private static readonly DateTime PreservedTimestamp = new(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);

    [TestMethod]
    public async Task Materialize_WithoutExplicitContext_DoesNotCaptureAmbientTestEvidence()
    {
        await using var fixture = new FixtureProject("ExplicitEvidenceOwner");

        fixture.Materialize();

        Assert.IsEmpty(fixture.EvidenceFiles);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.DirectoryPath, "fixture-invocation.xml")));
        Assert.IsTrue(File.Exists(fixture.ProjectPath));
    }

    [TestMethod]
    public async Task Materialize_UnchangedInputs_PreservesBytesAndTimestamps()
    {
        await using var fixture = new FixtureProject("StableInputs", diagnostics: FixtureDiagnostics.None);
        fixture.AddSource("Nested/Program.cs", "Console.Write(\"héllo\");").Materialize();
        var files = Directory.GetFiles(fixture.DirectoryPath, "*", SearchOption.AllDirectories);
        var expected = files.ToDictionary(path => path, File.ReadAllBytes);
        foreach (var path in files) File.SetLastWriteTimeUtc(path, PreservedTimestamp);

        fixture.Materialize();

        Assert.AreSequenceEqual(files,
            Directory.GetFiles(fixture.DirectoryPath, "*", SearchOption.AllDirectories), SequenceOrder.InAnyOrder);
        foreach (var path in files)
        {
            Assert.AreSequenceEqual(expected[path], File.ReadAllBytes(path), path);
            Assert.AreEqual(PreservedTimestamp, File.GetLastWriteTimeUtc(path), path);
        }
        Assert.AreEqual("Console.Write(\"héllo\");", File.ReadAllText(Path.Combine(fixture.DirectoryPath, "Nested/Program.cs")));
    }

    [TestMethod]
    public async Task Materialize_ChangedDefinition_UpdatesOnlyChangedInputs()
    {
        await using var fixture = new FixtureProject("ChangedInputs", diagnostics: FixtureDiagnostics.None);
        fixture.AddSource("Program.cs", "Console.Write(1);").WithProperty("Description", "before").Materialize();
        var source = Path.Combine(fixture.DirectoryPath, "Program.cs");
        File.SetLastWriteTimeUtc(source, PreservedTimestamp);

        fixture.WithProperty("Description", "after & <quoted>").AddSource("Added.cs", "public sealed class Added;").Materialize();

        Assert.AreEqual("after & <quoted>", Assert.ContainsSingle(XDocument.Load(fixture.ProjectPath).Descendants("Description")).Value);
        Assert.AreEqual("public sealed class Added;", File.ReadAllText(Path.Combine(fixture.DirectoryPath, "Added.cs")));
        Assert.AreEqual(PreservedTimestamp, File.GetLastWriteTimeUtc(source));
        Assert.AreEqual("Console.Write(1);", File.ReadAllText(source));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Materialize_MissingOrModifiedSource_RestoresDeclaredContent(bool delete)
    {
        await using var fixture = new FixtureProject("RepairInputs", diagnostics: FixtureDiagnostics.None);
        fixture.AddSource("Program.cs", "Console.Write(\"original\");").Materialize();
        var path = Path.Combine(fixture.DirectoryPath, "Program.cs");
        if (delete) File.Delete(path);
        else File.WriteAllText(path, "Console.Write(\"tampered\");");

        fixture.Materialize();

        Assert.AreEqual("Console.Write(\"original\");", File.ReadAllText(path));
    }

    [TestMethod]
    public async Task Materialize_DefaultCaches_RemainIsolated()
    {
        await using var first = new FixtureProject("FirstIsolated", diagnostics: FixtureDiagnostics.None);
        await using var second = new FixtureProject("SecondIsolated", diagnostics: FixtureDiagnostics.None);
        first.Materialize();
        second.Materialize();

        var firstCache = ReadCache(first);
        var secondCache = ReadCache(second);
        Assert.AreEqual(Path.Combine(first.DirectoryPath, ".nuget", "packages"), firstCache);
        Assert.AreEqual(Path.Combine(second.DirectoryPath, ".nuget", "packages"), secondCache);
        Assert.AreNotEqual(firstCache, secondCache);
    }

    [TestMethod]
    public async Task Materialize_SharedFeed_BorrowsCacheUntilOwnerDisposal()
    {
        var feed = new PackageFeedFixture();
        await using var owner = feed;
        string workspace;
        await using (var first = new FixtureProject("FirstBorrower", diagnostics: FixtureDiagnostics.None))
        {
            first.WithPackageFeed(feed).Materialize();
            workspace = first.DirectoryPath;
            Assert.AreEqual(feed.CacheDirectory, ReadCache(first));
            Assert.DoesNotContain(element => (string?)element.Attribute("key") == "FixtureFeed0",
                XDocument.Load(Path.Combine(first.DirectoryPath, "NuGet.Config")).Descendants("packageSource"));
            Directory.CreateDirectory(feed.CacheDirectory);
            File.WriteAllText(Path.Combine(feed.CacheDirectory, "owner-marker"), "retained");
            Directory.CreateDirectory(feed.HttpCacheDirectory);
            File.WriteAllText(Path.Combine(feed.HttpCacheDirectory, "owner-marker"), "shared HTTP cache");
        }

        Assert.IsFalse(Directory.Exists(workspace));
        Assert.AreEqual("retained", File.ReadAllText(Path.Combine(feed.CacheDirectory, "owner-marker")));
        Assert.AreEqual("shared HTTP cache", File.ReadAllText(Path.Combine(feed.HttpCacheDirectory, "owner-marker")));
        await using (var second = new FixtureProject("SecondBorrower", diagnostics: FixtureDiagnostics.None))
        {
            second.WithPackageFeed(feed).Materialize();
            Assert.AreEqual(feed.CacheDirectory, ReadCache(second));
            Assert.AreEqual("retained", File.ReadAllText(Path.Combine(feed.CacheDirectory, "owner-marker")));
            Assert.AreEqual("shared HTTP cache", File.ReadAllText(Path.Combine(feed.HttpCacheDirectory, "owner-marker")));
        }

        await feed.DisposeAsync();
        Assert.IsFalse(Directory.Exists(feed.DirectoryPath));
        await using var late = new FixtureProject("LateBorrower", diagnostics: FixtureDiagnostics.None);
        late.WithPackageFeed(feed);
        Assert.ThrowsExactly<ObjectDisposedException>(late.Materialize);
    }

    [TestMethod]
    public async Task Dispose_RemovesWorkspaceAfterMaterializationFailure()
    {
        string workspace;
        await using (var fixture = new FixtureProject("PartialSetup", diagnostics: FixtureDiagnostics.None))
        {
            workspace = fixture.DirectoryPath;
            Directory.CreateDirectory(Path.Combine(workspace, "Program.cs"));
            fixture.AddSource("Program.cs", "Console.Write(1);");
            Assert.ThrowsExactly<UnauthorizedAccessException>(fixture.Materialize);
        }

        Assert.IsFalse(Directory.Exists(workspace));
    }

    [TestMethod]
    public async Task Constructor_UnknownDiagnostics_RejectsTheOption()
    {
        var error = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
        {
            await using var fixture = new FixtureProject("InvalidOption", diagnostics: (FixtureDiagnostics)123);
        });
        Assert.AreEqual("diagnostics", error.ParamName);
    }

    private static string ReadCache(FixtureProject fixture)
    {
        var setting = Assert.ContainsSingle(element => (string?)element.Attribute("key") == "globalPackagesFolder",
            XDocument.Load(Path.Combine(fixture.DirectoryPath, "NuGet.Config")).Descendants("add"));
        var value = setting.Attribute("value");
        Assert.IsNotNull(value);
        return value.Value;
    }
}
