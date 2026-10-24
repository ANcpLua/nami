using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nami.Api.Contracts;
using Nami.Collector.Storage;
using Nami.Collector.Ingestion.Primitives;

namespace Nami.Tools;

internal readonly record struct ToolClock(DateTimeOffset UtcNow);
internal readonly record struct ToolRunResult(JsonElement Content, bool IsError);
internal readonly record struct Result<T, TError>(T? Value, TError? Error)
{
    public bool IsSuccess => Error is null;
}

/// <summary>Pure, deterministic analysis over an immutable project snapshot.</summary>
internal static class ToolDispatcher
{
    private static readonly string[] s_tools = ["sources_status", "errors_recent", "traces_get", "traces_slowest", "logs_search", "changes_recent"];
    private static readonly string[] s_signals = ["traces", "metrics", "logs"];

    public static IReadOnlyList<string> Names => s_tools;
    public static string? SnapshotId(JsonElement input)
    {
        var explicitId = Text(input, "snapshot_id");
        if (Text(input, "cursor") is not { } cursor) return explicitId;
        var parts = DecodeCursor(cursor);
        if (explicitId is not null && explicitId != parts[0]) throw new ToolInputException("cursor", "Cursor belongs to a different snapshot.");
        return parts[0];
    }

    public static ToolRunResult Run(string name, ISnapshot snapshot, JsonElement input, ToolClock clock)
    {
        try
        {
            // The immutable snapshot carries the clock; a replay must ignore elapsed wall time.
            _ = clock;
            if (!s_tools.Contains(name, StringComparer.Ordinal)) return Error("not_found", "Unknown tool.", "name", s_tools);
            var contracts = ContractTypes.Analysis(name);
            if (ContractJson.Validate(contracts.Input, input) is { } issue)
                return Error("invalid_argument", issue.Message, issue.Field, issue.Allowed);
            // Relative inputs are interpreted against the captured clock, including after a restart.
            var end = snapshot.CapturedAt;
            var start = ParseSince(Text(input, "since") ?? "-60m", end);
            var service = Text(input, "service");
            var watermarks = snapshot.Signals.Where(s => service is null || s.ServiceName == service)
                .GroupBy(static s => s.Signal).ToDictionary(static g => g.Key, static g => g.Max(s => s.WatermarkUnixNano), StringComparer.Ordinal);
            var window = new ToolWindow(start, end, service) { RequestedEnd = end, Watermarks = watermarks };
            var maxBytes = input.TryGetProperty("max_bytes", out var budget) ? ContractJson.ReadInteger(budget) : 32768;
            var fields = input.TryGetProperty("fields", out var projection)
                ? projection.EnumerateArray().Select(static f => f.GetString()!).ToArray() : [];
            if (fields.Any(static field => field is "profiles" or "baggage"))
                return Error("data_unavailable", "Profiles and baggage are not recorded signals in this collector.", "fields");
            var result = name switch
            {
                "sources_status" => SourcesStatusTool.Run(snapshot, window),
                "errors_recent" => ErrorsRecentTool.Run(snapshot, window),
                "traces_slowest" => TracesSlowestTool.Run(snapshot, window, Text(input, "operation"), input.TryGetProperty("limit", out var limit) ? ContractJson.ReadInteger(limit) : 10),
                "logs_search" => LogsSearchTool.Run(snapshot, window, Text(input, "query")!),
                "changes_recent" => ChangesRecentTool.Run(snapshot, window),
                "traces_get" => TracesGetTool.Run(snapshot, window, Text(input, "trace_id")!),
                _ => throw new InvalidOperationException()
            };
            return Page(name, contracts.Output, snapshot, input, result, fields, maxBytes, window);
        }
        catch (ToolInputException error) { return Error("invalid_argument", error.Message, error.Field, error.Allowed); }
        catch (ToolNotFoundException error) { return Error("not_found", error.Message, error.Field, null, error.ResourceId); }
        catch (FormatException) { return Error("invalid_argument", "Malformed cursor or timestamp.", "cursor"); }
        catch (OverflowException) { return Error("invalid_argument", "Timestamp or count is outside its supported range.", "since"); }
        catch (ArgumentOutOfRangeException) { return Error("invalid_argument", "Timestamp is outside its supported range.", "since"); }
    }

