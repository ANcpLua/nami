using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nami.Api.Contracts;
using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
[DependsOn(typeof(RegistryGateTests), nameof(RegistryGateTests.Catalog_MatchesResolvedRegistry))]
public sealed class UnionCompilerTests(TestContext context)
{
    private static MetadataReference[] References => ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Append(typeof(ContractJson).Assembly.Location).Distinct()
        .Select(static p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToArray();

    private static Diagnostic[] Errors(string code)
    {
        return CSharpCompilation.Create("union-proof",
                [CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Preview))], References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable,
                    generalDiagnosticOption: ReportDiagnostic.Error))
            .GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
    }

    [TestMethod]
    public void Union_SwitchWithoutDefault_IsExhaustive()
    {
        const string source = """
                              using Nami.Api.Contracts.OTel.Metrics;
                              public static class Consumer { public static int Read(MetricPointData v) => v switch {
                              MetricNumberPointData => 1, MetricHistogramPointData => 2, MetricExponentialHistogramPointData => 3, MetricSummaryPointData => 4 }; }
                              """;
        var errors = Errors(source);
        Assert.HasCount(0, errors, string.Join("\n", errors.Select(static e => e.ToString())));
    }

    [TestMethod]
    public async Task Union_AddedCase_BreaksBuild()
    {
        var source = await File.ReadAllTextAsync(
            Path.Combine(ProcessRunner.RepositoryRoot, "internal/nami.contracts/Nami/Api/Contracts/OTel/Metrics.cs"),
            context.CancellationToken);
        var line = source.Split('\n')
            .Single(static l => l.StartsWith("public union MetricPointData(", StringComparison.Ordinal));
        var union = line.Replace(");", ", AddedCase);", StringComparison.Ordinal);
        var errors = Errors("using Nami.Api.Contracts.OTel.Metrics; namespace Probe { " + union +
                            " public class AddedCase {} public static class Consumer { public static int Read(MetricPointData v) => v switch { MetricNumberPointData => 1, MetricHistogramPointData => 2, MetricExponentialHistogramPointData => 3, MetricSummaryPointData => 4 }; }}");
        Assert.IsTrue(errors.Any(static d => d.Id == "CS8509"),
            string.Join("\n", errors.Select(static e => e.ToString())));
        Assert.Contains("AddedCase", string.Join("\n", errors.Select(static e => e.ToString())));
    }
}
