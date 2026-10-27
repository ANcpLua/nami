using System.Collections.Immutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace Nami.IntegrationTests.Compilation;

/// <summary>The reference pack selected by the actual SDK build of the test support assembly.</summary>
internal static class CompilerReferences
{
    internal static ImmutableArray<MetadataReference> Framework { get; } = Load();

    private static ImmutableArray<MetadataReference> Load()
    {
        var defaults = XElement.Load(Path.Combine(AppContext.BaseDirectory, "fixture-defaults.xml"));
        var paths = defaults.Elements("Reference").Select(reference => (string?)reference.Attribute("Path")
            ?? throw new InvalidDataException("A staged framework reference is missing its path.")).ToArray();
        if (paths.Length == 0) throw new InvalidDataException("The SDK framework references were not staged.");
        return [.. paths.Order(StringComparer.Ordinal).Select(path => MetadataReference.CreateFromFile(path))];
    }
}