    internal static ToolRunResult Error(string code, string message, string? field = null,
        IReadOnlyList<string>? allowed = null, string? resourceId = null)
    {
        // Refusals obey the minimum valid response budget too. Echoes are bounded by their
        // encoded JSON bytes, so control characters and non-ASCII cannot bypass the limit.
        const int maxErrorBytes = 1024;
        var value = new JsonObject
        {
            ["code"] = BoundedEcho(code, 64), ["message"] = BoundedEcho(message, 320), ["retryable"] = false
        };
        if (field is not null) value["field"] = BoundedEcho(field, 128);
        if (resourceId is not null)
        {
            value["resource_type"] = field == "trace_id" ? "trace" : "snapshot";
            value["resource_id"] = BoundedEcho(resourceId, 128);
        }
        if (allowed is not null)
        {
            var choices = new JsonArray();
            value["allowed"] = choices;
            var omitted = false;
            foreach (var choice in allowed)
            {
                choices.Add((JsonNode?)JsonValue.Create(BoundedEcho(choice, 80)));
                if (Encoding.UTF8.GetByteCount(value.ToJsonString()) <= maxErrorBytes - 48) continue;
                choices.RemoveAt(choices.Count - 1);
                omitted = true;
                break;
            }
            if (omitted) value["hint"] = "More allowed values omitted.";
        }
        var output = ContractJson.Serialize("Common.ToolError", ContractJson.ToElement(value));
        while (Encoding.UTF8.GetByteCount(output.GetRawText()) > maxErrorBytes)
        {
            var current = value["message"]!.GetValue<string>();
            value["message"] = BoundedEcho(current, Math.Max(3, current.Length / 2));
            output = ContractJson.Serialize("Common.ToolError", ContractJson.ToElement(value));
        }
        return new(output, true);
    }

