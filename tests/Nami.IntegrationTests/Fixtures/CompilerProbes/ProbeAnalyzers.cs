using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Nami.IntegrationTests.CompilerProbes;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ThrowingAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor s_rule = new("FIXTURE999", "Failure probe", "Failure probe",
            "Fixture", DiagnosticSeverity.Warning, isEnabledByDefault: true);
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [s_rule];
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationAction(_ => throw new InvalidOperationException("analyzer failed"));
        }
    }

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NameAnalyzer : DiagnosticAnalyzer
    {
        public const string RuleId = "FIXTURE001";
        public const string Enabled = "fixture.enabled";
        private static readonly DiagnosticDescriptor s_rule = new(RuleId, "Blocked name", "Type '{0}' is blocked",
            "Fixture", DiagnosticSeverity.Warning, isEnabledByDefault: true);
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [s_rule];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ClassDeclaration);
        }

        private static void Analyze(SyntaxNodeAnalysisContext context)
        {
            var provider = context.Options.AnalyzerConfigOptionsProvider;
            var options = provider.GetOptions(context.Node.SyntaxTree);
            if (!options.TryGetValue(Enabled, out var enabled)) provider.GlobalOptions.TryGetValue(Enabled, out enabled);
            if (enabled != "true") return;
            var banned = context.Options.AdditionalFiles.Single().GetText(context.CancellationToken)!.ToString();
            var declaration = (ClassDeclarationSyntax)context.Node;
            if (declaration.Identifier.ValueText == banned)
                context.ReportDiagnostic(Diagnostic.Create(s_rule, declaration.Identifier.GetLocation(), banned));
        }
    }
