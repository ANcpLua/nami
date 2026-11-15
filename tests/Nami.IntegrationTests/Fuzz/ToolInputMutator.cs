using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nami.Api.Contracts;
using Nami.Tools;

namespace Nami.IntegrationTests.Fuzz;

/// <summary>One tools/call argument object as the MCP host forwards it: the JSON text an agent sent.</summary>
internal sealed record ToolCase(string Tool, string Json, string Note);

/// <summary>
///     Generates tool arguments from the tools' input contracts and then breaks them: values at and past every bound,
///     wrong JSON types, unknown and duplicate members, lone surrogates, deep nesting, hostile <c>since</c> and
///     cursor values. Text produced here is what <c>JSON.stringify</c> could send, plus duplicate keys.
/// </summary>
internal sealed class ToolInputMutator(Random random)
{
    internal static IReadOnlyList<string> Tools { get; } =
        ["sources_status", "errors_recent", "traces_get", "traces_slowest", "logs_search", "changes_recent"];

    private static readonly string[] s_since =
    [
        "-60m", "-1s", "-0s", "-0.5h", "-1.5d", "-99999999999999999999d", "-1e3m", "--5m", "-5", "-5M", "-5mm", "-.m",
        "-NaNs", "-Infinityd", "-m", "60m", "1970-01-01T00:00:00Z", "0001-01-01T00:00:00Z", "9999-12-31T23:59:59Z",
        "2026-11-15T25:00:00Z", "2026-02-29T00:00:00Z", "2024-02-29T00:00:00Z", "2016-12-31T23:59:60Z",
        "2026-11-15T12:00:00+14:00", "2026-11-15T12:00:00-23:59", "2026-11-15t12:00:00z",
        "2026-11-15T12:00:00.123456789012345678Z", "2026-11-15 12:00:00Z", "", " ", "\u0661\u0662m",
        "-\uD83D\uDE00m", "2026-11-15T12:00:00Z\n"
    ];

    private static readonly string[] s_strings =
    [
        "", " ", "checkout", "unknown", "\u0000", "a\u0301", "\uD83D\uDE00", "\u202E", new string('x', 256),
        new string('x', 257), string.Concat(Enumerable.Repeat("\uD83D\uDE00", 256)), string.Concat(Enumerable.Repeat("\uD83D\uDE00", 257)),
        new string('q', 4096), new string('q', 4097), "profiles", "baggage", "snapshot", "trace_id"
    ];

    internal ToolCase Next(IReadOnlyList<string> traceIds, IReadOnlyList<string> cursors, IReadOnlyList<string> snapshots)
    {
        var tool = Tools[random.Next(Tools.Count)];
        var input = new JsonObject();
        var notes = new List<string>();
        if (random.Next(2) == 0) input["since"] = s_since[random.Next(s_since.Length)];
        if (random.Next(4) == 0) input["service"] = random.Next(2) == 0 ? "checkout" : s_strings[random.Next(s_strings.Length)];
        if (random.Next(3) == 0) input["max_bytes"] = Integer(1024, 262144);
        if (random.Next(4) == 0) input["fields"] = Fields(tool);
        if (random.Next(5) == 0 && cursors.Count > 0) input["cursor"] = Cursor(cursors[random.Next(cursors.Count)]);
        if (random.Next(6) == 0) input["snapshot_id"] = snapshots.Count > 0 && random.Next(2) == 0
            ? snapshots[random.Next(snapshots.Count)]
            : s_strings[random.Next(s_strings.Length)];
        switch (tool)
        {
            case "traces_get" when random.Next(8) > 0:
                input["trace_id"] = traceIds.Count > 0 && random.Next(2) == 0
                    ? traceIds[random.Next(traceIds.Count)]
                    : random.Next(4) switch
                    {
                        0 => new string('0', 32),
                        1 => "0AF7651916CD43DD8448EB211C80319C",
                        2 => "0af7651916cd43dd8448eb211c80319",
                        _ => "\u0660af7651916cd43dd8448eb211c80319c"[..32]
                    };
                break;
            case "traces_slowest":
                if (random.Next(2) == 0) input["limit"] = Integer(1, 100);
                if (random.Next(3) == 0) input["operation"] = s_strings[random.Next(s_strings.Length)];
                break;
            case "logs_search" when random.Next(8) > 0:
                input["query"] = random.Next(2) == 0 ? "e" : s_strings[random.Next(s_strings.Length)];
                break;
        }

        var raw = new List<(string Placeholder, string Text)>();
        var steps = random.Next(3);
        for (var i = 0; i < steps; i++) notes.Add(Break(input, raw));
        var text = input.ToJsonString();
        foreach (var (placeholder, replacement) in raw)
            text = text.Replace(JsonValue.Create(placeholder)!.ToJsonString(), replacement, StringComparison.Ordinal);
        return new ToolCase(tool, text, string.Join("; ", notes));
    }

