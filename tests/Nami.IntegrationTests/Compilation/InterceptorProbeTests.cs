using System.Runtime.Loader;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Nami.Tests.Compilation;

namespace Nami.IntegrationTests.Compilation;

/// <summary>
/// A generator built against the pinned Roslyn package emits <c>[InterceptsLocation]</c>; the compiler that
/// consumes it is the SDK's, which is newer. Both tests fail when that compiler stops binding the interceptor.
/// </summary>
[TestClass]
[TestCategory("Compiler")]
public sealed class InterceptorProbeTests(TestContext context)
{
    private const string InterceptorsNamespace = "Nami.IntegrationTests.InterceptorProbe.Generated";

    private const string ProbeSource = """
                                       public static class Probe
                                       {
                                           public static string Target() => "original";
                                       }

                                       public static class Caller
                                       {
                                           public static string Call() => Probe.Target();
                                       }
                                       """;

    private static string GeneratorPath =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "InterceptorProbe", "InterceptorProbe.dll");

    [TestMethod]
    public void InterceptorProbe_PinnedRoslynGenerator_IsBoundInProcess()
    {
        var generator = LoadGenerator();
        var builtAgainst = generator.GetType().Assembly.GetReferencedAssemblies()
            .Single(reference => reference.Name == "Microsoft.CodeAnalysis.CSharp").Version;
        Assert.IsNotNull(builtAgainst);
        Assert.AreEqual(PinnedRoslynVersion(), new Version(builtAgainst.Major, builtAgainst.Minor, builtAgainst.Build));
        var project = new FixtureProjectDefinition("InterceptedLibrary", "net11.0")
            .WithProperty("OutputType", "Library")
            .WithProperty("InterceptorsNamespaces", InterceptorsNamespace)
            .AddSource("Probe.cs", ProbeSource);

        var result = InProcessCompiler.Generate(project, CompilerReferences.Framework, generator,
            cancellationToken: context.CancellationToken);

        Assert.IsEmpty(result.Diagnostics, DiagnosticReport.Format(result.Diagnostics));
        Assert.ContainsSingle(result.Sources);
        using var image = new MemoryStream();
        var emitted = result.Compilation.Emit(image, cancellationToken: context.CancellationToken);
        Assert.IsEmpty(emitted.Diagnostics.Where(item => item.Severity >= DiagnosticSeverity.Warning),
            DiagnosticReport.Format(emitted.Diagnostics));
        Assert.IsTrue(emitted.Success);
        image.Position = 0;
        var loader = new AssemblyLoadContext("interceptor-probe", true);
        try
        {
            var call = loader.LoadFromStream(image).GetType("Caller", true)!.GetMethod("Call");
            Assert.IsNotNull(call);
            Assert.AreEqual("intercepted:original", call.Invoke(null, null));
        }
        finally
        {
            loader.Unload();
        }
    }

    [TestMethod]
    [Timeout(180_000, CooperativeCancellation = true)]
    public async Task InterceptorProbe_SdkCompiler_BindsAndRunsUnderNativeAot()
    {
        await using var project = new FixtureProject("InterceptedNative", context: context);
        project.WithProperty("InterceptorsNamespaces", InterceptorsNamespace)
            .AddAnalyzerReference(GeneratorPath)
            .AddSource("Probe.cs", ProbeSource)
            .AddSource("Program.cs", "Console.Write(Caller.Call());");

        var build = await project.PublishAsync(true, context.CancellationToken);

        Assert.AreEqual(0, build.ExitCode, build.Output + build.Error);
        var run = await ChildProcess.RunAsync(new ProcessStartInfo(build.NativePath), "the native interceptor probe",
            context.CancellationToken);
        Assert.AreEqual(0, run.ExitCode, run.Error);
        Assert.AreEqual("intercepted:original", run.Output);
    }

    private static IIncrementalGenerator LoadGenerator()
    {
        var type = AssemblyLoadContext.Default.LoadFromAssemblyPath(GeneratorPath)
            .GetType("Nami.IntegrationTests.InterceptorProbe.InterceptorProbeGenerator", true)!;
        return (IIncrementalGenerator)Activator.CreateInstance(type)!;
    }

    private static Version PinnedRoslynVersion()
    {
        var pinned = XElement.Load(Path.Combine(AppContext.BaseDirectory, "fixture-defaults.xml")).Elements("Package")
            .Where(package => (string?)package.Attribute("Id") == "Microsoft.CodeAnalysis.CSharp")
            .Select(package => (string?)package.Attribute("Version")).Single()!;
        return Version.Parse(pinned.Split('-')[0]);
    }
}
