using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Nami.Instrumentation.Generators.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InlineSystemPromptAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "NAMI0136";

    private const int LengthThreshold = 40;
    private const string OptionsTypeName = "Microsoft.Agents.AI.ChatClientAgentOptions";
    private const string InstructionsMember = "Instructions";
    private const string InstructionsParameter = "instructions";

    private static readonly DiagnosticDescriptor s_rule = new(
        DiagnosticId,
        "Inline system-prompt literal",
        "System prompt literals must load from `.md` files with `ConcurrentDictionary<string, string>` " +
        "caching. Move the prompt to `Data/Instructions/<agent>.md` and load via `LoadInstructions(\"<agent>.md\")`.",
        "Nami.Instrumentation",
        DiagnosticSeverity.Warning,
        true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [s_rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeAssignment, OperationKind.SimpleAssignment);
        context.RegisterOperationAction(AnalyzeArgument, OperationKind.Argument);
    }

    private static void AnalyzeAssignment(OperationAnalysisContext context)
    {
        var assignment = (ISimpleAssignmentOperation)context.Operation;
        if (assignment.Target is not IPropertyReferenceOperation propertyRef)
            return;

        if (propertyRef.Property.Name != InstructionsMember ||
            propertyRef.Property.ContainingType?.ToDisplayString() != OptionsTypeName)
            return;

        Report(context, assignment.Value);
    }

    private static void AnalyzeArgument(OperationAnalysisContext context)
    {
        var argument = (IArgumentOperation)context.Operation;
        if (argument.Parameter?.Name != InstructionsParameter)
            return;

        var containing = argument.Parameter.ContainingSymbol?.ContainingType?.ToDisplayString();
        if (containing is not "Microsoft.Agents.AI.ChatClientAgent" and not OptionsTypeName)
            return;

        Report(context, argument.Value);
    }

    private static void Report(OperationAnalysisContext context, IOperation value)
    {
        if (value is not ILiteralOperation literal || literal.ConstantValue.Value is not string text)
            return;

        if (text.Length < LengthThreshold && !text.Contains('\n', StringComparison.Ordinal))
            return;

        context.ReportDiagnostic(Diagnostic.Create(s_rule, value.Syntax.GetLocation()));
    }
}
