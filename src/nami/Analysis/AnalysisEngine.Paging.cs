using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Nami.Api.Contracts.Mcp.Tools;

namespace Nami.Collector.Analysis;

internal sealed partial class AnalysisEngine
{
    private static readonly string[] s_identityKeys = ["failure_id", "session_id", "trace_id", "series_id", "ci_run_id", "name"];
    // aggregate_telemetry's record count per group, in the order of its groups.
    private int[]? _groupSizes;

    /// <summary>
    /// The contract's page size for the current operation, which an omitted limit takes. The HTTP boundary refused a
    /// limit outside the contract's range before the engine ran.
    /// </summary>
    private int LimitDefault => _operation switch
    {
        "triage" => TriageInput.LimitDefault,
        "get_failure_context" => GetFailureContextInput.LimitDefault,
        "compare_groups" => CompareGroupsInput.LimitDefault,
        "compare_traces" => CompareTracesInput.LimitDefault,
        "verify_fix" => VerifyFixInput.LimitDefault,
        "aggregate_telemetry" => AggregateTelemetryInput.LimitDefault,
        "discover_attributes" => DiscoverAttributesInput.LimitDefault,
        "get_agent_run" => GetAgentRunInput.LimitDefault,
        "list_releases" => ListReleasesInput.LimitDefault,
        "get_service_map" => GetServiceMapInput.LimitDefault,
        "check_telemetry_health" => CheckTelemetryHealthInput.LimitDefault,
        "get_mcp_stats" => GetMcpStatsInput.LimitDefault,
        "list_sessions" => ListSessionsInput.LimitDefault,
        "list_traces" => ListTracesInput.LimitDefault,
        "search_logs" => SearchLogsInput.LimitDefault,
        "list_metrics" => ListMetricsInput.LimitDefault,
        "ci_log" => CiLogInput.LimitDefault,
        "ci_log.phases" => CiLogInput.PhaseLimitDefault,
        "run_saved_query" => RunSavedQueryInput.LimitDefault,
        _ => throw new InvalidOperationException($"'{_operation}' has no page size.")
    };

    // get_trace takes no limit: its logs, annotations and links page by its spans' default page size.
    private int Limit(JsonObject input) => _operation == "get_trace" ? GetTraceInput.MaxSpansDefault : (int)J.Number(input, "limit", LimitDefault);

    // A span, log or step collection pages by its own maximum, or by limit when lower.
    private int PageSize(JsonObject input, string path) => path switch
    {
        "trace.spans" => (int)J.Number(input, "max_spans", GetTraceInput.MaxSpansDefault),
        "observed.spans" => Math.Min(Limit(input), (int)J.Number(input, "max_spans", GetFailureContextInput.MaxSpansDefault)),
        "observed.logs" => Math.Min(Limit(input), (int)J.Number(input, "max_logs", GetFailureContextInput.MaxLogsDefault)),
        _ => Limit(input)
    };

    // One collection of a list operation; examined defaults to the window's spans and log records.
    private JsonObject Page(JsonObject input, string field, IEnumerable<JsonObject> source, int? examined = null)
    {
        var all = source.Cast<JsonNode?>().ToList(); var limit = Limit(input); var fingerprint = Fingerprint(input);
        var start = Resume(Positions(input, fingerprint), field, all);
        var page = all.Skip(start).Take(limit).ToList(); var end = start + page.Count; var more = end < all.Count;
        return J.O((field, J.A(page)), ("has_more", more), ("next_cursor", more ? Continue(fingerprint, J.O((field, Position(all, end)))) : null),
            ("coverage", Coverage(examined ?? _spans.Count(s => _window.Contains(s.Start)) + _logs.Count(s => _window.Contains(s.Start)), all.Count, page.Count, more)));
    }

