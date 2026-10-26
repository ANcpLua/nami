using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nami.Tests.Infrastructure;
using Nami.SemanticConventions;

namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
[DoNotParallelize]
public sealed partial class UnionCompilerTests(TestContext context)
{
    private const string ContractDirectory = "internal/nami.contracts";
    private const string IngestionDirectory = "src/nami.collector.ingestion";
    private const string StorageDirectory = "src/nami.collector.storage";

    [TestMethod]
    public async Task Build_ActualGeneratedModelsAndConsumers_SucceedsWithProductionPolicy()
    {
        await using var project = new FixtureProject("GeneratedConsumers", context: context);
        AddActualSources(project);
        var result = await project.BuildAsync(context.CancellationToken);
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        Assert.AreEqual("false", result.GetRecordedProperty("TreatWarningsAsErrors"));
    }

    [TestMethod]
    [DataRow("Common.cs", "AttributeObjectValue")]
    [DataRow("OTel/Metrics.cs", "MetricNumberValue")]
    [DataRow("OTel/Metrics.cs", "MetricPointData")]
    [DataRow("Engine.cs", "EngineCommand")]
    [DataRow("Tools.cs", "SignalStatus")]
    public async Task Build_AddedWireCase_RejectsTheActualGeneratedWriter(string file, string type)
    {
        var path = ContractDirectory + "/Nami/Api/Contracts/" + file;
        var source = await File.ReadAllTextAsync(RepositoryPath(path), context.CancellationToken);
        var pattern = "(?m)^public readonly union " + type + @"\(([^\r\n]+)\);$";
        var declaration = Regex.Match(source, pattern, RegexOptions.CultureInvariant);
        Assert.IsTrue(declaration.Success, "The generated union declaration must be present: " + type);
        var changed = source.Replace(declaration.Value,
            "public readonly union " + type + "(" + declaration.Groups[1].Value + ", AddedCase);",
            StringComparison.Ordinal) + "\npublic sealed class AddedCase;\n";
        await using var project = new FixtureProject("AddedWireCase", context: context);
        AddActualSources(project, path, changed);
        var result = await project.BuildAsync(context.CancellationToken);
        AssertMissingCase(result, "AddedCase", file);
    }

    [TestMethod]
    [DataRow("Nami.Collector.Ingestion.Models", "MetricPointData", "IngestionMappings.g.cs", false)]
    [DataRow("Nami.Collector.Ingestion.Models", "MetricNumberValue", "IngestionMappings.g.cs", false)]
    [DataRow("Nami.Collector.Ingestion.Models", "MetricShape", "OtlpIngestionModels.cs", false)]
    [DataRow("Nami.Collector.Storage", "StoredAttributeValue", "StoredAttributeProjection.g.cs", true)]
    [DataRow("Nami.Collector.Storage", "StoredObjectValue", "StoredAttributeProjection.g.cs", true)]
    [DataRow("Nami.Collector.Storage", "StoredNumberValue", "MetricObservationCodec.cs", true)]
    [DataRow("Nami.Collector.Storage", "StoredMetricPointData", "MetricProjection.cs", false)]
    public async Task Build_AddedHierarchyLeaf_RejectsTheActualConsumer(
        string ns, string type, string consumer, bool canonicalWriter)
    {
        await using var project = new FixtureProject("AddedHierarchyLeaf", context: context);
        AddActualSources(project);
        project.AddSource("AddedCase.cs", $$"""
            namespace {{ns}};
            internal sealed class AddedCase : {{type}}
            {
                {{(canonicalWriter ? "internal override void WriteCanonical(Utf8JsonWriter writer) => throw new NotSupportedException();" : "")}}
                {{(type == "StoredAttributeValue" ? "internal override void WriteJson(Utf8JsonWriter writer, JsonSerializerOptions options) => throw new NotSupportedException();" : "")}}
            }
            """);
        var result = await project.BuildAsync(context.CancellationToken);
        AssertMissingCase(result, "AddedCase", consumer);
    }

