using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Nami.Instrumentation.Generators.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ChatClientBuilderBypassAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "NAMI0137";

    private static readonly string[] s_providerClients =
    [
        "OpenAI.OpenAIClient",
        "Azure.AI.OpenAI.AzureOpenAIClient",
        "Anthropic.AnthropicClient",
        "OllamaSharp.OllamaApiClient"
    ];

    private static readonly DiagnosticDescriptor s_rule = new(
        DiagnosticId,
        "Provider SDK client instantiated outside ChatClientBuilder",
        "Provider SDK clients may only be instantiated inside an `IXxxChatClientBuilder` implementation. " +
        "Resolve `IXxxChatClientBuilder` from DI and call `.BuildChatClient(provider)` instead.",
        "Nami.Instrumentation",
        DiagnosticSeverity.Warning,
        true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [s_rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var builder = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var symbol in s_providerClients
                         .Select(start.Compilation.GetTypeByMetadataName)
                         .OfType<INamedTypeSymbol>())
                builder.Add(symbol);

            if (builder.Count is 0)
                return;

            var providers = builder.ToImmutable();
            start.RegisterOperationAction(ctx => Analyze(ctx, providers), OperationKind.ObjectCreation);
        });
    }

    private static void Analyze(
        OperationAnalysisContext context,
        ImmutableHashSet<INamedTypeSymbol> providers)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (creation.Type is not INamedTypeSymbol created || !providers.Contains(created))
            return;

        if (IsAllowed(context.Operation.Syntax.SyntaxTree.FilePath))
            return;

        context.ReportDiagnostic(Diagnostic.Create(s_rule, creation.Syntax.GetLocation()));
    }

    private static bool IsAllowed(string path)
    {
        var normalized = "/" + path.Replace('\\', '/');
        return (normalized.ContainsIgnoreCase("/Clients/") &&
                normalized.EndsWithIgnoreCase("ChatClientBuilder.cs")) ||
               (normalized.ContainsIgnoreCase("/Factories/") &&
                normalized.EndsWithIgnoreCase("ChatClientFactory.cs"));
    }
}
