using System.Collections;
using System.Text.Json;
using Nami.SemanticConventions;

namespace Nami.Tests.Policies;

/// <summary>Validates captured attribute types against Nami's generated registry, including enum wire types.</summary>
public sealed class ConformsToSemanticConventionsAttribute : TelemetryPolicyAttribute
{
    private static readonly Dictionary<string, JsonValueKind[]> s_enumKinds = LoadEnumKinds();

    private protected override IEnumerable<string> Violations(TelemetryInvocation invocation)
    {
        foreach (var span in invocation.Traces.SelectMany(trace => trace.Evidence.Spans))
        foreach (var (key, value) in span.Resource.Attributes
                     .Concat(span.Scope?.Attributes ?? new Dictionary<string, object?>())
                     .Concat(span.Attributes))
        {
            var definition = AttributeCatalog.Find(key);
            if (definition is null) continue;
            if (definition.Stability == Stability.Deprecated)
                yield return $"{Describe(span)} captured deprecated attribute '{key}'.";
            if (!Matches(definition, value))
                yield return $"{Describe(span)} attribute '{key}' does not match registry type '{definition.Type}'.";
        }
    }

    private static bool Matches(AttributeDefinition definition, object? value)
    {
        var type = definition.Type;
        if (type.StartsWith("template[", StringComparison.Ordinal)) type = type[9..^1];
        if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            return value is IEnumerable items and not string
                                              && items.Cast<object?>().All(item =>
                                                  Matches(definition with { Type = type[..^2] }, item));
        }

        return type switch
        {
            "string" => value is string,
            "int" => value is long,
            "double" => value is double,
            "boolean" => value is bool,
            "any" => true,
            "enum" => s_enumKinds.TryGetValue(definition.Key, out var kinds)
                      && ((value is string && kinds.Contains(JsonValueKind.String))
                          || (value is long && kinds.Contains(JsonValueKind.Number))),
            _ => false
        };
    }

    private static Dictionary<string, JsonValueKind[]> LoadEnumKinds()
    {
        using var stream =
            typeof(ConformsToSemanticConventionsAttribute).Assembly.GetManifestResourceStream(
                "Nami.Tests.Registry.json")
            ?? throw new InvalidOperationException("Nami registry metadata was not embedded.");
        using var json = JsonDocument.Parse(stream);
        return json.RootElement.GetProperty("attribute_catalog").EnumerateArray()
            .Where(attribute => attribute.GetProperty("type").ValueKind == JsonValueKind.Object)
            .GroupBy(attribute => attribute.GetProperty("key").GetString()!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.SelectMany(attribute => attribute.GetProperty("type")
                    .GetProperty("members").EnumerateArray().Select(member => member.GetProperty("value").ValueKind))
                .Distinct().ToArray(), StringComparer.Ordinal);
    }
}
