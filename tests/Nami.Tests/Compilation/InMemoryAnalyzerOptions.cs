using System.Collections.Immutable;
using JetBrains.Annotations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Nami.Tests.Compilation;

/// <summary>Immutable analyzer options addressed by virtual source and additional-file paths.</summary>
[PublicAPI]
public sealed class InMemoryAnalyzerOptions : AnalyzerConfigOptionsProvider
{
    private readonly AnalyzerConfigOptions _empty = new Options(ImmutableDictionary<string, string>.Empty);
    private readonly ImmutableDictionary<string, AnalyzerConfigOptions> _files;

    public InMemoryAnalyzerOptions(IReadOnlyDictionary<string, string>? global = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? files = null)
    {
        GlobalOptions = new Options(global?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
                                    ?? ImmutableDictionary<string, string>.Empty);
        _files = files?.ToImmutableDictionary(pair => pair.Key,
                     AnalyzerConfigOptions (pair) =>
                         new Options(pair.Value.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)),
                     StringComparer.Ordinal)
                 ?? ImmutableDictionary<string, AnalyzerConfigOptions>.Empty;
    }

    public override AnalyzerConfigOptions GlobalOptions { get; }

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
    {
        return _files.GetValueOrDefault(tree.FilePath, _empty);
    }

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
    {
        return _files.GetValueOrDefault(textFile.Path, _empty);
    }

    private sealed class Options(ImmutableDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            return values.TryGetValue(key, out value!);
        }
    }
}
