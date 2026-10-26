using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Nami.Tests;

/// <summary>The hex trace and span identity rules shared by the assertions and policies.</summary>
internal static class TelemetryId
{
    private static readonly SearchValues<char> s_hexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    /// <summary>Whether a parent ID is absent: null, empty or all zero.</summary>
    internal static bool IsAbsent([NotNullWhen(false)] string? id)
    {
        return !id.AsSpan().ContainsAnyExcept('0');
    }

    /// <summary>Whether <paramref name="id" /> has exactly <paramref name="length" /> hex digits and is not all zero.</summary>
    internal static bool IsValid(string id, int length)
    {
        return id.Length == length && !id.AsSpan().ContainsAnyExcept(s_hexDigits) && id.AsSpan().ContainsAnyExcept('0');
    }
}