    private JsonValue Integer(int minimum, int maximum) => random.Next(10) switch
    {
        0 => JsonValue.Create(minimum - 1),
        1 => JsonValue.Create(maximum + 1),
        2 => JsonValue.Create(minimum),
        3 => JsonValue.Create(maximum),
        4 => JsonValue.Create((double)maximum + 0.5),
        5 => JsonValue.Create(minimum.ToString(CultureInfo.InvariantCulture))!,
        6 => JsonValue.Create(-0.0),
        7 => JsonValue.Create(long.MaxValue),
        _ => JsonValue.Create(minimum + random.Next(maximum - minimum + 1))
    };

    private JsonArray Fields(string tool)
    {
        var allowed = tool switch
        {
            "sources_status" => ContractFields.For<Nami.Api.Contracts.Tools.ProjectedSourceStatus>(),
            "errors_recent" => ContractFields.For<Nami.Api.Contracts.Tools.ProjectedErrorGroup>(),
            "traces_slowest" => ContractFields.For<Nami.Api.Contracts.Tools.ProjectedSlowTrace>(),
            "logs_search" => ContractFields.For<Nami.Api.Contracts.Tools.ProjectedLogHit>(),
            "changes_recent" => ContractFields.For<Nami.Api.Contracts.Tools.ProjectedChange>(),
            _ => ContractFields.For<Nami.Api.Contracts.Tools.ProjectedTrace>()
        };
        var fields = new JsonArray();
        var count = random.Next(5) switch { 0 => 0, 1 => 128, 2 => 129, _ => 1 + random.Next(4) };
        for (var i = 0; i < count; i++)
        {
            fields.Add(random.Next(6) switch
            {
                0 => JsonValue.Create(s_strings[random.Next(s_strings.Length)]),
                1 => JsonValue.Create(7),
                _ => JsonValue.Create(allowed[random.Next(allowed.Count)])
            });
        }

        return fields;
    }

