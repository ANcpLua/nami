using System.Buffers.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Nami.Collector.Storage;

namespace Nami.Collector.Analysis;

internal sealed class AnalysisInputException(string field, string message, string code = "analysis.invalid") : Exception(message)
{
    public string Field { get; } = field;
    public string Code { get; } = code;
    public string Description { get; } = message;
    /// <summary>The values the field accepts, when they form a closed set.</summary>
    public IReadOnlyList<string>? Allowed { get; init; }
    /// <summary>What to send instead.</summary>
    public string? Hint { get; init; }
    /// <summary>The rejected value, when it is safe to repeat.</summary>
    public string? RejectedValue { get; init; }
    /// <summary>For a <c>*.not_found</c> code, the identifier that was not found.</summary>
    public string? Missing { get; init; }
}

internal readonly record struct AnalysisWindow(DateTimeOffset Start, DateTimeOffset End)
{
    // No telemetry precedes the Unix epoch, so a baseline that reaches before it holds what follows it.
    public ulong StartNs => Start <= DateTimeOffset.UnixEpoch ? 0 : NamiTimeConversions.ToUnixNanoUnsigned(Start);
    public ulong EndNs => End <= DateTimeOffset.UnixEpoch ? 0 : NamiTimeConversions.ToUnixNanoUnsigned(End);
    public JsonObject Json => J.O(("start_time", Start.ToString("O")), ("end_time", End.ToString("O")));
    public bool Contains(ulong time) => time >= StartNs && time < EndNs;
    public static AnalysisWindow Resolve(JsonObject input, DateTimeOffset now, double defaultMinutes = 60)
    {
        var end = J.Date(input, "end_time") ?? now;
        var start = J.Date(input, "start_time") ?? end.AddMinutes(-J.Number(input, "lookback_minutes", defaultMinutes));
        if (start < DateTimeOffset.UnixEpoch) throw new AnalysisInputException("start_time", "start_time must be on or after the Unix epoch.");
        if (end <= start) throw new AnalysisInputException("end_time", "end_time must be after start_time.");
        return new(start, end);
    }
}

