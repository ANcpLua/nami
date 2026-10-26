using System.Globalization;
using Microsoft.CodeAnalysis;
using Nami.Tests.Compilation;
using Nami.IntegrationTests.CompilerProbes;

namespace Nami.IntegrationTests.Compilation;

[TestClass]
[TestCategory("Compiler")]
public sealed class InProcessCompilerTests(TestContext context)
{
    [TestMethod]
    public void Compile_DefineConstants_SelectsConditionalType()
    {
        var project = Library().WithProperty("DefineConstants", "FIRST;SECOND")
            .AddSource("Conditional.cs", """
                                         #if FIRST && SECOND
                                         public sealed class Selected { }
                                         #else
                                         #error Missing fixture symbols
                                         #endif
                                         """);

        var result = InProcessCompiler.Compile(project, cancellationToken: context.CancellationToken);

        Assert.IsTrue(result.Success, result.DiagnosticText);
        Assert.IsNotNull(result.Compilation.GetTypeByMetadataName("Selected"));
    }

    [TestMethod]
    public void Compile_ImplicitUsingsEnabled_ResolvesConsole()
    {
        var project = new FixtureProjectDefinition("ConsoleConsumer", "net10.0")
            .AddSource("Program.cs", "Console.WriteLine(\"hello\");");

        var result = InProcessCompiler.Compile(project, cancellationToken: context.CancellationToken);

        Assert.IsTrue(result.Success, result.DiagnosticText);
        Assert.IsNotNull(result.Compilation.GetEntryPoint(context.CancellationToken));
    }

    [TestMethod]
    public void Compile_MultipleVirtualFiles_ResolvesFrameworkAndUserTypes()
    {
        var project = Library().AddSource("Model.cs", "public sealed class Model { }")
            .AddSource("Consumer.cs",
                "public sealed class Consumer { public System.Collections.Generic.List<Model> Models { get; } = new(); }");

        var result = InProcessCompiler.Compile(project, cancellationToken: context.CancellationToken);

        Assert.IsTrue(result.Success, result.DiagnosticText);
        Assert.IsNotEmpty(result.Image);
        var type = result.Compilation.GetTypeByMetadataName("Consumer");
        Assert.IsNotNull(type);
        Assert.AreEqual(Accessibility.Public, type.DeclaredAccessibility);
        var property = Assert.ContainsSingle(type.GetMembers("Models").OfType<IPropertySymbol>());
        Assert.AreEqual("System.Collections.Generic.List<Model>", property.Type.ToDisplayString());
    }

    [TestMethod]
    public void Compile_UndefinedType_ReportsSourcePathAndToken()
    {
        var project = Library().AddSource("virtual/Consumer.cs",
            "public sealed class Consumer { public MissingType Value; }");

        var result = InProcessCompiler.Compile(project, cancellationToken: context.CancellationToken);

        Assert.IsFalse(result.Success);
        Assert.IsEmpty(result.Image);
        Assert.IsEmpty(result.Symbols);
        var error = Assert.ContainsSingle(result.Diagnostics.Where(item => item.Id == "CS0246"));
        Assert.AreEqual("virtual/Consumer.cs", error.Location.SourceTree!.FilePath);
        Assert.AreEqual("MissingType",
            error.Location.SourceTree.GetText(context.CancellationToken).ToString(error.Location.SourceSpan));
        Assert.Contains("virtual/Consumer.cs(1,", result.DiagnosticText);
    }

    [TestMethod]
    public void Compile_IdenticalInputs_EmitsIdenticalPeAndPdb()
    {
        var project = Library().AddSource("Model.cs", "public sealed class Model { public int Value => 42; }");

        var first = InProcessCompiler.Compile(project, cancellationToken: context.CancellationToken);
        var second = InProcessCompiler.Compile(project, cancellationToken: context.CancellationToken);

        Assert.IsTrue(first.Success, first.DiagnosticText);
        Assert.IsTrue(second.Success, second.DiagnosticText);
        Assert.AreSequenceEqual(first.Image, second.Image);
        Assert.AreSequenceEqual(first.Symbols, second.Symbols);
    }

    [TestMethod]
    public void Compile_CancelledToken_ThrowsBeforeCompilation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var token = cancellation.Token;

