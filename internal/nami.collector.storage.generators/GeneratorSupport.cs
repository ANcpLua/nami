// Support code for the DuckDB generator. Adapted from ANcpLua.Roslyn.Utilities.Sources
// 5.0.0-preview.1 (MIT), which this project compiled in until 15 Nov. Only what the
// generator uses is here. Escaping, quoting and hashing keep the package's exact behaviour,
// because the generated code and the schema hash depend on them.

using System.Collections;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;

namespace Nami.Collector.Storage.Generators;

/// <summary>An array with value equality, so incremental pipeline steps compare by content.</summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    private readonly T[]? _items;

    public EquatableArray(T[] items) => _items = items;

    public int Length => Items.Length;

    int IReadOnlyCollection<T>.Count => Items.Length;

    public T this[int index] => Items[index];

    private T[] Items => _items ?? Array.Empty<T>();

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    public bool Equals(EquatableArray<T> other)
    {
        var left = Items;
        var right = other.Items;
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var item in Items)
            {
                hash = (hash * 31) + EqualityComparer<T>.Default.GetHashCode(item);
            }

            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal static class EquatableArrayExtensions
{
    public static EquatableArray<T> ToEquatableArray<T>(this T[] items)
        where T : IEquatable<T> => new(items);
}

internal static class GeneratedCodeHelpers
{
    public const string NullableEnable = "#nullable enable";
}

/// <summary>A string builder that indents each new line by the current block depth (four spaces).</summary>
internal sealed class IndentedStringBuilder(int initialCapacity)
{
    private const string IndentString = "    ";

    private readonly StringBuilder _sb = new(initialCapacity);
    private int _indentLevel;
    private bool _needsIndent = true;

    public IndentedStringBuilder AppendLine()
    {
        _sb.AppendLine();
        _needsIndent = true;
        return this;
    }

    public IndentedStringBuilder AppendLine(string text)
    {
        WriteIndentIfNeeded();
        _sb.AppendLine(text);
        _needsIndent = true;
        return this;
    }

    public IndentedStringBuilder AppendLineNoIndent(string text)
    {
        _sb.AppendLine(text);
        _needsIndent = true;
        return this;
    }

    public void BeginBlock(string? opener = "{")
    {
        if (opener is not null)
        {
            AppendLine(opener);
        }

        _indentLevel++;
    }

    public void EndBlock(string? closer = "}")
    {
        if (_indentLevel > 0)
        {
            _indentLevel--;
        }

        if (closer is not null)
        {
            AppendLine(closer);
        }
    }

    public override string ToString() => _sb.ToString();

    private void WriteIndentIfNeeded()
    {
        if (!_needsIndent)
        {
            return;
        }

        for (var i = 0; i < _indentLevel; i++)
        {
            _sb.Append(IndentString);
        }

        _needsIndent = false;
    }
}

/// <summary>Escapes text for a regular C# string literal.</summary>
internal static class SourceString
{
    public static string EscapeCSharpString(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        var needsEscape = false;
        foreach (var ch in s)
        {
            if (ch is '"' or '\\' || char.IsControl(ch))
            {
                needsEscape = true;
                break;
            }
        }

        if (!needsEscape)
        {
            return s;
        }

        var builder = new StringBuilder(s.Length + 16);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\0':
                    builder.Append("\\0");
                    break;
                case '\a':
                    builder.Append("\\a");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\v':
                    builder.Append("\\v");
                    break;
                default:
                    if (char.IsControl(ch))
                    {
                        builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(ch);
                    }

                    break;
            }
        }

        return builder.ToString();
    }
}

/// <summary>Quotes a DuckDB identifier.</summary>
internal static class SqlIdentifier
{
    public static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}

/// <summary>Lower-case hexadecimal SHA-256 of the UTF-8 bytes of a string.</summary>
internal static class Sha256Hex
{
    public static string Hash(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
        var chars = new char[bytes.Length * 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            chars[i * 2] = HexChar(b >> 4);
            chars[(i * 2) + 1] = HexChar(b & 0xF);
        }

        return new string(chars);

        static char HexChar(int nibble) => (char)(nibble < 10 ? '0' + nibble : 'a' + nibble - 10);
    }
}

internal static class GeneratorExtensions
{
    public static IncrementalValuesProvider<TSource> WhereNotNull<TSource>(this IncrementalValuesProvider<TSource?> source)
        where TSource : struct =>
        source
            .Where(static x => x is not null)
            .Select(static (x, _) => x ?? throw new InvalidOperationException("Unexpected null value in WhereNotNull"));

    public static T? GetConstructorArgument<T>(this AttributeData attribute, int index)
    {
        var arguments = attribute.ConstructorArguments;
        if (arguments.IsDefaultOrEmpty || index < 0 || index >= arguments.Length)
        {
            return default;
        }

        var argument = arguments[index];
        if (argument.Kind == TypedConstantKind.Error || argument.IsNull)
        {
            return default;
        }

        if (argument.Kind == TypedConstantKind.Type && typeof(T).IsAssignableFrom(typeof(ITypeSymbol)))
        {
            return (T?)argument.Value;
        }

        return argument.Value is T value ? value : default;
    }

    public static bool IsEqualTo(this ISymbol? symbol, ISymbol? expected) =>
        symbol is not null && expected is not null && SymbolEqualityComparer.Default.Equals(expected, symbol);

    public static bool EndsWithOrdinal(this string? value, string suffix) =>
        value is not null && value.EndsWith(suffix, StringComparison.Ordinal);
}
