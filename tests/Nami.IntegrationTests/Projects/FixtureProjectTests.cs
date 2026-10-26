using System.Text.Json;

namespace Nami.IntegrationTests.Projects;

[TestClass]
public sealed class FixtureProjectTests(TestContext context)
{
    [TestMethod]
    public async Task BuildAsync_OrdinaryConsumer_RunsWithoutTestRunnerDependencies()
    {
        await using var project = new FixtureProject("OrdinaryConsumer");
        project
            .AddSource("Program.cs", "Console.Write(\"fixture-ok\");");

        var build = await project.BuildAsync(context.CancellationToken);
        Assert.AreEqual(0, build.ExitCode, build.Output + build.Error);
        var run = await RunAsync(build.AssemblyPath);
        Assert.AreEqual(0, run.ExitCode, run.Error);
        Assert.AreEqual("fixture-ok", run.Output);
        using var dependencies = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(build.OutputDirectory, "OrdinaryConsumer.deps.json"), context.CancellationToken));
        var libraries = dependencies.RootElement.GetProperty("libraries").EnumerateObject()
            .Select(property => property.Name).ToArray();
        Assert.IsEmpty(libraries.Where(name => name.StartsWith("MSTest", StringComparison.Ordinal)
                                               || name.StartsWith("Microsoft.Testing.", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task BuildAsync_InvalidSource_ReturnsTheCompilerDiagnostic()
    {
        await using var project = new FixtureProject("InvalidConsumer");
        project
            .AddSource("Program.cs", "MissingType value = new(); Console.Write(value);");

        var build = await project.BuildAsync(context.CancellationToken);

        Assert.AreNotEqual(0, build.ExitCode);
        Assert.Contains("CS0246", build.Output + build.Error);
        Assert.IsFalse(File.Exists(build.AssemblyPath));
    }

    [TestMethod]
    public async Task BuildAsync_XmlCharactersInProperties_PreservesTheExactValue()
    {
        const string Description = "<fixture> & \"quoted\"";
        await using var project = new FixtureProject("EscapedConsumer");
        project
            .WithProperty("Description", Description)
            .AddSource("Nested Folder/Program.cs", """
                                                   using System.Reflection;
                                                   Console.Write(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyDescriptionAttribute>()!.Description);
                                                   """);

        var build = await project.BuildAsync(context.CancellationToken);
        Assert.AreEqual(0, build.ExitCode, build.Output + build.Error);
        var run = await RunAsync(build.AssemblyPath);
        Assert.AreEqual(0, run.ExitCode, run.Error);
        Assert.AreEqual(Description, run.Output);
    }

    [TestMethod]
    public async Task BuildAsync_CancelledToken_DoesNotMaterializeTheProject()
    {
        await using var project = new FixtureProject("CancelledConsumer");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        _ = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => project.BuildAsync(cancellation.Token));

        Assert.IsFalse(File.Exists(project.ProjectPath));
        Assert.IsFalse(Directory.Exists(project.DirectoryPath));
    }

    [TestMethod]
    public async Task DisposeAsync_MaterializedProject_RemovesItsDirectory()
    {
        var project = new FixtureProject("DisposableConsumer");
        var directory = project.DirectoryPath;
        project.Materialize();

        await project.DisposeAsync();

        Assert.IsFalse(Directory.Exists(directory));
    }

    private Task<(int ExitCode, string Output, string Error)> RunAsync(string assembly)
    {
        var start = new ProcessStartInfo("dotnet");
        start.ArgumentList.Add(assembly);
        return ChildProcess.RunAsync(start, "the generated ordinary consumer", context.CancellationToken);
    }
}