internal static partial class J
{
    public static JsonNode? N(object? value) => value switch
    {
        null => null, JsonNode node => node.DeepClone(), string v => JsonValue.Create(v), bool v => JsonValue.Create(v),
        byte v => JsonValue.Create(v), short v => JsonValue.Create(v), int v => JsonValue.Create(v), long v => JsonValue.Create(v), ulong v => JsonValue.Create(v),
        double v when double.IsFinite(v) => JsonValue.Create(v), double => null, decimal v => JsonValue.Create(v),
        IEnumerable<JsonNode?> values => new JsonArray(values.Select(static v => v?.DeepClone()).ToArray()),
        IEnumerable<string> values => new JsonArray(values.Select(static v => (JsonNode?)JsonValue.Create(v)).ToArray()),
        _ => throw new InvalidOperationException("Unsupported analysis JSON value.")
    };
    public static JsonObject O(params (string Key, object? Value)[] items)
    { var obj = new JsonObject(); foreach (var item in items) obj[item.Key] = N(item.Value); return obj; }
    public static JsonArray A(IEnumerable<JsonNode?> items) => new(items.Select(static v => v?.DeepClone()).ToArray());
    public static string? String(JsonNode? input, string name) =>
        input is JsonObject obj && obj[name] is { } value && value.GetValueKind() is JsonValueKind.String
            ? value.GetValue<string>() : null;
    public static double Number(JsonNode? input, string name, double fallback = 0) =>
        input is JsonObject obj && obj[name] is { } value && value.GetValueKind() is JsonValueKind.Number
            ? double.Parse(value.ToJsonString(), CultureInfo.InvariantCulture) : fallback;
    public static bool Bool(JsonNode? input, string name, bool fallback = false) =>
        input is JsonObject obj && obj[name] is { } value && value.GetValueKind() is JsonValueKind.True or JsonValueKind.False
            ? value.GetValue<bool>() : fallback;
    public static string Text(JsonNode? value) => value is null ? "" : value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString();
    public static DateTimeOffset? Date(JsonNode? input, string name)
    {
        if ((input as JsonObject)?[name] is null) return null;
        if (ContractDateTime.Read(String(input, name) ?? "") is not { } time)
            throw new AnalysisInputException(name, name + " must be an RFC 3339 timestamp.") { Hint = "Send a time such as 2026-10-19T12:00:00Z." };
        // An instant holds 100 ns. A finer boundary cannot be applied exactly, and moving it would
        // include or exclude records the request did not select.
        if (ContractDateTime.FinerThanTicks(time.Fraction))
            throw new AnalysisInputException(name, name + " is finer than 100 nanoseconds; use at most seven fractional digits.", "time.precision_unsupported");
        if (time.Instant is not { } value || !NamiTimeConversions.TryToUnixNanoUnsigned(value, out _))
            throw new AnalysisInputException(name, name + " must be within the unsigned OTLP Unix-nanosecond range.");
        return value;
    }
    public static JsonObject Attributes(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new();
        var parsed = JsonNode.Parse(json);
        if (parsed is JsonObject obj) { var unwrapped = new JsonObject(); foreach (var item in obj) unwrapped[item.Key] = Unwrap(item.Value); return unwrapped; }
        var result = new JsonObject();
        if (parsed is JsonArray list) foreach (var pair in list)
            if (String(pair, "key") is { } key) result[key] = Unwrap(pair?["value"]);
        return result;
    }
    private static JsonNode? Unwrap(JsonNode? value)
    {
        if (value is not JsonObject obj) return value?.DeepClone();
        if (String(obj, "type") == "int" && long.TryParse(Text(obj["value"]), CultureInfo.InvariantCulture, out var integer)) return JsonValue.Create(integer);
        if (String(obj, "type") == "double" && double.TryParse(Text(obj["value"]), CultureInfo.InvariantCulture, out var number)) return JsonValue.Create(number);
        foreach (var name in new[] { "stringValue", "string_value", "intValue", "int_value", "doubleValue", "double_value", "boolValue", "bool_value", "value" })
            if (obj[name] is { } scalar) return scalar.DeepClone();
        return obj.DeepClone();
    }
    public static string Normalize(string message) => Numbers().Replace(Quoted().Replace(Hex().Replace(Uuid().Replace(message, "#"), "#"), "#"), "#");
    [GeneratedRegex(@"\b\d+(?:\.\d+)?\b")] private static partial Regex Numbers();
    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F-]{27,}")] private static partial Regex Uuid();
    [GeneratedRegex(@"\b(?:0x)?[0-9a-fA-F]{8,}\b")] private static partial Regex Hex();
    [GeneratedRegex("\"[^\"]*\"|'[^']*'")] private static partial Regex Quoted();
    /// <summary>A recorded Unix-nanosecond time as RFC 3339, to the 100 ns a <see cref="DateTimeOffset"/> holds.</summary>
    public static string Time(ulong nanos) => Instant(nanos).ToString("O");
    /// <summary>The instant of a Unix-nanosecond time, to the 100 ns a <see cref="DateTimeOffset"/> holds.</summary>
    public static DateTimeOffset Instant(ulong nanos) => DateTimeOffset.UnixEpoch.AddTicks((long)(nanos / 100));
    public static double Percentile(IEnumerable<double> source, double quantile)
    {
        var values = source.Order().ToArray(); if (values.Length is 0) return double.NaN;
        var p = (values.Length - 1) * quantile; var i = (int)p;
        return values[i] + (values[Math.Min(i + 1, values.Length - 1)] - values[i]) * (p - i);
    }
}

internal sealed record AnalysisRecord(string Id, string? TraceId, string? SpanId, string? SessionId,
    string Service, string Name, ulong Start, ulong End, bool Error, double Duration,
    JsonObject Attributes, JsonObject Resource, SpanStorageRow? Span = null, LogStorageRow? Log = null)
{
    public JsonNode? Value(string key, string? scope = null) => key switch
    {
        CollectorSemanticAttributeCatalog.ServiceName => JsonValue.Create(Service), "span.name" => JsonValue.Create(Name),
        "status" => JsonValue.Create(Error ? "ERROR" : "OK"),
        "severity" => Log is null ? null : JsonValue.Create(Log.SeverityNumber),
        _ => scope == "resource" ? Resource[key] : Attributes[key] ?? (scope is null ? Resource[key] : null)
    };
    public static AnalysisRecord From(SpanStorageRow s) => new(s.SpanId, s.TraceId, s.SpanId, s.SessionId,
        s.ServiceName ?? "unknown_service", s.Name, s.StartTimeUnixNano, s.EndTimeUnixNano, s.StatusCode == 2,
        s.DurationNs / 1_000_000d, SpanAttributes(s), J.Attributes(s.ResourceJson), Span: s);
    private static JsonObject SpanAttributes(SpanStorageRow span)
    {
        var attributes = J.Attributes(span.AttributesJson);
        if (JsonNode.Parse(span.EventsJson ?? "[]") is JsonArray events)
            foreach (var entry in events.OfType<JsonObject>().Where(static e => J.String(e, "name") == "exception"))
                foreach (var pair in J.Attributes(entry["attributes"]?.ToJsonString()))
                    if (pair.Key is CollectorSemanticAttributeCatalog.RecordedFailureKind)
                        attributes[pair.Key] ??= pair.Value?.DeepClone();
        return attributes;
    }
    public static AnalysisRecord From(LogStorageRow l) => new(l.LogId, l.TraceId, l.SpanId, l.SessionId,
        l.ServiceName ?? "unknown_service", l.EventName ?? "", l.EffectiveTimeUnixNano() ?? 0, l.EffectiveTimeUnixNano() ?? 0, l.SeverityNumber >= 17,
        0, J.Attributes(l.AttributesJson), J.Attributes(l.ResourceJson), Log: l);
}