    // Each collection advances independently. The signed continuation fixes the window and
    // query, so reducing a page size never discards another collection's remaining evidence.
    private JsonObject PageCollections(JsonObject input, JsonObject result)
    {
        string[] paths = _operation switch
        {
            "triage" => ["failures", "regressions", "releases", "silent_services"],
            "get_failure_context" => ["observed.spans", "observed.logs", "context.similar_failures", "context.changes", "context.annotations"],
            "compare_groups" => ["discriminators", "structural", "excluded_keys"],
            "compare_traces" => ["aligned", "unmatched", "after_divergence"],
            "verify_fix" => ["guardrails"],
            "aggregate_telemetry" => ["groups"],
            "discover_attributes" => ["attributes"],
            "get_agent_run" => ["steps", "findings.repeated_calls", "findings.failed_tool_calls", "findings.token_share"],
            "get_service_map" => ["edges", "nodes"],
            "check_telemetry_health" => ["silent_services", "semconv_gaps", "orphan_spans.services", "schema_urls", "metric_conflicts.series"],
            "get_mcp_stats" => ["groups", "tools", "comparison.groups"],
            "get_trace" => ["trace.spans", "logs", "annotations", "links"],
            _ => []
        };
        if (paths.Length is 0) return result;
        var fingerprint = Fingerprint(input); var positions = Positions(input, fingerprint);
        var more = false; var totals = new JsonObject(); var next = new JsonObject(); var returned = 0;
        foreach (var path in paths)
        {
            var parts = path.Split('.'); JsonNode? parent = result;
            foreach (var key in parts[..^1]) parent = (parent as JsonObject)?[key];
            if (parent is not JsonObject owner || owner[parts[^1]] is not JsonArray array) continue;
            var all = array.ToList(); var size = PageSize(input, path);
            var start = Resume(positions, path, all);
            var page = all.Skip(start).Take(size).ToList(); var end = start + page.Count;
            totals[path] = all.Count; next[path] = Position(all, end); more |= end < all.Count;
            // A failure's evidence is its spans and logs; its context lists are not records it returned.
            if (_operation != "get_failure_context" || path is "observed.spans" or "observed.logs") returned += page.Count;
            owner[parts[^1]] = J.A(page);
            // other is the groups after this page and the records they hold, whichever measures were requested.
            if (path == "groups" && _groupSizes is { } sizes) result["other"] = J.O(("groups", all.Count - end), ("count", sizes.Skip(end).Sum()));
        }
        result["page_totals"] = totals;
        result["has_more"] = more;
        if (more) result["next_cursor"] = Continue(fingerprint, next);
        else result.Remove("next_cursor");
        if (result["coverage"] is JsonObject coverage) { coverage["returned"] = returned; coverage["truncated"] = more || J.Bool(coverage, "truncated"); }
        return result;
    }

    // Page sizes may shrink between pages (a budget retry), and the signed cursor fixes the absolute
    // window, so neither the sizes nor the window's relative or resolved spelling identify the query.
    private static string Fingerprint(JsonObject input)
    {
        var scope = input.DeepClone().AsObject();
        foreach (var field in new[] { "cursor", "limit", "phase_limit", "max_spans", "max_logs", "start_time", "end_time", "lookback_minutes" }) scope.Remove(field);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(scope.ToJsonString())));
    }

    // The positions a signed cursor recorded, once it is known to continue this operation and query.
    private JsonObject? Positions(JsonObject input, string fingerprint)
    {
        if (J.String(input, "cursor") is not { } cursor) return null;
        var state = handles.Read(cursor, project, "cursor");
        if (J.String(state, "operation") != _operation || J.String(state, "filter") != fingerprint || state["positions"] is not JsonObject positions)
            throw new AnalysisInputException("cursor", "Use this cursor with its original operation and filters.", "cursor.invalid");
        return positions;
    }

    private string Continue(string fingerprint, JsonObject positions) =>
        handles.Issue(project, "cursor", J.O(("operation", _operation), ("filter", fingerprint), ("positions", positions), ("window", _window.Json)));

    // A position is how many items the earlier pages returned and a digest of their identities in order.
    private static JsonObject Position(List<JsonNode?> all, int seen) => J.O(("seen", seen), ("digest", Digest(all, seen)));

    // A continuation resumes after the items the earlier pages returned only while those are still exactly the
    // first items: an item that arrived or vanished ahead of the position would otherwise be skipped or repeated.
    private static int Resume(JsonObject? positions, string path, List<JsonNode?> all)
    {
        if (positions?[path] is not JsonObject position) return 0;
        var seen = (int)J.Number(position, "seen", -1);
        if (seen < 0 || seen > all.Count || J.String(position, "digest") != Digest(all, seen))
            throw new AnalysisInputException("cursor", "Results ahead of this cursor changed after it was issued; start a new query.", "cursor.stale");
        return seen;
    }

    private static string Digest(List<JsonNode?> items, int count)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var length = new byte[4];
        foreach (var item in items.Take(count))
        {
            var identity = Encoding.UTF8.GetBytes(Identity(item));
            BinaryPrimitives.WriteInt32LittleEndian(length, identity.Length);
            hash.AppendData(length); hash.AppendData(identity);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    // An item is its record id, a span its trace and span id; anything else is its content.
    private static string Identity(JsonNode? item)
    {
        if (item is not JsonObject row) return "value:" + (item?.ToJsonString() ?? "null");
        if (J.String(row, "log_id") is { } log) return "log:" + log;
        if (J.String(row, "id") is { } id) return "id:" + id;
        if (J.String(row, "span_id") is { } span) return "span:" + J.String(row, "trace_id") + "/" + span;
        foreach (var key in s_identityKeys)
            if (J.String(row, key) is { } value) return key + ":" + value;
        return "content:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(row.ToJsonString())));
    }
}