    private string Cursor(string cursor)
    {
        switch (random.Next(6))
        {
            case 0:
                return cursor;
            case 1:
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(Pad(cursor.Replace('-', '+').Replace('_', '/'))));
                var parts = decoded.Split('|');
                parts[2] = random.Next(3) switch { 0 => "-1", 1 => "99999999999", _ => "2147483647" };
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join('|', parts))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            case 2:
                return cursor + "=";
            case 3:
                return Convert.ToBase64String(Encoding.UTF8.GetBytes("a|b|c|d|e"));
            case 4:
                return "%%%";
            default:
                return cursor[..(cursor.Length / 2)];
        }
    }

    private static string Pad(string value) => value.PadRight((value.Length + 3) / 4 * 4, '=');

    private string Break(JsonObject input, List<(string, string)> raw)
    {
        var placeholder = $"\u0001R{raw.Count}\u0001";
        var name = input.Count > 0 && random.Next(3) > 0
            ? input.ElementAt(random.Next(input.Count)).Key
            : random.Next(2) == 0 ? "unknown_member" : "since";
        switch (random.Next(8))
        {
            case 0:
                input[name] = placeholder;
                raw.Add((placeholder, random.Next(4) switch
                {
                    0 => "\"\\ud800\"",
                    1 => "\"a\\udfff\\ud800\"",
                    2 => "\"\\ud83d\\ude00\"",
                    _ => "\"\\u0000\""
                }));
                return name + " = lone surrogate escape";
            case 1:
                input[placeholder] = 1;
                raw.Add((placeholder, random.Next(2) == 0 ? "\"\\ud800\"" : "\"\""));
                return "member named by a lone surrogate or empty";
            case 2:
                var depth = random.Next(3) switch { 0 => 63, 1 => 64, _ => 200 };
                input[name] = placeholder;
                raw.Add((placeholder, new string('[', depth) + new string(']', depth)));
                return $"{name} nested {depth} deep";
            case 3:
                input[name] = placeholder;
                raw.Add((placeholder, random.Next(6) switch
                {
                    0 => "1e400",
                    1 => "-1e400",
                    2 => "1e-400",
                    3 => "1E+5",
                    4 => "10e-1",
                    _ => "100000000000000000000000000000"
                }));
                return name + " = number literal";
            case 4 when input.Count > 0:
                var key = input.ElementAt(random.Next(input.Count)).Key;
                input[placeholder] = random.Next(2) == 0 ? input[key]?.DeepClone() : JsonValue.Create(5);
                raw.Add((placeholder, JsonValue.Create(key)!.ToJsonString()));
                return "duplicate member " + key;
            case 5:
                input[name] = random.Next(5) switch
                {
                    0 => new JsonObject(),
                    1 => new JsonArray(),
                    2 => JsonValue.Create(true),
                    3 => null,
                    _ => JsonValue.Create(3.25)
                };
                return name + " of another JSON type";
            case 6:
                input[s_strings[random.Next(s_strings.Length)]] = "x";
                return "unknown member";
            default:
                input[name] = s_strings[random.Next(s_strings.Length)];
                return name + " = string";
        }
    }
}

/// <summary>What a tool must answer for any argument object: a bounded result or a bounded refusal, never a throw.</summary>
internal static class ToolOracle
{
    private static readonly HashSet<string> s_codes = new(StringComparer.Ordinal)
    {
        "invalid_argument", "not_found", "data_unavailable", "unauthorized"
    };

    internal static async Task<List<FuzzFinding>> RunAsync(NamiToolService service, ToolCase input, CancellationToken ct)
    {
        var findings = new List<FuzzFinding>();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(input.Json, new JsonDocumentOptions { MaxDepth = 1024 });
        }
        catch (JsonException)
        {
            return findings;
        }

        using (document)
        {
            ToolRunResult result;
            try
            {
                result = await service.InvokeAsync(input.Tool, document.RootElement, "default", ct);
            }
            catch (Exception error) when (!ct.IsCancellationRequested)
            {
                findings.Add(OtlpOracle.Finding("tool input " + input.Tool, error,
                    "the tool threw on its arguments; the engine answers internal_error"));
                return findings;
            }

            var text = result.Content.GetRawText();
            if (result.IsError)
            {
                var code = result.Content.TryGetProperty("code", out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
                if (code is null || !s_codes.Contains(code))
                    findings.Add(new FuzzFinding("tool input " + input.Tool, "refusal-code:" + code, text));
                if (Encoding.UTF8.GetByteCount(text) > 1024)
                    findings.Add(new FuzzFinding("tool input " + input.Tool, "refusal-size", $"{Encoding.UTF8.GetByteCount(text)} bytes"));
                if (ContractJson.Validate("Common.ToolError", result.Content) is { } issue)
                    findings.Add(new FuzzFinding("tool input " + input.Tool, "refusal-contract:" + issue.Field, issue.Message));
                return findings;
            }

            var budget = document.RootElement.ValueKind == JsonValueKind.Object &&
                         document.RootElement.TryGetProperty("max_bytes", out var bytes) &&
                         bytes.ValueKind == JsonValueKind.Number && bytes.TryGetDouble(out var parsed) && parsed is >= 1024 and <= 262144
                ? (int)parsed
                : 32768;
            if (Encoding.UTF8.GetByteCount(text) > budget)
                findings.Add(new FuzzFinding("tool input " + input.Tool, "result-size", $"{Encoding.UTF8.GetByteCount(text)} > {budget}"));
            return findings;
        }
    }
}