internal static class AnalysisMatchers
{
    // The request's contract holds the matchers, their count, keys, ops and scopes; the operands an op needs are checked here.
    public static void Validate(JsonNode? node)
    {
        if (node is not JsonArray list) return;
        foreach (var m in list)
        {
            var op = J.String(m, "op");
            if (op == "in" && m?["values"] is not JsonArray) throw new AnalysisInputException("matchers.values", "The in operator requires values.");
            if (op != "exists" && op != "in" && m?["value"] is null) throw new AnalysisInputException("matchers.value", "This operator requires a value.");
        }
    }
    public static bool Match(AnalysisRecord record, JsonNode? node)
    {
        if (node is not JsonArray list) return true;
        foreach (var m in list)
        {
            var actual = record.Value(J.String(m, "key")!, J.String(m, "scope") ?? (record.Log is null ? "span" : "log"));
            var expected = m?["value"]; var a = J.Text(actual); var b = J.Text(expected);
            var numeric = double.TryParse(a, CultureInfo.InvariantCulture, out var an) && double.TryParse(b, CultureInfo.InvariantCulture, out var bn) ? an.CompareTo(bn) : (int?)null;
            var ok = J.String(m, "op") switch
            {
                "exists" => actual is not null, "eq" => actual is not null && Equal(actual, expected),
                "neq" => actual is null || !Equal(actual, expected), "in" => actual is { } present && ((JsonArray)m!["values"]!).Any(v => Equal(present, v)),
                "prefix" => actual is not null && a.StartsWith(b, StringComparison.Ordinal),
                "contains" => actual is not null && a.Contains(b, StringComparison.Ordinal),
                "gt" => numeric > 0, "gte" => numeric >= 0, "lt" => numeric < 0, "lte" => numeric <= 0, _ => false
            };
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>
    /// Equality keeps the operand's type: a number equals a number of the same value, a string the same
    /// string and a boolean the same boolean. A number never equals a string, so 1 does not match "1".
    /// </summary>
    public static bool Equal(JsonNode actual, JsonNode? expected) => (actual.GetValueKind(), expected?.GetValueKind()) switch
    {
        (JsonValueKind.Number, JsonValueKind.Number) => SameNumber(actual.ToJsonString(), expected.ToJsonString()),
        (JsonValueKind.String, JsonValueKind.String) => string.Equals(actual.GetValue<string>(), expected.GetValue<string>(), StringComparison.Ordinal),
        (JsonValueKind.True or JsonValueKind.False, JsonValueKind.True or JsonValueKind.False) => actual.GetValueKind() == expected.GetValueKind(),
        _ => false
    };

    // Integers keep every digit as decimals; only values beyond decimal's range compare as doubles.
    private static bool SameNumber(string actual, string expected) =>
        decimal.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) && decimal.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)
            ? a == b : double.Parse(actual, CultureInfo.InvariantCulture) == double.Parse(expected, CultureInfo.InvariantCulture);
}

internal sealed class AnalysisHandles(byte[] key)
{
    public string Issue(string project, string kind, JsonObject value)
    {
        var data = Encoding.UTF8.GetBytes(J.O(("project", project), ("kind", kind), ("value", value)).ToJsonString());
        return Base64Url.EncodeToString(data) + "." + Convert.ToHexStringLower(HMACSHA256.HashData(key, data));
    }
    public JsonObject Read(string token, string project, string kind)
    {
        try
        {
            var parts = token.Split('.');
            var data = Base64Url.DecodeFromChars(parts[0]);
            if (parts.Length != 2 || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(parts[1]), HMACSHA256.HashData(key, data))) throw new FormatException();
            var payload = JsonNode.Parse(data)!.AsObject();
            if (J.String(payload, "project") != project || J.String(payload, "kind") != kind) throw new FormatException();
            return payload["value"]!.AsObject();
        }
        catch (Exception e) when (e is FormatException or JsonException or InvalidOperationException or IndexOutOfRangeException)
        { throw new AnalysisInputException(kind, "This handle is invalid or belongs to another project.", kind + ".invalid"); }
    }
}