        _ = Assert.ThrowsExactly<OperationCanceledException>(() =>
            InProcessCompiler.Compile(Library(), cancellationToken: token));
    }

    [TestMethod]
    public void Compile_UnsupportedFramework_RejectsReferenceMismatch()
    {
        var project = new FixtureProjectDefinition("OtherFramework", "net9.0");

        _ = Assert.ThrowsExactly<NotSupportedException>(() =>
            InProcessCompiler.Compile(project, cancellationToken: context.CancellationToken));
    }

    [TestMethod]
    public void Generate_IncrementalSource_CompilesGeneratedConstant()
    {
        var project = Library().AddSource("Consumer.cs",
            "public sealed class Consumer { public int Value => GeneratedValue.Number; }");

        var result =
            InProcessCompiler.Generate(project, new ValueGenerator(), cancellationToken: context.CancellationToken);

        Assert.IsEmpty(result.Diagnostics);
        Assert.IsEmpty(result.Compilation.GetDiagnostics(context.CancellationToken)
            .Where(item => item.Severity == DiagnosticSeverity.Error));
        var generated = Assert.ContainsSingle(result.Sources);
        Assert.AreEqual("GeneratedValue.g.cs", generated.HintName);
        var type = result.Compilation.GetTypeByMetadataName("GeneratedValue");
        Assert.IsNotNull(type);
        var field = Assert.ContainsSingle(type.GetMembers("Number").OfType<IFieldSymbol>());
        Assert.AreEqual(42, field.ConstantValue);
    }

    [TestMethod]
    public void Generate_InitializationThrows_ReturnsFailureEvidence()
    {
        var result = InProcessCompiler.Generate(Library(), new ThrowingGenerator(),
            cancellationToken: context.CancellationToken);

        var generator = Assert.ContainsSingle(result.RunResult.Results);
        var error = Assert.IsInstanceOfType<InvalidOperationException>(generator.Exception);
        Assert.AreEqual("generator failed", error.Message);
        Assert.Contains("generator failed",
            Assert.ContainsSingle(result.Diagnostics).GetMessage(CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public async Task AnalyzeAsync_AnalyzerThrows_ReturnsFailureDiagnostic()
    {
        var project = Library().AddSource("Model.cs", "public sealed class Model { }");

        var diagnostics = await InProcessCompiler.AnalyzeAsync(project, [new ThrowingAnalyzer()],
            cancellationToken: context.CancellationToken);

        var failure = Assert.ContainsSingle(diagnostics.Where(item => item.Id == "AD0001"));
        Assert.Contains("analyzer failed", failure.GetMessage(CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public async Task AnalyzeAsync_AdditionalFileAndOptions_ReportsConfiguredType()
    {
        var project = Library().AddSource("virtual/Bad.cs", "public sealed class Bad { }");

        var diagnostics = await InProcessCompiler.AnalyzeAsync(project, [new NameAnalyzer()],
            [new InMemoryAdditionalText("/virtual/BannedSymbols.txt", "Bad")],
            new InMemoryAnalyzerOptions(new Dictionary<string, string> { [NameAnalyzer.Enabled] = "true" }),
            context.CancellationToken);

        var diagnostic = Assert.ContainsSingle(diagnostics.Where(item => item.Id == NameAnalyzer.RuleId));
        Assert.AreEqual("Bad",
            (await diagnostic.Location.SourceTree!.GetTextAsync(context.CancellationToken)).ToString(diagnostic.Location
                .SourceSpan));
        Assert.AreEqual("virtual/Bad.cs", diagnostic.Location.SourceTree.FilePath);
    }

    [TestMethod]
    public async Task AnalyzeAsync_PerFileOverride_DisablesGlobalRule()
    {
        var project = Library().AddSource("virtual/Bad.cs", "public sealed class Bad { }");
        var options = new InMemoryAnalyzerOptions(
            new Dictionary<string, string> { [NameAnalyzer.Enabled] = "true" },
            new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["virtual/Bad.cs"] = new Dictionary<string, string> { [NameAnalyzer.Enabled] = "false" }
            });

        var diagnostics = await InProcessCompiler.AnalyzeAsync(project, [new NameAnalyzer()],
            [new InMemoryAdditionalText("/virtual/BannedSymbols.txt", "Bad")], options, context.CancellationToken);

        Assert.IsEmpty(diagnostics);
    }

    private static FixtureProjectDefinition Library()
    {
        return new FixtureProjectDefinition("MemoryLibrary", "net10.0")
            .WithProperty("OutputType", "Library").WithProperty("ImplicitUsings", "disable");
    }

    private sealed class ValueGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(output =>
                output.AddSource("GeneratedValue.g.cs",
                    "public static class GeneratedValue { public const int Number = 42; }"));
        }
    }

    private sealed class ThrowingGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            throw new InvalidOperationException("generator failed");
        }
    }
}
