using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Nami.Api.Contracts;

/// <summary>A failed assertion in the TypeSpec-generated wire schema.</summary>
public sealed record ContractIssue(
    string Field,
    string Keyword,
    string Message,
    IReadOnlyList<string>? Allowed = null,
    string? Hint = null);

/// <summary>
///     Validates both sides of serialization against the same normalized JSON Schemas as the MCP.
///     The emitter rejects unsupported schema assertions. No reflection-based serialization is used.
/// </summary>
public static class ContractJson
{
    private static readonly JsonDocument s_schemas = LoadSchemas();

    private static JsonDocument LoadSchemas()
    {
        using var stream = typeof(ContractJson).Assembly.GetManifestResourceStream("nami.contract-schemas.json")
                           ?? throw new InvalidOperationException("The generated contract schemas are missing.");
        return JsonDocument.Parse(stream);
    }

    public static string FormatTimestamp(DateTimeOffset value)
    {
        return JsonSerializer.SerializeToElement(value, ContractTimestampJsonContext.Default.DateTimeOffset)
            .GetString()!;
    }

    /// <summary>Reads an exact JSON integer after schema validation, including integral decimal and exponent forms.</summary>
    public static int ReadInteger(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !IsExactInteger(value))
            throw new JsonException("Expected an exact JSON integer.");
        return (int)value.GetDecimal();
    }

    public static JsonElement Schema(string name)
    {
        return s_schemas.RootElement.GetProperty(name);
    }

    public static ContractIssue? Validate(string name, JsonElement value)
    {
        var schema = s_schemas.RootElement.GetProperty(name);
        return Check(schema, value, schema, "");
    }

    public static void Require(string name, JsonElement value)
    {
        if (Validate(name, value) is { } issue)
            throw new JsonException($"{issue.Field}: {issue.Message}", issue.Field, null, null);
    }

    public static T? Deserialize<T>(JsonElement value)
    {
        var name = ContractTypes.NameOf(typeof(T));
        Require(name, value);
        return (T?)value.Deserialize(ContractTypes.TypeInfo(name));
    }

    public static T? Deserialize<T>(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Deserialize<T>(document.RootElement);
    }

    public static JsonElement Serialize<T>(T value)
    {
        var name = ContractTypes.NameOf(typeof(T));
        var json = JsonSerializer.SerializeToElement(value, ContractTypes.TypeInfo(name));
        Require(name, json);
        return json;
    }

    /// <summary>Serializes an internal document; null optional members mean unset, as on generated C# models.</summary>
    public static JsonElement Serialize(string name, JsonElement value)
    {
        var schema = s_schemas.RootElement.GetProperty(name);
        if (Check(schema, value, schema, "", true) is { } issue)
            throw new JsonException($"{issue.Field}: {issue.Message}", issue.Field, null, null);
        var type = ContractTypes.TypeInfo(name);
        var typed = value.Deserialize(type);
        var result = JsonSerializer.SerializeToElement(typed, type);
        Require(name, result);
        return result;
    }

    public static JsonElement ToElement(JsonNode? value)
    {
        using var document = JsonDocument.Parse(value?.ToJsonString() ?? "null");
        return document.RootElement.Clone();
    }

    private static ContractIssue? Check(JsonElement schema, JsonElement value, JsonElement root, string path,
        bool allowUnset = false)
    {
        if (schema.ValueKind == JsonValueKind.True) return null;
        if (schema.ValueKind == JsonValueKind.False) return new ContractIssue(path, "false", "is not allowed");
        if (schema.TryGetProperty("$ref", out var reference))
        {
            var name = reference.GetString()!["#/$defs/".Length..];
            if (Check(root.GetProperty("$defs").GetProperty(name), value, root, path, allowUnset) is
                { } referenced) return referenced;
        }

        if (schema.TryGetProperty("enum", out var choices) &&
            !choices.EnumerateArray().Any(choice => JsonElement.DeepEquals(choice, value)))
            return new ContractIssue(path, "enum",
                "must be one of: " + string.Join(", ", choices.EnumerateArray().Select(Text)),
                choices.EnumerateArray().Select(Text).ToArray());
        if (schema.TryGetProperty("const", out var constant) && !JsonElement.DeepEquals(constant, value))
            return new ContractIssue(path, "const", "must be " + Text(constant), [Text(constant)]);
        if (schema.TryGetProperty("type", out var type) && !Takes(type, value))
            return new ContractIssue(path, "type", $"must be {string.Join(" or ", Types(type))}");
        if (schema.TryGetProperty("allOf", out var all))
        {
            foreach (var branch in all.EnumerateArray())
                if (Check(branch, value, root, path, allowUnset) is { } issue)
                    return issue;
        }

        foreach (var keyword in new[] { "anyOf", "oneOf" })
        {
            if (!schema.TryGetProperty(keyword, out var branches)) continue;
            var outcomes = branches.EnumerateArray()
                .Select(branch => (Schema: branch, Issue: Check(branch, value, root, path, allowUnset))).ToArray();
            var matches = outcomes.Count(outcome => outcome.Issue is null);
            if (matches == 0)
            {
                // Prefer the branch whose discriminator matches; otherwise name an unknown
                // property before a missing property, like the MCP's argument diagnostics.
                var objectForms = outcomes.Where(outcome => outcome.Schema.TryGetProperty("properties", out _))
                    .ToArray();
                if (value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var outcome in objectForms)
                        if (HasMatchingTag(outcome.Schema, value))
                            return outcome.Issue;
                    var discriminatorFields = objectForms.Length > 1
                        ? objectForms[0].Schema.GetProperty("properties").EnumerateObject()
                            .Where(property => objectForms.All(form =>
                                form.Schema.GetProperty("properties").TryGetProperty(property.Name, out var tag) &&
                                tag.TryGetProperty("enum", out var tags) && tags.GetArrayLength() == 1)).ToArray()
                        : [];
                    if (objectForms.Length == 1 || discriminatorFields.Length > 0)
                    {
                        var accepted = objectForms
                            .SelectMany(form =>
                                form.Schema.GetProperty("properties").EnumerateObject()
                                    .Select(property => property.Name)).Distinct(StringComparer.Ordinal).ToArray();
                        foreach (var member in value.EnumerateObject())
                        {
                            if (!accepted.Contains(member.Name, StringComparer.Ordinal))
                            {
                                var field = Child(path, member.Name);
                                return new ContractIssue(field, "additionalProperties",
                                    "is not accepted here; accepted: " + string.Join(", ", accepted), accepted,
                                    $"Replace {field} with one of the accepted arguments or drop it.");
                            }
                        }
                    }

                    if (discriminatorFields.Length > 0)
                    {
                        foreach (var property in discriminatorFields)
                        {
                            if (!objectForms.All(form =>
                                    form.Schema.GetProperty("properties").TryGetProperty(property.Name, out var tag) &&
                                    tag.TryGetProperty("enum", out var tags) && tags.GetArrayLength() == 1)) continue;
                            var field = Child(path, property.Name);
                            if (!value.TryGetProperty(property.Name, out _))
                                return new ContractIssue(field, "required", "is required");
                            var tags = objectForms.Select(form =>
                                Text(form.Schema.GetProperty("properties").GetProperty(property.Name)
                                    .GetProperty("enum")[0])).ToArray();
                            return new ContractIssue(field, "enum", "must be one of: " + string.Join(", ", tags), tags,
                                "Send one of the allowed values.");
                        }
                    }

                    var closest = objectForms.OrderByDescending(outcome => SharedMembers(outcome.Schema, value))
                        .FirstOrDefault();
                    if (closest.Issue is not null && SharedMembers(closest.Schema, value) > 0) return closest.Issue;
                }

                var labels = branches.EnumerateArray().SelectMany(branch => Labels(branch, root))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(label => label.StartsWith('{') ? 1 : 0).ToArray();
                return new ContractIssue(path, "enum", "must be one of: " + string.Join(", ", labels), labels,
                    "Send one of the allowed values.");
            }

            if (keyword == "oneOf" && matches != 1)
                return new ContractIssue(path, keyword, "must match exactly one form");
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            schema.TryGetProperty("properties", out var properties);
            schema.TryGetProperty("additionalProperties", out var additional);
            foreach (var member in value.EnumerateObject())
            {
                if (properties.ValueKind == JsonValueKind.Object &&
                    properties.TryGetProperty(member.Name, out _)) continue;
                var field = Child(path, member.Name);
                if (additional.ValueKind == JsonValueKind.False)
                {
                    var names = properties.EnumerateObject().Select(property => property.Name).ToArray();
                    return new ContractIssue(field, "additionalProperties",
                        "is not accepted here; accepted: " + string.Join(", ", names), names,
                        $"Replace {field} with one of the accepted arguments or drop it.");
                }

                if (additional.ValueKind == JsonValueKind.Object &&
                    Check(additional, member.Value, root, field, allowUnset) is { } invalid) return invalid;
            }

            var required = schema.TryGetProperty("required", out var namesRequired)
                ? namesRequired.EnumerateArray().Select(name => name.GetString()!).ToHashSet(StringComparer.Ordinal)
                : [];
            if (properties.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in properties.EnumerateObject())
                {
                    var field = Child(path, property.Name);
                    if (value.TryGetProperty(property.Name, out var member))
                    {
                        if (allowUnset && member.ValueKind == JsonValueKind.Null && !required.Contains(property.Name))
                            continue;
                        if (Check(property.Value, member, root, field, allowUnset) is { } issue) return issue;
                    }
                    else if (required.Contains(property.Name))
                        return new ContractIssue(field, "required", "is required");
                }
            }
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            if (Length(schema, value.GetArrayLength(), "minItems", "maxItems", "items", path) is
                { } issue) return issue;
            if (schema.TryGetProperty("items", out var items))
            {
                var index = 0;
                foreach (var item in value.EnumerateArray())
                    if (Check(items, item, root, $"{path}[{index++}]", allowUnset) is { } invalid)
                        return invalid;
            }
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!;
            if (Length(schema, text.EnumerateRunes().Count(), "minLength", "maxLength", "characters", path) is
                { } issue) return issue;
            if (schema.TryGetProperty("pattern", out var pattern) &&
                !ContractPattern.IsMatch(pattern.GetString()!, text))
            {
                return pattern.GetString() == Schema("Common.Timestamp").GetProperty("pattern").GetString()
                    ? new ContractIssue(path, "format", "must match format \"date-time\"")
                    : new ContractIssue(path, "pattern", $"must match pattern \"{pattern.GetString()}\"");
            }
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            var number = value.GetDouble();
            foreach (var keyword in new[] { "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum" })
            {
                if (!schema.TryGetProperty(keyword, out var bound)) continue;
                var limit = bound.GetDouble();
                if (keyword switch
                    {
                        "minimum" => number < limit, "maximum" => number > limit,
                        "exclusiveMinimum" => number <= limit, _ => number >= limit
                    })
                {
                    var low = schema.TryGetProperty("minimum", out var minimum) ? minimum.GetRawText() : null;
                    var high = schema.TryGetProperty("maximum", out var maximum) ? maximum.GetRawText() : null;
                    var range = low is not null && high is not null ? $"from {low} to {high}" :
                        low is not null ? $"of at least {low}" : $"of at most {high}";
                    var hint = path.Contains('.') ? $"Correct {path}." :
                        schema.TryGetProperty("default", out var fallback) ? $"Omit {path} for {fallback}." :
                        $"Omit {path} for the tool's default.";
                    return new ContractIssue(path, keyword, $"Use {path} {range}.", Hint: hint);
                }
            }
        }

        return null;
    }

    private static ContractIssue? Length(JsonElement schema, int count, string min, string max, string unit,
        string path)
    {
        int? low = schema.TryGetProperty(min, out var minimum) ? minimum.GetInt32() : null;
        int? high = schema.TryGetProperty(max, out var maximum) ? maximum.GetInt32() : null;
        if (count >= (low ?? 0) && count <= (high ?? int.MaxValue)) return null;
        var singular = unit == "items" ? "item" : "character";

        string Count(int number)
        {
            return $"{number} {(number == 1 ? singular : unit)}";
        }

        var range = (low, high) switch
        {
            ({ } first, { } last) when first == last => $"exactly {Count(last)}",
            ({ } first, { } last) => $"{first} to {Count(last)}",
            ({ } first, null) => $"at least {Count(first)}",
            _ => $"at most {Count(high!.Value)}"
        };
        return new ContractIssue(path, count < low ? min : max,
            $"Use {path} {(unit == "items" ? "with" : "of")} {range}.");
    }

    private static IEnumerable<string> Labels(JsonElement schema, JsonElement root)
    {
        if (schema.TryGetProperty("$ref", out var reference))
            return Labels(root.GetProperty("$defs").GetProperty(reference.GetString()!["#/$defs/".Length..]), root);
        if (schema.TryGetProperty("enum", out var values)) return values.EnumerateArray().Select(Text);
        if (schema.TryGetProperty("properties", out var properties))
            return ["{" + string.Join(", ", properties.EnumerateObject().Select(property => property.Name)) + "}"];
        if (schema.TryGetProperty("type", out var type)) return Types(type);
        if (schema.TryGetProperty("anyOf", out var branches))
            return branches.EnumerateArray().SelectMany(branch => Labels(branch, root));
        return [];
    }

    private static IEnumerable<string> Types(JsonElement type)
    {
        return type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Select(item => item.GetString()!)
            : [type.GetString()!];
    }

    private static bool Takes(JsonElement type, JsonElement value)
    {
        return Types(type).Any(name => name switch
        {
            "null" => value.ValueKind == JsonValueKind.Null,
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "number" => value.ValueKind == JsonValueKind.Number && double.IsFinite(value.GetDouble()),
            "integer" => value.ValueKind == JsonValueKind.Number && IsExactInteger(value),
            _ => throw new InvalidOperationException($"Unsupported contract type {name}.")
        });
    }

    // JSON Schema tests the mathematical number, not its rounded binary64 projection.
    // Keep exponent notation and trailing decimal zeros valid without letting tiny fractions
    // underflow to zero or fractions near 2^53 round to an integer.
    internal static bool IsExactInteger(JsonElement value)
    {
        var text = value.GetRawText().AsSpan();
        var exponentIndex = text.IndexOfAny('e', 'E');
        var mantissa = exponentIndex < 0 ? text : text[..exponentIndex];
        var decimalIndex = mantissa.IndexOf('.');
        var fractionalDigits = decimalIndex < 0 ? 0 : mantissa.Length - decimalIndex - 1;
        var trailingZeros = 0;
        var nonzero = false;
        for (var i = mantissa.Length - 1; i >= 0; i--)
        {
            var digit = mantissa[i];
            if (digit is '-' or '.') continue;
            if (!nonzero && digit == '0') trailingZeros++;
            else if (digit != '0') nonzero = true;
        }

        if (!nonzero) return true;
        var exponent = 0L;
        if (exponentIndex >= 0)
        {
            var digits = text[(exponentIndex + 1)..];
            var negative = digits[0] == '-';
            if (digits[0] is '+' or '-') digits = digits[1..];
            // Only the comparison with a token-length-sized fraction matters.
            // Saturation avoids overflow on an otherwise valid, very large exponent.
            foreach (var digit in digits)
                exponent = Math.Min(int.MaxValue, exponent * 10 + digit - '0');
            if (negative) exponent = -exponent;
        }

        return exponent >= (long)fractionalDigits - trailingZeros;
    }

    private static bool HasMatchingTag(JsonElement schema, JsonElement value)
    {
        return schema.TryGetProperty("properties", out var properties) &&
               properties.EnumerateObject().Any(property =>
                   property.Value.TryGetProperty("enum", out var values) && values.GetArrayLength() == 1 &&
                   value.TryGetProperty(property.Name, out var member) && JsonElement.DeepEquals(values[0], member));
    }

    private static int SharedMembers(JsonElement schema, JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out var properties)
            ? value.EnumerateObject().Count(property => properties.TryGetProperty(property.Name, out _))
            : 0;
    }

    private static string Text(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
    }

    private static string Child(string path, string name)
    {
        return path.Length == 0 ? name : path + "." + name;
    }
}

[JsonSerializable(typeof(DateTimeOffset))]
internal sealed partial class ContractTimestampJsonContext : JsonSerializerContext;
