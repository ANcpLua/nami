using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Nami.Api.Contracts;

/// <summary>ECMAScript patterns emitted from TypeSpec and shared with the MCP's JSON Schema validator.</summary>
public static class ContractPattern
{
    private static readonly ConcurrentDictionary<string, Regex> s_compiled = new(StringComparer.Ordinal);

    public static bool IsMatch(string pattern, string value) =>
        s_compiled.GetOrAdd(pattern, static source => new Regex(source, RegexOptions.ECMAScript | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1))).IsMatch(value);
}