    private static string BoundedEcho(string value, int encodedByteLimit)
    {
        var result = new StringBuilder();
        var bytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var text = rune.ToString();
            var length = JsonEncodedText.Encode(text).EncodedUtf8Bytes.Length;
            if (bytes + length > encodedByteLimit - 3) return result.Append("...").ToString();
            result.Append(text);
            bytes += length;
        }
        return result.ToString();
    }

    private static ToolRunResult Page(string name, string schema, ISnapshot snapshot, JsonElement input, ToolRows result,
        string[] fields, int maxBytes, ToolWindow window)
    {
        foreach (var field in fields)
            if (!result.AllowedFields.Contains(field, StringComparer.Ordinal))
                throw new ToolInputException("fields", "Unknown projected field: " + field, result.AllowedFields);
        var offset = 0;
        var queryHash = QueryHash(input);
        if (Text(input, "cursor") is { } cursor)
        {
            var parts = DecodeCursor(cursor);
            if (parts[0] != snapshot.Id || parts[1] != name || parts[3] != queryHash || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out offset) || offset < 0 || offset > (result.Single && result.Items[0]["spans"] is JsonArray traceSpans ? traceSpans.Count : result.Items.Count))
                throw new ToolInputException("cursor", "Cursor does not match this snapshot, tool, or query.");
        }
        var watermarks = result.Signals.Select(signal => snapshot.Signals.Where(s => s.Signal == signal && window.Matches(s.ServiceName)).Select(static s => s.WatermarkUnixNano).DefaultIfEmpty(0UL).Max()).ToArray();
        var watermark = watermarks.Length == 0 ? 0 : watermarks.Min();
        var freshness = new JsonObject
        {
            ["watermark"] = Timestamp(watermark), ["complete"] = TimeConversions.ToUnixNanoUnsigned(window.RequestedEnd) <= watermark,
            ["examined"] = result.Examined, ["matched"] = result.Items.Count, ["returned"] = 0, ["truncated"] = false
        };
        var envelope = new JsonObject { ["snapshot_id"] = snapshot.Id, ["freshness"] = freshness };
        var page = new JsonArray();
        if (result.Single)
        {
            var item = Project(result.Items[0], fields);
            envelope[result.Member] = item;
            freshness["returned"] = 1;
            if (item["spans"] is JsonArray spans)
            {
                var source = spans.Select(static node => node?.DeepClone()).ToArray();
                spans.Clear();
                for (var i = offset; i < source.Length; i++)
                {
                    spans.Add(source[i]);
                    freshness["truncated"] = i + 1 < source.Length;
                    envelope["next_cursor"] = i + 1 < source.Length ? EncodeCursor(snapshot.Id, name, i + 1, queryHash) : null;
                    if (Encoding.UTF8.GetByteCount(envelope.ToJsonString()) <= maxBytes) continue;
                    spans.RemoveAt(spans.Count - 1);
                    freshness["truncated"] = true;
                    envelope["next_cursor"] = EncodeCursor(snapshot.Id, name, i, queryHash);
                    if (spans.Count == 0) return Error("invalid_argument", "max_bytes cannot fit a span; request fewer fields.", "fields", result.AllowedFields);
                    break;
                }
            }
            var serialized = ContractJson.Serialize(schema, ContractJson.ToElement(envelope));
            if (Encoding.UTF8.GetByteCount(serialized.GetRawText()) > maxBytes)
                return Error("invalid_argument", "Projected trace exceeds max_bytes; request fewer fields.", "fields", result.AllowedFields);
            return new(serialized, false);
        }
        envelope[result.Member] = page;
        for (var i = offset; i < result.Items.Count; i++)
        {
            page.Add((JsonNode)Project(result.Items[i], fields));
            freshness["returned"] = page.Count;
            freshness["truncated"] = i + 1 < result.Items.Count;
            envelope["next_cursor"] = i + 1 < result.Items.Count ? EncodeCursor(snapshot.Id, name, i + 1, queryHash) : null;
            if (Encoding.UTF8.GetByteCount(envelope.ToJsonString()) <= maxBytes) continue;
            page.RemoveAt(page.Count - 1);
            freshness["returned"] = page.Count;
            freshness["truncated"] = true;
            envelope["next_cursor"] = EncodeCursor(snapshot.Id, name, i, queryHash);
            break;
        }
        if (page.Count == 0 && offset < result.Items.Count)
            return Error("invalid_argument", "max_bytes cannot fit one result item; increase it or project fewer fields.", "max_bytes");
        var output = ContractJson.Serialize(schema, ContractJson.ToElement(envelope));
        if (Encoding.UTF8.GetByteCount(output.GetRawText()) > maxBytes)
            return Error("invalid_argument", "max_bytes is smaller than the result envelope.", "max_bytes");
        return new(output, false);
    }

    private static JsonObject Project(JsonObject item, string[] fields) => fields.Length == 0
        ? (JsonObject)item.DeepClone()
        : new JsonObject(item.Where(p => fields.Contains(p.Key, StringComparer.Ordinal)).Select(static p => KeyValuePair.Create(p.Key, p.Value?.DeepClone())));

    private static string QueryHash(JsonElement input)
    {
        var normalized = new JsonObject();
        foreach (var p in input.EnumerateObject().Where(static p => p.Name is not ("cursor" or "snapshot_id")).OrderBy(static p => p.Name, StringComparer.Ordinal))
            normalized[p.Name] = JsonNode.Parse(p.Value.GetRawText());
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized.ToJsonString())));
    }
    private static string EncodeCursor(string snapshot, string name, int offset, string hash) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join('|', snapshot, name, offset.ToString(CultureInfo.InvariantCulture), hash))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string[] DecodeCursor(string cursor)
    {
        var encoded = cursor.Replace('-', '+').Replace('_', '/');
        var parts = Encoding.UTF8.GetString(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '='))).Split('|');
        if (parts.Length != 4) throw new FormatException("Malformed cursor.");
        return parts;
    }
    internal static string? Text(JsonElement value, string name) => value.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    internal static string Timestamp(ulong nanos) => ContractJson.FormatTimestamp(TimeConversions.NanosToDateTimeOffset(nanos));
    internal static DateTimeOffset ParseSince(string value, DateTimeOffset now)
    {
        if (value.StartsWith('-') && value.Length > 2 && double.TryParse(value.AsSpan(1, value.Length - 2), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) && double.IsFinite(amount))
        {
            var duration = value[^1] switch { 's' => TimeSpan.FromSeconds(amount), 'm' => TimeSpan.FromMinutes(amount), 'h' => TimeSpan.FromHours(amount), 'd' => TimeSpan.FromDays(amount), _ => throw new ToolInputException("since", "Relative time must use seconds (s), minutes (m), hours (h), or days (d).") };
            var relative = now - duration;
            return relative < DateTimeOffset.UnixEpoch ? DateTimeOffset.UnixEpoch : relative;
        }
        if (ContractJson.Validate("Common.Timestamp", ContractJson.ToElement(JsonValue.Create(value))) is not null ||
            !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var absolute) || absolute > now)
            throw new ToolInputException("since", "Expected a past RFC 3339 timestamp or relative time such as -15m.");
        return absolute < DateTimeOffset.UnixEpoch ? DateTimeOffset.UnixEpoch : absolute;
    }
}