    [TestMethod]
    [Timeout(300_000, CooperativeCancellation = true)]
    public async Task Publish_ActualUnionWriters_RoundTripsEveryCaseUnderNativeAot()
    {
        await using var project = new FixtureProject("NativeUnionWriters", context: context);
        AddActualSources(project);
        project.WithProperty("OutputType", "Exe").AddSource("Program.cs", """
            using Nami.Api.Contracts;
            using Nami.Api.Contracts.Common;
            using Nami.Api.Contracts.Engine;
            using Nami.Api.Contracts.OTel.Metrics;
            using Nami.Api.Contracts.Tools;

            if (JsonSerializer.IsReflectionEnabledByDefault)
                throw new InvalidOperationException("Reflection must remain disabled.");
            (string Name, string Json)[] cases =
            [
                ("Common.AttributeObjectValue", "{\"kind\":\"int\",\"value\":\"-9223372036854775808\"}"),
                ("Common.AttributeObjectValue", "{\"kind\":\"double\",\"value\":\"NaN\"}"),
                ("Common.AttributeObjectValue", "{\"kind\":\"bytes\",\"base64\":\"AP8=\"}"),
                ("Common.AttributeObjectValue", "{\"kind\":\"kvlist\",\"values\":{\"type\":null,\"kind\":\"kept\"}}"),
                ("OTel.Metrics.MetricNumberValue", "{\"kind\":\"int\",\"value\":\"9223372036854775807\"}"),
                ("OTel.Metrics.MetricNumberValue", "{\"kind\":\"double\",\"value\":1.25}"),
                ("OTel.Metrics.MetricPointData", "{\"kind\":\"number\"}"),
                ("OTel.Metrics.MetricPointData", "{\"kind\":\"histogram\",\"count\":\"1\",\"explicit_bounds\":[],\"bucket_counts\":[\"1\"]}"),
                ("OTel.Metrics.MetricPointData", "{\"kind\":\"exponential_histogram\",\"count\":\"1\",\"scale\":0,\"zero_count\":\"1\",\"zero_threshold\":0}"),
                ("OTel.Metrics.MetricPointData", "{\"kind\":\"summary\",\"count\":\"1\",\"sum\":0,\"quantile_values\":[]}"),
                ("Engine.EngineCommand", "{\"id\":\"1\",\"revision\":\"test\",\"method\":\"call\",\"name\":\"sources_status\",\"input\":{},\"credential\":null,\"traceparent\":null,\"tracestate\":null}"),
                ("Engine.EngineCommand", "{\"id\":\"2\",\"revision\":\"test\",\"method\":\"health\"}"),
                ("Engine.EngineCommand", "{\"id\":\"3\",\"revision\":\"test\",\"method\":\"cancel\"}"),
                ("Tools.SignalStatus", "{\"signal\":\"traces\",\"kind\":\"active\",\"last_seen\":\"2026-10-26T00:00:00Z\"}"),
                ("Tools.SignalStatus", "{\"signal\":\"logs\",\"kind\":\"silent\",\"silent_for\":\"PT1M\"}")
            ];
            foreach (var (name, json) in cases)
            {
                var metadata = ContractTypes.TypeInfo(name);
                var value = JsonSerializer.Deserialize(json, metadata);
                var encoded = JsonSerializer.Serialize(value, metadata);
                if (encoded != json)
                    throw new InvalidOperationException($"{name}: expected {json}, got {encoded}");
            }
            object[] defaults =
            [
                default(AttributeObjectValue), default(MetricNumberValue), default(MetricPointData),
                default(EngineCommand), default(SignalStatus)
            ];
            foreach (var value in defaults)
                RequireFailure<InvalidOperationException>(() => JsonSerializer.Serialize(value,
                    ContractTypes.TypeInfo(ContractTypes.NameOf(value.GetType()))));
            foreach (var name in cases.Select(static item => item.Name).Distinct())
            {
                var metadata = ContractTypes.TypeInfo(name);
                RequireFailure<JsonException>(() => JsonSerializer.Deserialize("{}", metadata));
                RequireFailure<JsonException>(() => JsonSerializer.Deserialize(
                    name == "Engine.EngineCommand" ? "{\"method\":\"unknown\"}" : "{\"kind\":\"unknown\"}", metadata));
            }
            Console.Write("15 roundtrips; 5 defaults rejected; 10 invalid tags rejected; reflection disabled");

            static void RequireFailure<T>(Action action) where T : Exception
            {
                try { action(); }
                catch (T) { return; }
                throw new InvalidOperationException($"Expected {typeof(T).Name}.");
            }
            """);
        var published = await project.PublishAsync(true, context.CancellationToken);
        Assert.AreEqual(0, published.ExitCode, published.Output + published.Error);
        var run = await ChildProcess.RunAsync(new ProcessStartInfo(
                Path.Combine(published.OutputDirectory, project.Definition.Name + (OperatingSystem.IsWindows() ? ".exe" : ""))),
            "the NativeAOT union consumer", context.CancellationToken);
        Assert.AreEqual(0, run.ExitCode, run.Output + run.Error);
        Assert.AreEqual("15 roundtrips; 5 defaults rejected; 10 invalid tags rejected; reflection disabled", run.Output);
    }

