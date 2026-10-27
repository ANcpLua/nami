using System.Text.Json;

namespace Nami.IntegrationTests.Projects;

[TestClass]
public sealed class PackageFeedTests(TestContext context) : IAsyncDisposable
{
    private const string PackageId = "MaterializedProject.Fixture.Producer";
    private const string PackageVersion = "1.2.3-fixture";
    private readonly PackageFeedFixture _feed = new();

    public ValueTask DisposeAsync()
    {
        return _feed.DisposeAsync();
    }

    [TestMethod]
    public async Task PackAsync_CancelledRequest_LeavesTheFeedAndProducerInMemory()
    {
        await using var producer = Producer();
        ConfigureProducer(producer);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var token = cancellation.Token;

        _ = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            _feed.PackAsync(producer, PackageId, PackageVersion, token));

        Assert.IsFalse(Directory.Exists(_feed.DirectoryPath));
        Assert.IsFalse(Directory.Exists(producer.DirectoryPath));
    }

    [TestMethod]
    public async Task PackAsync_GeneratedProducer_IsRestoredAndExecutedByTheConsumer()
    {
        await using var producer = Producer();
        ConfigureProducer(producer);
        var package = await _feed.PackAsync(producer, PackageId, PackageVersion, context.CancellationToken);
        await using var consumer = new FixtureProject("PackageConsumer", context: context);
        consumer.WithPackageFeed(_feed)
            .AddPackage(package.Id, package.Version).AddSource("Program.cs", "Console.Write(ProducedValue.Number);");

        var build = await consumer.BuildAsync(context.CancellationToken);
        Assert.AreEqual(0, build.ExitCode, build.Output + build.Error);
        var start = new ProcessStartInfo("dotnet");
        start.ArgumentList.Add(build.AssemblyPath);
        var execution = await ChildProcess.RunAsync(start, "the package consumer", context.CancellationToken);

        Assert.AreEqual(0, execution.ExitCode, execution.Error);
        Assert.AreEqual("42", execution.Output);
        using var assets = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(consumer.DirectoryPath, "obj", "project.assets.json"), context.CancellationToken));
        var restored = assets.RootElement.GetProperty("libraries").GetProperty(PackageId + "/" + PackageVersion);
        Assert.AreEqual("package", restored.GetProperty("type").GetString());
        Assert.AreEqual(Path.TrimEndingDirectorySeparator(_feed.CacheDirectory),
            Path.TrimEndingDirectorySeparator(Assert
                .ContainsSingle(assets.RootElement.GetProperty("packageFolders").EnumerateObject()).Name));
    }

    [TestMethod]
    public async Task PackAsync_IdenticalInputs_ReusesThePublishedIdentity()
    {
        await using var producer = Producer();
        ConfigureProducer(producer);
        var first = await _feed.PackAsync(producer, PackageId, PackageVersion, context.CancellationToken);

        var second = await _feed.PackAsync(producer, PackageId, PackageVersion, context.CancellationToken);

        Assert.AreSame(first, second);
        Assert.HasCount(1, Directory.GetFiles(_feed.DirectoryPath, "*.nupkg"));
        first.Verify();
    }

    [TestMethod]
    public async Task PackAsync_EquivalentNuGetVersions_ReuseTheNormalizedIdentity()
    {
        await using var producer = Producer();
        ConfigureProducer(producer);
        var first = await _feed.PackAsync(producer, PackageId, "1.2", context.CancellationToken);

        var second = await _feed.PackAsync(producer, PackageId, "1.2.0", context.CancellationToken);

        Assert.AreEqual("1.2.0", first.Version);
        Assert.AreSame(first, second);
        first.Verify();
    }

    [TestMethod]
    public async Task PackAsync_SameProducerWithNewVersion_PreservesBothPublications()
    {
        await using var producer = Producer();
        ConfigureProducer(producer);
        var first = await _feed.PackAsync(producer, PackageId, PackageVersion, context.CancellationToken);

        var second = await _feed.PackAsync(producer, PackageId, "1.2.4-fixture", context.CancellationToken);

        Assert.AreEqual(PackageVersion, first.Version);
        Assert.AreEqual("1.2.4-fixture", second.Version);
        Assert.AreNotEqual(first.Path, second.Path);
        Assert.HasCount(2, Directory.GetFiles(_feed.DirectoryPath, "*.nupkg"));
        first.Verify();
        second.Verify();
    }

    [TestMethod]
    public async Task PackAsync_ConcurrentRequests_ObserveOnePublication()
    {
        await using var producer = Producer();
        ConfigureProducer(producer);

        var packages = await Task.WhenAll(
            _feed.PackAsync(producer, PackageId, PackageVersion, context.CancellationToken),
            _feed.PackAsync(producer, PackageId, PackageVersion, context.CancellationToken));

        Assert.AreSame(packages[0], packages[1]);
        Assert.HasCount(1, Directory.GetFiles(_feed.DirectoryPath, "*.nupkg"));
    }

    [TestMethod]
    public async Task PackAsync_ChangedInputsForExistingIdentity_RejectsTheConflict()
    {
        await using var producer = Producer();
        ConfigureProducer(producer);
        var package = await _feed.PackAsync(producer, PackageId, PackageVersion, context.CancellationToken);
        producer.WithProperty("Description", "changed inputs");

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _feed.PackAsync(producer, PackageId, PackageVersion, context.CancellationToken));

        Assert.Contains(PackageId + "/" + PackageVersion, error.Message);
        package.Verify();
    }

    [TestMethod]
    public async Task PackAsync_FailedProducer_DoesNotReserveThePackageIdentity()
    {
        await using var invalid = new FixtureProject("InvalidProducer", context: context);
        invalid.WithProperty("OutputType", "Library")
            .AddSource("Model.cs", "public class Model { MissingType Value; }");
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _feed.PackAsync(invalid, PackageId, PackageVersion, context.CancellationToken));
        Assert.Contains("CS0246", error.Message);
        await using var valid = Producer();
        ConfigureProducer(valid);

        var package = await _feed.PackAsync(valid, PackageId, PackageVersion, context.CancellationToken);

        Assert.AreEqual(PackageId, package.Id);
        Assert.AreEqual(PackageVersion, package.Version);
        package.Verify();
    }

    [TestMethod]
    public async Task Verify_ModifiedPackage_RejectsTheChangedArchive()
    {
        await using var producer = Producer();
        ConfigureProducer(producer);
        var package = await _feed.PackAsync(producer, PackageId, PackageVersion, context.CancellationToken);
        await File.AppendAllTextAsync(package.Path, "modified", context.CancellationToken);

        _ = Assert.ThrowsExactly<InvalidDataException>(package.Verify);
    }

    private FixtureProject Producer()
    {
        return new FixtureProject("FixtureProducer", context: context);
    }

    private static void ConfigureProducer(FixtureProject project)
    {
        project
            .WithProperty("OutputType", "Library")
            .AddSource("ProducedValue.cs", "public static class ProducedValue { public static int Number => 42; }");
    }

    [TestMethod]
    public void Constructor_BeforePackaging_DoesNotCreateTheFeed()
    {
        Assert.IsFalse(Directory.Exists(_feed.DirectoryPath));
    }

    [TestMethod]
    public async Task DisposeAsync_PublishedFeed_RemovesItsPackages()
    {
        await using var producer = Producer();
        ConfigureProducer(producer);
        var package = await _feed.PackAsync(producer, PackageId, PackageVersion, context.CancellationToken);

        await _feed.DisposeAsync();

        Assert.IsFalse(Directory.Exists(_feed.DirectoryPath));
        Assert.IsFalse(File.Exists(package.Path));
    }
}