internal sealed record ToolRows(string Member, IReadOnlyList<JsonObject> Items, long Examined, string[] Signals, string[] AllowedFields, bool Single = false);
internal readonly record struct ToolWindow(DateTimeOffset Start, DateTimeOffset End, string? Service)
{
    internal DateTimeOffset RequestedEnd { get; init; } = End;
    internal IReadOnlyDictionary<string, ulong>? Watermarks { get; init; }
    internal bool Matches(string? service) => Service is null || Service == service;
    internal bool Contains(ulong nanos, string? signal = null)
    {
        var end = TimeConversions.ToUnixNanoUnsigned(End);
        if (Watermarks is { } watermarks)
        {
            if (signal is null && watermarks.Count > 0) end = Math.Min(end, watermarks.Values.Max());
            else if (signal is not null && watermarks.TryGetValue(signal, out var watermark)) end = Math.Min(end, watermark);
        }
        return nanos >= TimeConversions.ToUnixNanoUnsigned(Start) && nanos < end;
    }
}
internal sealed class ToolInputException(string fieldName, string message, IReadOnlyList<string>? allowed = null) : Exception(message)
{
    internal string Field => fieldName;
    internal IReadOnlyList<string>? Allowed => allowed;
}
internal sealed class ToolNotFoundException(string fieldName, string resourceId) : Exception($"No retained {fieldName} matches {resourceId} in this project.")
{
    internal string Field => fieldName;
    internal string ResourceId => resourceId;
}

internal sealed class NamiToolService(INamiStore store)
{
    public async Task<ToolRunResult> InvokeAsync(string name, JsonElement input, string projectId, CancellationToken cancellationToken = default)
    {
        string? snapshotId = null;
        try
        {
            if (!ToolDispatcher.Names.Contains(name, StringComparer.Ordinal)) return ToolDispatcher.Error("not_found", "Unknown tool.", "name", ToolDispatcher.Names);
            if (ContractJson.Validate(ContractTypes.Analysis(name).Input, input) is { } issue)
                return ToolDispatcher.Error("invalid_argument", issue.Message, issue.Field, issue.Allowed);
            snapshotId = ToolDispatcher.SnapshotId(input);
            var snapshot = await store.ReadSnapshotAsync(projectId, snapshotId, cancellationToken).ConfigureAwait(false);
            return ToolDispatcher.Run(name, snapshot, input, new ToolClock(snapshot.CapturedAt));
        }
        catch (SnapshotUnavailableException error) { return ToolDispatcher.Error("not_found", error.Message, "snapshot_id", resourceId: snapshotId); }
        catch (ToolInputException error) { return ToolDispatcher.Error("invalid_argument", error.Message, error.Field, error.Allowed); }
        catch (FormatException) { return ToolDispatcher.Error("invalid_argument", "Malformed cursor.", "cursor"); }
    }
}
