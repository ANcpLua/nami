using System.Text;
using JetBrains.Annotations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Nami.Tests.Compilation;

/// <summary>Source or analyzer input with a virtual path and no backing file.</summary>
[PublicAPI]
public sealed class InMemoryAdditionalText(string path, SourceText text) : AdditionalText
{
    private readonly SourceText _text = text ?? throw new ArgumentNullException(nameof(text));

    public InMemoryAdditionalText(string path, string text) : this(path, SourceText.From(text, Encoding.UTF8))
    {
    }

    public override string Path { get; } = path ?? throw new ArgumentNullException(nameof(path));

    public override SourceText GetText(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _text;
    }
}
