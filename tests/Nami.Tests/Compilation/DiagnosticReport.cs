using System.Globalization;
using JetBrains.Annotations;
using Microsoft.CodeAnalysis;

namespace Nami.Tests.Compilation;

/// <summary>Stable, location-aware compiler evidence suitable for an assertion message.</summary>
[PublicAPI]
public static class DiagnosticReport
{
    public static string Format(IEnumerable<Diagnostic> diagnostics)
    {
        return string.Join(Environment.NewLine, diagnostics.Select(Format));
    }

    public static string Format(Diagnostic diagnostic)
    {
        var position = diagnostic.Location.GetMappedLineSpan();
        var location = diagnostic.Location.IsInSource
            ? $"{position.Path}({position.StartLinePosition.Line + 1},{position.StartLinePosition.Character + 1}): "
            : "";
        return
            $"{location}{diagnostic.Severity.ToString().ToLowerInvariant()} {diagnostic.Id}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}";
    }
}
