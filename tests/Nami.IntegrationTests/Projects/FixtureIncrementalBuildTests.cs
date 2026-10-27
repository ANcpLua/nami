using System.IO.Compression;

namespace Nami.IntegrationTests.Projects;

[TestClass]
[TestCategory("Integration")]
public sealed class FixtureIncrementalBuildTests(TestContext context)
{
    [TestMethod]
    public async Task Build_UnchangedInputs_ReusesAssemblyAndCreatesSeparateEvidence()
    {
        await using var fixture = new FixtureProject("IncrementalConsumer", context: context);
        fixture.AddSource("Program.cs", "Console.Write(\"stable\");");
        var first = await fixture.BuildAsync(context.CancellationToken);
        Assert.AreEqual(0, first.ExitCode, first.Output + first.Error);
        var assemblyTime = File.GetLastWriteTimeUtc(first.AssemblyPath);
        var assemblyBytes = File.ReadAllBytes(first.AssemblyPath);
        var source = Path.Combine(fixture.DirectoryPath, "Program.cs");
        var sourceTime = File.GetLastWriteTimeUtc(source);

        var second = await fixture.BuildAsync(context.CancellationToken);

        Assert.AreEqual(0, second.ExitCode, second.Output + second.Error);
        Assert.AreEqual(sourceTime, File.GetLastWriteTimeUtc(source));
        Assert.AreEqual(assemblyTime, File.GetLastWriteTimeUtc(second.AssemblyPath));
        Assert.AreSequenceEqual(assemblyBytes, File.ReadAllBytes(second.AssemblyPath));
        Assert.IsNotNull(first.Evidence);
        Assert.IsNotNull(second.Evidence);
        Assert.AreNotEqual(first.Evidence.DirectoryPath, second.Evidence.DirectoryPath);
        using var archive = ZipFile.OpenRead(second.Evidence.InputsPath);
        var entry = Assert.ContainsSingle(item => item.FullName == "Program.cs", archive.Entries);
        using var reader = new StreamReader(entry.Open());
        Assert.AreEqual("Console.Write(\"stable\");", await reader.ReadToEndAsync(context.CancellationToken));
        Assert.IsNotNull(second.Evidence.BinlogPath);
        Assert.IsTrue(File.Exists(second.Evidence.BinlogPath));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Build_WithoutRequestedEvidence_StillProducesRunnableOutput(bool supplyContext)
    {
        await using var fixture = new FixtureProject("QuietConsumer", context: supplyContext ? context : null,
            diagnostics: supplyContext ? FixtureDiagnostics.None : FixtureDiagnostics.Full);
        fixture.AddSource("Program.cs", "Console.Write(\"quiet\");");

        var build = await fixture.BuildAsync(context.CancellationToken);

        Assert.AreEqual(0, build.ExitCode, build.Output + build.Error);
        Assert.IsNull(build.Evidence);
        Assert.IsEmpty(fixture.EvidenceFiles);
        Assert.IsEmpty(Directory.GetFiles(fixture.DirectoryPath, "*.binlog", SearchOption.AllDirectories));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.DirectoryPath, "fixture-invocation.xml")));
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet");
        start.ArgumentList.Add(build.AssemblyPath);
        var run = await ChildProcess.RunAsync(start, "quiet consumer", context.CancellationToken);
        Assert.AreEqual(0, run.ExitCode, run.Error);
        Assert.AreEqual("quiet", run.Output);
    }

    [TestMethod]
    public async Task Build_DisabledDiagnostics_ReturnsCompilerFailureWithoutArchives()
    {
        await using var fixture = new FixtureProject("QuietFailure", context: context,
            diagnostics: FixtureDiagnostics.None);
        fixture.AddSource("Program.cs", "MissingType value = new();");

        var build = await fixture.BuildAsync(context.CancellationToken);

        Assert.AreNotEqual(0, build.ExitCode);
        Assert.Contains("CS0246", build.Output);
        Assert.IsNull(build.Evidence);
        Assert.IsEmpty(fixture.EvidenceFiles);
        Assert.IsEmpty(Directory.GetFiles(fixture.DirectoryPath, "*.binlog", SearchOption.AllDirectories));
        Assert.IsEmpty(Directory.GetFiles(fixture.DirectoryPath, "*.zip", SearchOption.AllDirectories));
    }

    [TestMethod]
    public async Task Build_SharedFeed_ReusesPackageAndHttpCacheAcrossConsumers()
    {
        await using var feed = new PackageFeedFixture();
        await using var producer = new FixtureProject("CacheProducer", context: context);
        producer.WithProperty("OutputType", "Library")
            .AddSource("Value.cs", "public static class CachedValue { public const string Text = \"shared\"; }");
        await feed.PackAsync(producer, "Nami.Cache.Probe", "1.0.0", context.CancellationToken);
        string cacheFile;
        DateTime packageTime;
        await using (var first = new FixtureProject("FirstConsumer", context: context))
        {
            Configure(first);
            var build = await first.BuildAsync(context.CancellationToken);
            Assert.AreEqual(0, build.ExitCode, build.Output + build.Error);
            Assert.AreEqual(feed.HttpCacheDirectory, build.GetRecordedProperty("NUGET_HTTP_CACHE_PATH"));
            Assert.AreEqual(feed.CacheDirectory, build.GetRecordedProperty("NUGET_PACKAGES"));
            cacheFile = Assert.ContainsSingle(Directory.GetFiles(feed.CacheDirectory, "*.nupkg", SearchOption.AllDirectories));
            packageTime = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(cacheFile, packageTime);
        }

        await using (var second = new FixtureProject("SecondConsumer", context: context))
        {
            Configure(second);
            var build = await second.BuildAsync(context.CancellationToken);
            Assert.AreEqual(0, build.ExitCode, build.Output + build.Error);
            Assert.AreEqual(feed.HttpCacheDirectory, build.GetRecordedProperty("NUGET_HTTP_CACHE_PATH"));
            Assert.AreEqual(feed.CacheDirectory, build.GetRecordedProperty("NUGET_PACKAGES"));
            Assert.AreEqual(packageTime, File.GetLastWriteTimeUtc(cacheFile));
        }

        void Configure(FixtureProject fixture) => fixture.WithPackageFeed(feed)
            .AddPackage("Nami.Cache.Probe", "1.0.0").AddSource("Program.cs", "Console.Write(CachedValue.Text);")
            .RecordProperties("NUGET_HTTP_CACHE_PATH", "NUGET_PACKAGES");
    }
}