    private static void AssertMissingCase(FixtureBuildResult result, string type, string consumer)
    {
        var output = result.Output + result.Error;
        Assert.AreNotEqual(0, result.ExitCode, "Adding a case must invalidate the unchanged consumer.");
        var errors = CompilerErrors().Matches(output)
            .Select(static match => match.Groups[1].Value).Distinct().ToArray();
        Assert.AreSequenceEqual(["CS8509"], errors, output);
        Assert.Contains(line => line.Contains("error CS8509:", StringComparison.Ordinal)
                                && line.Contains(type, StringComparison.Ordinal)
                                && line.Contains(consumer, StringComparison.Ordinal), output.Split('\n'), output);
    }

    private static void AddActualSources(FixtureProject project, string? replacedPath = null, string? replacement = null)
    {
        project.WithProperty("OutputType", "Library")
            .WithProperty("TreatWarningsAsErrors", "false")
            .WithProperty("WarningsAsErrors", string.Join(";",
                XDocument.Load(RepositoryPath("Directory.Build.props")).Root!.Elements("PropertyGroup")
                    .Where(static group => group.Attribute("Condition") is null)
                    .Elements("WarningsAsErrors")
                    .Select(static property => property.Value.Replace("$(WarningsAsErrors)", "", StringComparison.Ordinal))))
            .RecordProperties("TreatWarningsAsErrors", "WarningsAsErrors");
        foreach (var file in Directory.EnumerateFiles(RepositoryPath(ContractDirectory), "*.cs")
                     .Concat(Directory.EnumerateFiles(RepositoryPath(ContractDirectory + "/Nami"), "*.cs", SearchOption.AllDirectories))
                     .Concat(Directory.EnumerateFiles(RepositoryPath(IngestionDirectory + "/Generated"), "*.cs"))
                     .Concat(Directory.EnumerateFiles(RepositoryPath(StorageDirectory + "/Generated"), "*.cs")))
            Add(file);
        foreach (var path in new[]
                 {
                     IngestionDirectory + "/OtlpIngestionModels.cs",
                     IngestionDirectory + "/NormalizedAttributes.cs",
                     StorageDirectory + "/StoredAttributeValue.cs",
                     StorageDirectory + "/CanonicalAttributeOrder.cs",
                     StorageDirectory + "/StorageJsonSerializerContext.cs",
                     StorageDirectory + "/MetricProjection.cs",
                     StorageDirectory + "/MetricObservationCodec.cs"
                 }) Add(RepositoryPath(path));
        var attributes = CSharpSyntaxTree.ParseText(File.ReadAllText(
            RepositoryPath("internal/nami.collector.storage.generators/DuckDbAttributes.cs")))
            .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(static type => type.Identifier.ValueText == "DuckDbAttributeSource")
            .DescendantNodes().OfType<LiteralExpressionSyntax>().Single().Token.ValueText;
        project.AddSource("DuckDbAttributes.g.cs", attributes);
        project.AddAssemblyReference(typeof(AttributeStability).Assembly.Location);
        project.AddSource("Usings.cs", """
            global using System.Buffers;
            global using System.Globalization;
            global using System.Diagnostics.CodeAnalysis;
            global using Nami.SemanticConventions;
            global using System.Security.Cryptography;
            global using System.Text;
            global using System.Text.Json;
            global using System.Text.Json.Serialization;
            global using Nami.Collector.Ingestion;
            """);


        return;

        void Add(string file)
        {
            var relative = Path.GetRelativePath(ProcessRunner.RepositoryRoot, file).Replace('\\', '/');
            project.AddSource(relative, relative == replacedPath ? replacement! : File.ReadAllText(file));
        }
    }

    [GeneratedRegex(@"\berror (CS\d+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex CompilerErrors();

    private static string RepositoryPath(string path) => Path.Combine(ProcessRunner.RepositoryRoot, path);
}
