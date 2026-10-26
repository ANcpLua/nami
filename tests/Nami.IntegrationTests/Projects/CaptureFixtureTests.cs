using System.Collections.Concurrent;
using System.Text.Json;

namespace Nami.IntegrationTests.Projects;

[UsesFixture<CaptureFixture>]
public sealed class CaptureFixtureTests(TestContext context)
{
    private static readonly ConcurrentBag<string> s_directories = [];

    [TestInitialize]
    public void RecordFixtureDirectory()
    {
        s_directories.Add(FixtureContext.Current<CaptureFixture>().DirectoryPath);
    }

    [ClassCleanup]
    public static void VerifyFixtureDirectoriesWereRemoved()
    {
        Assert.IsNotEmpty(s_directories);
        Assert.IsEmpty(s_directories.Where(Directory.Exists));
    }

    [TestMethod]
    [Timeout(180_000, CooperativeCancellation = true)]
    public async Task PublishAsync_CaptureConsumer_RecordsTelemetryUnderNativeAot()
    {
        var project = FixtureContext.Current<CaptureFixture>()
            .AddSource("Program.cs", """
                                     using System.Diagnostics;
                                     using OpenTelemetry;
                                     using OpenTelemetry.Trace;
                                     using Nami.Tests.Capture;

                                     CapturedActivityAssertions.RequireNativeAot();
                                     var exporter = new ActivitySnapshotExporter("native-fixture");
                                     using var provider = Sdk.CreateTracerProviderBuilder()
                                         .AddSource("native-fixture")
                                         .AddProcessor(new SimpleActivityExportProcessor(exporter)).Build();
                                     using var source = new ActivitySource("native-fixture");
                                     using (var activity = source.StartActivity("captured-operation"))
                                     {
                                         if (activity is null) throw new InvalidOperationException("Activity was not created.");
                                         activity.SetTag("fixture.value", "preserved");
                                     }
                                     var snapshot = exporter.GetSnapshots().Single();
                                     if (!Equals(snapshot.Tags["fixture.value"], "preserved"))
                                         throw new InvalidOperationException("The exported attribute changed.");
                                     Console.Write(snapshot.Operation);
                                     """);

        var build = await project.PublishAsync(true, context.CancellationToken);
        Assert.AreEqual(0, build.ExitCode, build.Output + build.Error);
        using var assets = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(project.DirectoryPath, "obj", "project.assets.json"), context.CancellationToken));
        Assert.IsEmpty(assets.RootElement.GetProperty("libraries").EnumerateObject()
            .Where(library => library.Name.StartsWith("MSTest", StringComparison.Ordinal)
                              || library.Name.StartsWith("Microsoft.Testing.", StringComparison.Ordinal)
                              || library.Name.StartsWith("Nami.Tests", StringComparison.Ordinal)));
        var run = await ChildProcess.RunAsync(new ProcessStartInfo(build.NativePath), "the native capture consumer",
            context.CancellationToken);
        Assert.AreEqual(0, run.ExitCode, run.Error);
        Assert.AreEqual("captured-operation", run.Output);
    }

    [TestMethod]
    [DataRow("first-operation")]
    [DataRow("second-operation")]
    public async Task Attribute_EachDataRow_ProvidesAnIsolatedCaptureConsumer(string operation)
    {
        var project = FixtureContext.Current<CaptureFixture>();
        Assert.IsFalse(Directory.Exists(project.DirectoryPath));
        project.AddSource("Program.cs", $"""
                                         using System.Diagnostics;
                                         using Nami.Tests.Capture;
                                         using var activity = new Activity("{operation}");
                                         activity.Start();
                                         Console.Write(ActivitySnapshot.Capture(activity).Operation);
                                         """);

        var build = await project.BuildAsync(context.CancellationToken);
        Assert.AreEqual(0, build.ExitCode, build.Output + build.Error);
        var start = new ProcessStartInfo("dotnet");
        start.ArgumentList.Add(build.AssemblyPath);
        var run = await ChildProcess.RunAsync(start, "the attributed capture consumer", context.CancellationToken);
        Assert.AreEqual(0, run.ExitCode, run.Error);
        Assert.AreEqual(operation, run.Output);
    }
}
