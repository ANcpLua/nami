using System.Collections.Immutable;
using System.Text;
using JetBrains.Annotations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Nami.Tests.Compilation;

/// <summary>Compiles virtual sources with the SDK compiler and caller-selected reference assemblies.</summary>
[PublicAPI]
public static class InProcessCompiler
{
    public static CSharpCompilation CreateCompilation(FixtureProjectDefinition definition,
        IEnumerable<MetadataReference> references, CSharpCompilationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(references);
        if (definition.Properties.ContainsKey("TargetFrameworks"))
            throw new NotSupportedException("Create a separate compilation and reference set for each target framework.");

        var parseOptions = ParseOptions(definition);
        var trees = definition.Sources.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
            CSharpSyntaxTree.ParseText(SourceText.From(pair.Value, Encoding.UTF8), parseOptions, pair.Key,
                cancellationToken)).ToList();
        if (definition.Properties.GetValueOrDefault("ImplicitUsings") is "enable" or "true")
        {
            trees.Add(CSharpSyntaxTree.ParseText(SourceText.From(
                "global using System; global using System.Collections.Generic; global using System.IO; global using System.Linq; global using System.Net.Http; global using System.Threading; global using System.Threading.Tasks;",
                Encoding.UTF8), parseOptions, "__fixture_global_usings.g.cs", cancellationToken));
        }

        var output = definition.Properties.GetValueOrDefault("OutputType") is "Exe" or "WinExe"
            ? OutputKind.ConsoleApplication
            : OutputKind.DynamicallyLinkedLibrary;
        var compilationOptions = options ?? new CSharpCompilationOptions(output,
            optimizationLevel: definition.Properties.GetValueOrDefault("Optimize") == "false"
                ? OptimizationLevel.Debug
                : OptimizationLevel.Release,
            deterministic: true,
            allowUnsafe: definition.Properties.GetValueOrDefault("AllowUnsafeBlocks") == "true",
            nullableContextOptions: definition.Properties.GetValueOrDefault("Nullable") switch
            {
                "disable" => NullableContextOptions.Disable,
                "warnings" => NullableContextOptions.Warnings,
                "annotations" => NullableContextOptions.Annotations,
                _ => NullableContextOptions.Enable
            });
        return CSharpCompilation.Create(definition.Name, trees,
            references,
            compilationOptions);
    }

    public static InProcessCompilationResult Compile(FixtureProjectDefinition definition,
        IEnumerable<MetadataReference> references, CSharpCompilationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var compilation = CreateCompilation(definition, references, options, cancellationToken);
        using var image = new MemoryStream();
        using var symbols = new MemoryStream();
        var result = compilation.Emit(image, symbols,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb,
                pdbFilePath: definition.Name + ".pdb"),
            cancellationToken: cancellationToken);
        return new InProcessCompilationResult(compilation, result.Success, result.Diagnostics,
            result.Success ? [.. image.ToArray()] : [], result.Success ? [.. symbols.ToArray()] : []);
    }

    public static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(FixtureProjectDefinition definition,
        IEnumerable<MetadataReference> references, ImmutableArray<DiagnosticAnalyzer> analyzers, ImmutableArray<AdditionalText> additionalTexts = default,
        AnalyzerConfigOptionsProvider? options = null, CancellationToken cancellationToken = default)
    {
        var compilation = CreateCompilation(definition, references, cancellationToken: cancellationToken);
        if (analyzers.IsDefaultOrEmpty) return Task.FromResult(compilation.GetDiagnostics(cancellationToken));
        var analyzerOptions = new AnalyzerOptions(additionalTexts.IsDefault ? [] : additionalTexts,
            options ?? new InMemoryAnalyzerOptions());
        var execution = new CompilationWithAnalyzersOptions(analyzerOptions, null,
            false, false, false);
        return compilation.WithAnalyzers(analyzers, execution).GetAllDiagnosticsAsync(cancellationToken);
    }

    public static InProcessGeneratorResult Generate(FixtureProjectDefinition definition,
        IEnumerable<MetadataReference> references, IIncrementalGenerator generator,
        ImmutableArray<AdditionalText> additionalTexts = default, AnalyzerConfigOptionsProvider? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(generator);
        var compilation = CreateCompilation(definition, references, cancellationToken: cancellationToken);
        GeneratorDriver driver = CSharpGeneratorDriver.Create([generator.AsSourceGenerator()],
            additionalTexts.IsDefault ? [] : additionalTexts, ParseOptions(definition),
            options ?? new InMemoryAnalyzerOptions(),
            new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, true));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics,
            cancellationToken);
        return new InProcessGeneratorResult((CSharpCompilation)output, diagnostics, driver.GetRunResult());
    }

    private static CSharpParseOptions ParseOptions(FixtureProjectDefinition definition)
    {
        var language = LanguageVersion.CSharp15;
        if (definition.Properties.TryGetValue("LangVersion", out var configured)
            && !LanguageVersionFacts.TryParse(configured, out language))
            throw new ArgumentException("Invalid C# language version: " + configured, nameof(definition));
        var symbols = definition.Properties.GetValueOrDefault("DefineConstants", "")
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new CSharpParseOptions(language, preprocessorSymbols: symbols);
    }
}

[PublicAPI]
public sealed record InProcessCompilationResult(
    CSharpCompilation Compilation,
    bool Success,
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableArray<byte> Image,
    ImmutableArray<byte> Symbols)
{
    public string DiagnosticText => DiagnosticReport.Format(Diagnostics);
}

[PublicAPI]
public sealed record InProcessGeneratorResult(
    CSharpCompilation Compilation,
    ImmutableArray<Diagnostic> Diagnostics,
    GeneratorDriverRunResult RunResult)
{
    public ImmutableArray<GeneratedSourceResult> Sources =>
        [.. RunResult.Results.SelectMany(result => result.GeneratedSources)];
}
