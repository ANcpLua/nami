using Nami.Tests.Metadata;

namespace Nami.IntegrationTests.Projects;

[TestClass]
[DoNotParallelize]
public sealed class BuildContractTests(TestContext context)
{
    [TestMethod]
    public async Task RecordProperties_MultipleFrameworks_PreservesEachValueAndAssembly()
    {
        await using var project = Library("MultiFramework")
            .WithTargetFrameworks("net9.0", "net10.0")
            .WithProperty("Flavor", "nine", "'$(TargetFramework)' == 'net9.0'")
            .WithProperty("Flavor", "ten", "'$(TargetFramework)' == 'net10.0'")
            .RecordProperties("TargetFramework", "Flavor");
        ConfigureLibrary(project);

        var result = await project.BuildAsync(context.CancellationToken);

        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        Assert.HasCount(2, result.RecordedProperties);
        Assert.AreEqual("nine", result.GetRecordedProperty("Flavor", "net9.0"));
        Assert.AreEqual("ten", result.GetRecordedProperty("Flavor", "net10.0"));
        _ = Assert.ThrowsExactly<InvalidOperationException>(() => result.GetRecordedProperty("Flavor"));
        using var net9 = new PeImage([
            .. await File.ReadAllBytesAsync(result.GetAssemblyPath("net9.0"), context.CancellationToken)
        ]);
        using var net10 = new PeImage([
            .. await File.ReadAllBytesAsync(result.GetAssemblyPath("net10.0"), context.CancellationToken)
        ]);
        Assert.AreEqual(".NETCoreApp,Version=v9.0", net9.ReadTargetFrameworkName());
        Assert.AreEqual(".NETCoreApp,Version=v10.0", net10.ReadTargetFrameworkName());
    }

    [TestMethod]
    public async Task RecordProperties_SemicolonsAndEquals_PreservesTheWholeValue()
    {
        await using var project = Library("PropertyValue")
            .WithProperty("CustomValue", "first;second=third").RecordProperties("CustomValue");
        ConfigureLibrary(project);

        var result = await project.BuildAsync(context.CancellationToken);

        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        Assert.AreEqual("first;second=third", result.GetRecordedProperty("CustomValue"));
    }

    [TestMethod]
    public async Task CustomTarget_FailedRebuild_ReturnsTheErrorWithoutStaleProperties()
    {
        await using var project = Library("FailingRebuild").WithProperty("State", "first")
            .RecordProperties("State")
            .WithCustomTarget(new FixtureTarget("RejectRequestedBuild", "CoreCompile")
                .Error("FIXTURE123", "<rejected> & \"quoted\"", "'$(RejectBuild)' == 'true'"));
        ConfigureLibrary(project);
        var first = await project.BuildAsync(context.CancellationToken);
        Assert.AreEqual(0, first.ExitCode, first.Output + first.Error);
        project.WithProperty("RejectBuild", "true").WithProperty("State", "second");

        var second = await project.BuildAsync(context.CancellationToken);

        Assert.AreNotEqual(0, second.ExitCode);
        Assert.Contains("FIXTURE123", second.Output + second.Error);
        Assert.Contains("<rejected> & \"quoted\"", second.Output + second.Error);
        Assert.IsEmpty(second.RecordedProperties);
        Assert.AreEqual("first", first.GetRecordedProperty("State"));
    }

    [TestMethod]
    [ResourceLock(WellKnownResources.EnvironmentVariables)]
    public async Task BuildAsync_InheritedCiSettings_AreBlankUnlessExplicitlyRequested()
    {
        using var environment = new EnvironmentScope(("CI", "inherited"), ("GITHUB_ACTIONS", "inherited"),
            ("DOTNET_ENVIRONMENT", "inherited"));
        await using var project = Library("EnvironmentIsolation")
            .RecordProperties("CI", "GITHUB_ACTIONS", "DOTNET_ENVIRONMENT");
        ConfigureLibrary(project);

        var clean = await project.BuildAsync(context.CancellationToken);
        project.WithEnvironment("CI", "explicit");
        var overridden = await project.BuildAsync(context.CancellationToken);

        Assert.AreEqual(0, clean.ExitCode, clean.Output + clean.Error);
        Assert.AreEqual("", clean.GetRecordedProperty("CI"));
        Assert.AreEqual("", clean.GetRecordedProperty("GITHUB_ACTIONS"));
        Assert.AreEqual("", clean.GetRecordedProperty("DOTNET_ENVIRONMENT"));
        Assert.AreEqual(0, overridden.ExitCode, overridden.Output + overridden.Error);
        Assert.AreEqual("explicit", overridden.GetRecordedProperty("CI"));
        Assert.AreEqual("inherited", Environment.GetEnvironmentVariable("CI"));
    }

    private static FixtureProject Library(string name)
    {
        return new FixtureProject(name);
    }

    private static void ConfigureLibrary(FixtureProject project)
    {
        project
            .WithProperty("OutputType", "Library").AddSource("Model.cs", "public sealed class Model { }");
    }
}
