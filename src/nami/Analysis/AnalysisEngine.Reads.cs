using System.Text.Json.Nodes;
using Nami.Collector.Storage;

namespace Nami.Collector.Analysis;

internal sealed partial class AnalysisEngine
{
    private static readonly string[] s_missingArguments = ["Arguments not captured; repeats cannot be confirmed without argument hashes."];
    private JsonObject AnnotationJson(AnnotationRow row) => _state.GetValueOrDefault("annotation:" + row.AnnotationId) is { } metadata ? JsonNode.Parse(metadata.ValueJson)!.AsObject() : JsonSerializer.SerializeToNode(AnnotationMapper.ToContract(row), global::Nami.Api.Contracts.ContractTypes.TypeInfo<global::Nami.Api.Contracts.Annotations.Annotation>())!.AsObject();
    private List<JsonObject> TraceAnnotations(string traceId, IReadOnlySet<string>? spanIds = null) =>
    [
        .. snapshot.Annotations
            .Where(note =>
                note.TraceId == traceId && (note.SpanId is null || spanIds is null || spanIds.Contains(note.SpanId)))
            .Select(AnnotationJson)
    ];
    private static IEnumerable<string> AnnotationLabels(IEnumerable<JsonObject> notes) => notes
        .SelectMany(static note => (note["labels"] as JsonArray)?.Select(J.Text) ?? [])
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
    // A log record as the analysis tools return it: when, how severe, from which service, what it said and
    // where it belongs. log_id identifies it for paging and leaves before the response does.
    private static JsonObject LogJson(AnalysisRecord row) => J.O(("log_id", row.Id), ("time", J.Time(row.Start)),
        ("severity", row.Log!.SeverityText is { Length: > 0 } text ? text : SeverityName(row.Log.SeverityNumber)), ("severity_number", row.Log.SeverityNumber),
        ("service", row.Service), ("body", LogBody(row)), ("trace_id", row.TraceId), ("span_id", row.SpanId), ("attributes", ContractAttributes(row.Attributes)));
    // The OTel log data model's short names for its severity number ranges.
    private static string? SeverityName(byte number) => number switch
    {
        >= 1 and <= 4 => "TRACE", <= 8 and >= 5 => "DEBUG", <= 12 and >= 9 => "INFO", <= 16 and >= 13 => "WARN", <= 20 and >= 17 => "ERROR", <= 24 and >= 21 => "FATAL", _ => null
    };
    private static JsonObject Tokens(IEnumerable<AnalysisRecord> rows)
    {
        var spans = rows.Where(s => s.Span is not null).Select(s => s.Span!).ToList();
        long? Sum(Func<SpanStorageRow, long?> select) => spans.Any(s => select(s).HasValue) ? spans.Sum(s => select(s) ?? 0) : null;
        return J.O(("input", Sum(s => s.GenAiInputTokens)), ("output", Sum(s => s.GenAiOutputTokens)), ("cache_read", Sum(s => s.GenAiCacheReadInputTokens)), ("cache_write", Sum(s => s.GenAiCacheWriteInputTokens)), ("reasoning", Sum(s => s.GenAiReasoningTokens)));
    }
    private static long TotalTokens(IEnumerable<AnalysisRecord> rows) => rows.Sum(static r => (r.Span?.GenAiInputTokens ?? 0) + (r.Span?.GenAiOutputTokens ?? 0));
    private JsonObject AgentRun(JsonObject input)
    {
        var rows = Execution(input);
        if (rows.Count is 0)
        {
            var (kind, id, lister) = J.String(input, "trace_id") is { } trace ? ("trace", trace, "list_traces lists the traces") : ("session", J.String(input, "session_id"), "list_sessions lists the sessions");
            throw new AnalysisInputException(kind + "_id", $"{(kind == "trace" ? "Trace" : "Session")} {id} was not found in this project; {lister}.", kind + ".not_found") { Missing = id };
        }
        var steps = rows.Where(r => r.Span?.GenAiOperationName is not null || r.Value(CollectorSemanticAttributeCatalog.McpMethodName) is not null || r.Value(CollectorSemanticAttributeCatalog.GenAiToolName) is not null).Select((r, i) =>
        {
            var kind = r.Value(CollectorSemanticAttributeCatalog.GenAiToolName) is not null || r.Value(CollectorSemanticAttributeCatalog.McpMethodName) is not null ? "tool_call" : r.Span?.GenAiOperationName is "invoke_agent" or "create_agent" ? "agent" : r.Span?.GenAiOperationName == "retrieval" ? "retrieval" : "llm_call";
            return J.O(("index", i), ("kind", kind), ("name", r.Name), ("mcp_method", r.Value(CollectorSemanticAttributeCatalog.McpMethodName)), ("mcp_server", r.Value("mcp.server.name")), ("duration_ms", r.Duration), ("tokens", Tokens([r])), ("status", Status(r)), ("error", r.Error ? Message(r) : null), ("trace_id", r.TraceId), ("span_id", r.Id));
        }).ToList();
        var repeated = rows.Select(r => (Producer: r.Service, Tool: J.Text(r.Value(CollectorSemanticAttributeCatalog.GenAiToolName)), Hash: J.Text(r.Value(CollectorSemanticAttributeCatalog.GenAiToolCallArgumentsHash))))
            .Where(r => r.Tool.Length > 0 && r.Hash.Length > 0).GroupBy(r => r).Where(g => g.Count() > 1).Select(g => J.O(("tool", g.Key.Tool), ("argument_hash", g.Key.Hash), ("count", g.Count())));
        var total = TotalTokens(rows); var start = rows.Count is 0 ? now : J.Instant(rows.Min(r => r.Start));
        return J.O(("run", J.O(("agent_name", rows.Select(r => J.Text(r.Value(CollectorSemanticAttributeCatalog.GenAiAgentName))).FirstOrDefault(v => v.Length > 0)), ("models", rows.Select(r => r.Span?.GenAiResponseModel ?? r.Span?.GenAiRequestModel).OfType<string>().Distinct()), ("started", start.ToString("O")), ("duration_ms", rows.Count is 0 ? 0 : (rows.Max(r => r.End) - rows.Min(r => r.Start)) / 1_000_000d), ("tokens", Tokens(rows)), ("errors", rows.Count(r => r.Error)), ("session_id", J.String(input, "session_id")), ("trace_id", J.String(input, "trace_id")))),
            ("steps", J.A(steps)), ("findings", J.O(("repeated_calls", J.A(repeated)), ("failed_tool_calls", J.A(steps.Where(s => J.String(s, "kind") == "tool_call" && J.String(s, "status") == "error").Take(10).Select(static s => s["index"]))), ("slowest_steps", J.A(steps.OrderByDescending(s => J.Number(s, "duration_ms")).Take(3).Select(static s => s["index"]))), ("token_share", J.A(rows.GroupBy(r => r.Name).Select(g => J.O(("name", g.Key), ("tokens", TotalTokens(g)), ("share", total is 0 ? 0 : TotalTokens(g) / (double)total))))))),
            ("unknowns", J.N(rows.Any(r => r.Value(CollectorSemanticAttributeCatalog.GenAiToolName) is not null && r.Value(CollectorSemanticAttributeCatalog.GenAiToolCallArgumentsHash) is null) ? s_missingArguments : [])),
            ("coverage", Coverage(rows.Count, steps.Count, steps.Count, false)));
    }
    private JsonObject Sessions(JsonObject input)
    {
        var activeCutoff = NamiSessionActivity.GetActiveCutoffUnixNano(now);
        var all = _spans.Where(s => s.SessionId is not null).GroupBy(s => s.SessionId).Select(g =>
        {
            var items = g.ToList(); var start = items.Min(s => s.Start); var end = items.Max(s => s.End); var errors = items.Count(s => s.Error);
            var model = items.Select(s => s.Span?.GenAiResponseModel ?? s.Span?.GenAiRequestModel).OfType<string>().Distinct().ToArray();
            var active = end >= activeCutoff;
            var annotations = snapshot.Annotations.Where(n => n.SessionId == g.Key).Select(AnnotationJson).ToList();
            var labels = annotations.SelectMany(n => (n["labels"] as JsonArray)?.Select(J.Text) ?? []);
            var name = items.Select(s => J.Text(s.Value(CollectorSemanticAttributeCatalog.GenAiAgentName))).FirstOrDefault(v => v.Length > 0);
            if (!_window.Contains(start) || J.Bool(input, "active_only") && !active || J.Bool(input, "errors_only") && errors is 0
                || (end - start) / 1_000_000d < J.Number(input, "min_duration_ms") || TotalTokens(items) < J.Number(input, "min_tokens")
                || J.String(input, "agent_name") is { } agent && name != agent || J.String(input, "model") is { } requested && !model.Contains(requested)
                || J.String(input, "annotation_label") is { } label && !labels.Contains(label)
                || !items.Any(s => AnalysisMatchers.Match(s, input["matchers"]) && (J.String(input, "service_name") is not { } service || s.Service == service))) return null;
            var byModel = new JsonObject(); foreach (var m in model) byModel[m] = Tokens(items.Where(s => (s.Span?.GenAiResponseModel ?? s.Span?.GenAiRequestModel) == m));
            // GenAI usage, tokens per model and notes only where the session has them.
            var requests = items.Count(s => s.Span?.GenAiProviderName is not null);
            return J.O(("session_id", g.Key), (CollectorSemanticAttributeCatalog.State, active ? "active" : "ended"), ("start_time", J.Time(start)), ("end_time", J.Time(end)), ("duration_ms", (end - start) / 1_000_000d), ("last_activity", J.Time(end)), ("trace_count", items.Select(s => s.TraceId).Distinct().Count()), ("span_count", items.Count), ("error_count", errors), ("services", items.Select(s => s.Service).Distinct()),
                ("genai_usage", requests is 0 && model.Length is 0 ? null : J.O(("request_count", requests), ("total_input_tokens", TotalInput(items)), ("total_output_tokens", TotalOutput(items)), ("models_used", model), ("providers_used", items.Select(s => s.Span?.GenAiProviderName).OfType<string>().Distinct()))),
                ("failure_ids", items.Where(s => s.Error).Select(s => FailureId(Signature(s))).Distinct().Take(5)), ("agent_name", name), ("tokens_by_model", byModel.Count is 0 ? null : byModel),
                ("annotations", annotations.Count is 0 ? null : J.A(annotations)), ("tokens", TotalTokens(items)));
        }).OfType<JsonObject>();
        var sorted = J.String(input, "order_by") switch { "errors" => all.OrderByDescending(s => J.Number(s, "error_count")), "duration" => all.OrderByDescending(s => J.Number(s, "duration_ms")), "tokens" => all.OrderByDescending(s => J.Number(s, "tokens")), _ => all.OrderByDescending(s => J.String(s, "last_activity"), StringComparer.Ordinal) };
        return Page(input, "sessions", sorted.ThenBy(s => J.String(s, "session_id"), StringComparer.Ordinal).Select(static row =>
        {
            row.Remove("last_activity");
            row.Remove("tokens");
            return row;
        }));
    }
    private static long TotalInput(IEnumerable<AnalysisRecord> rows) => rows.Sum(static s => s.Span?.GenAiInputTokens ?? 0);
    private static long TotalOutput(IEnumerable<AnalysisRecord> rows) => rows.Sum(static s => s.Span?.GenAiOutputTokens ?? 0);
    private JsonObject Traces(JsonObject input)
    {
        var rows = _spans.GroupBy(s => s.TraceId!).Where(g =>
        {
            var groupRows = g.ToList();
            var root = groupRows.OrderBy(s => Depth(s, groupRows)).ThenBy(s => s.Start).First(); var duration = (g.Max(s => s.End) - g.Min(s => s.Start)) / 1_000_000d;
            return _window.Contains(g.Min(s => s.Start)) && (!J.Bool(input, "errors_only") || g.Any(s => s.Error))
                && (J.String(input, "session_id") is not { } session || g.Any(s => s.SessionId == session))
                && (J.String(input, "root_span_name") is not { } name || root.Name == name)
                && (J.String(input, "server_name") is not { } mcpServer || g.Any(s => J.Text(s.Value("mcp.server.name")) == mcpServer || s.Service == mcpServer))
                && (J.String(input, "tool_name") is not { } toolName || g.Any(s => J.Text(s.Value(CollectorSemanticAttributeCatalog.GenAiToolName)) == toolName))
                && (J.String(input, "transport") is not { } transport || g.Any(s => J.Text(s.Value("mcp.transport")) == transport || J.Text(s.Value(CollectorSemanticAttributeCatalog.NetworkTransport)) == transport))
                && (J.String(input, "failure_class") is not { } failureClass || g.Any(s => s.Error && McpFailureClass(s) == failureClass))
                && duration >= J.Number(input, "min_duration_ms") && duration <= J.Number(input, "max_duration_ms", double.MaxValue)
                && (J.String(input, "failure_id") is not { } fid || g.Any(s => s.Error && FailureId(Signature(s)) == fid))
                && (J.String(input, "annotation_label") is not { } label || AnnotationLabels(TraceAnnotations(g.Key)).Contains(label, StringComparer.Ordinal))
                && g.Any(s => AnalysisMatchers.Match(s, input["matchers"]) && (J.String(input, "service_name") is not { } service || s.Service == service) && (J.String(input, "release") is not { } release || ReleaseOf(s) == release));
        }).Select(g =>
        {
            var items = g.ToList(); var root = items.OrderBy(s => Depth(s, items)).ThenBy(s => s.Start).First();
            var start = g.Min(s => s.Start); var duration = g.Max(s => s.End) - start; var errors = g.Count(s => s.Error);
            return (Id: g.Key, Start: start, Duration: duration, Errors: errors, Trace: J.O(("trace_id", g.Key), ("root", J.O(("service", root.Service), ("name", root.Name))),
                ("start_time", J.Time(start)), ("duration_ms", duration / 1_000_000d), ("span_count", g.Count()), ("error_count", errors), ("services", g.Select(s => s.Service).Distinct()),
                ("failure_ids", g.Where(s => s.Error).Select(s => FailureId(Signature(s))).Distinct().Take(3)), ("session_id", g.Select(static s => s.SessionId).FirstOrDefault(static s => s is not null))));
        });
        // Newest first, longest first or most error spans first; equal values keep the newest trace first.
        var traces = (J.String(input, "order_by") switch
            {
                "duration" => rows.OrderByDescending(static t => t.Duration),
                "errors" => rows.OrderByDescending(static t => t.Errors),
                _ => rows.OrderByDescending(static t => t.Start)
            }).ThenByDescending(static t => t.Start).ThenBy(static t => t.Id, StringComparer.Ordinal).Select(static t => t.Trace).ToList();
        return Page(input, "traces", traces);
    }
    private JsonObject Trace(JsonObject input)
    {
        var rows = Execution(input); var selection = J.String(input, "selection") ?? "all";
        if (rows.Count is 0) throw new AnalysisInputException("trace_id", $"Trace {J.String(input, "trace_id")} was not found in this project; list_traces lists the traces.", "trace.not_found") { Missing = J.String(input, "trace_id") };
        var selected = rows.Where(s => selection is "all" or "critical_path" || s.Error).ToHashSet();
        var children = rows.ToLookup(s => s.Span?.ParentSpanId ?? "", StringComparer.Ordinal);
        if (selection == "errors_with_ancestors") foreach (var s in selected.ToList())
            { var current = s; while (current.Span?.ParentSpanId is { } parent && SpanIndex.GetValueOrDefault((current.TraceId, parent)) is { } ancestor && selected.Add(ancestor)) current = ancestor; }
        if (selection == "critical_path")
        {
            selected.Clear(); var current = rows.OrderBy(s => Depth(s, rows)).ThenBy(s => s.Start).First(); selected.Add(current);
            while (children[current.Id].OrderByDescending(s => s.Duration).FirstOrDefault() is { } child && selected.Add(child)) current = child;
        }
        var withAttributes = J.Bool(input, "include_attributes", true);
        var keep = (input["attributes"] as JsonArray)?.Select(J.Text).ToHashSet(StringComparer.Ordinal);
        var spans = selected.OrderBy(s => s.Start).ThenBy(s => s.Id, StringComparer.Ordinal).Select(s =>
        {
            var row = J.O(("span_id", s.Id), ("parent_span_id", s.Span?.ParentSpanId is { Length: > 0 } parent ? parent : null), ("service", s.Service), ("name", s.Name),
                ("start_time", J.Time(s.Start)), ("duration_ms", s.Duration), ("status", Status(s)),
                ("message", s.Span?.StatusMessage is { Length: > 0 } message ? message : null));
            if (withAttributes) row["attributes"] = ContractAttributes(keep is null ? s.Attributes : new JsonObject(s.Attributes.Where(p => keep.Contains(p.Key)).Select(static p => KeyValuePair.Create(p.Key, p.Value?.DeepClone()))));
            if (selection == "critical_path")
            {
                var intervals = children[s.Id].Select(c => (Start: Math.Max(c.Start, s.Start), End: Math.Min(c.End, s.End))).Where(x => x.End > x.Start).OrderBy(x => x.Start).ToList();
                ulong covered = 0, reached = 0;
                foreach (var interval in intervals) { covered += interval.End - Math.Max(interval.Start, Math.Min(reached, interval.End)); reached = Math.Max(reached, interval.End); }
                var self = Math.Max(0, s.Duration - covered / 1_000_000d); row["self_time_ms"] = self; row["self_time_share"] = s.Duration is 0 ? 0 : self / s.Duration;
            }
            return row;
        }).ToList();
        var start = rows.Min(static s => s.Start); var end = Math.Max(start, rows.Max(static s => s.End));
        var trace = J.O(("trace_id", rows[0].TraceId), ("start_time", J.Time(start)), ("end_time", J.Time(end)), ("duration_ms", (end - start) / 1_000_000d), ("span_count", rows.Count),
            ("error_count", rows.Count(static s => s.Error)), ("services", rows.Select(static s => s.Service).Distinct()), ("spans", J.A(spans)));
        // Each service's resource once, not on every span.
        if (withAttributes) trace["resources"] = J.A(rows.GroupBy(static s => (s.Service, Resource: s.Resource.ToJsonString()))
            .OrderBy(static g => g.Key.Service, StringComparer.Ordinal).ThenBy(static g => g.Key.Resource, StringComparer.Ordinal)
            .Select(static g => J.O(("service", g.Key.Service), ("attributes", ContractAttributes(g.First().Resource)))));
        var links = rows.SelectMany(s => JsonNode.Parse(s.Span?.LinksJson ?? "[]")?.AsArray() ?? []).OfType<JsonObject>().Select(l => { var traceId = J.String(l, "trace_id") ?? J.String(l, "traceId"); var spanId = J.String(l, "span_id") ?? J.String(l, "spanId"); return J.O(("trace_id", traceId), ("span_id", spanId), ("available", SpanIndex.ContainsKey((traceId, spanId ?? "")) || traceId is not null && spanId is not null && snapshot.RetainedLinkTargets.Contains((traceId, spanId)))); });
        var traceIds = rows.Select(s => s.TraceId).ToHashSet(StringComparer.Ordinal);
        // The window is the trace's own range, which a follow-up read can pass on.
        return J.O(("trace", trace), ("window", new AnalysisWindow(J.Instant(start), J.Instant(end)).Json), ("annotations", J.A(TraceAnnotations(rows[0].TraceId!, rows.Select(static row => row.Id).ToHashSet(StringComparer.Ordinal)))),
            ("missing_parent_span_ids", rows.Where(s => s.Span?.ParentSpanId is { Length: > 0 } p && !SpanIndex.ContainsKey((s.TraceId, p))).Select(s => s.Span!.ParentSpanId!).Distinct()), ("links", J.A(links)),
            ("logs", J.A(_logs.Where(l => traceIds.Contains(l.TraceId)).OrderByDescending(static l => l.Start).ThenBy(static l => l.Id, StringComparer.Ordinal).Take((int)J.Number(input, "max_logs")).Select(LogJson))),
            ("settled", Age(end) >= TimeSpan.FromSeconds(5)), ("coverage", Coverage(rows.Count, selected.Count, spans.Count, false)));
    }
    private JsonObject Logs(JsonObject input)
    {
        var rows = Select(input, "logs").Where(l => (J.String(input, "trace_id") is not { } trace || l.TraceId == trace) && (J.String(input, "span_id") is not { } span || l.SpanId == span) && (J.String(input, "session_id") is not { } session || l.SessionId == session)
            && (l.Log?.SeverityNumber ?? 0) >= J.Number(input, "severity_min") && (J.String(input, "query") is not { } query || LogBody(l).Contains(query, StringComparison.OrdinalIgnoreCase))).OrderByDescending(l => l.Start).ThenBy(l => l.Id, StringComparer.Ordinal).ToList();
        var context = (int)J.Number(input, "context");
        JsonObject result;
        // A grouped search answers with its patterns alone, the most frequent first.
        if ((input["group_by"] as JsonArray)?.Any(static key => J.Text(key) == "pattern") == true)
            result = Page(input, "patterns", rows.GroupBy(l => J.Normalize(LogBody(l))).OrderByDescending(static g => g.Count()).ThenBy(static g => g.Key, StringComparer.Ordinal)
                .Select(g => J.O(("template", g.Key), ("count", g.Count()), ("severity", g.Max(l => l.Log!.SeverityNumber)), ("first_seen", J.Time(g.Min(l => l.Start))), ("last_seen", J.Time(g.Max(l => l.Start))),
                    ("example_trace_ids", g.Select(l => l.TraceId).OfType<string>().Distinct().Take(3)))));
        else result = Page(input, "logs", rows.Select(LogJson));
        // Both answers count the matching log records; a capped read leaves that total unknown.
        result["coverage"]!["matched"] = snapshot.ScanComplete ? rows.Count : null;
        if (context > 0 && result["logs"] is JsonArray page)
        {
            var seen = page.Select(p => J.String(p, "log_id")).OfType<string>().ToHashSet(); var extra = new List<JsonObject>();
            foreach (var match in rows.Where(r => seen.Contains(r.Id)))
            { var resource = _logs.Where(l => JsonNode.DeepEquals(l.Resource, match.Resource) && l.Service == match.Service).ToList(); var index = resource.IndexOf(match); extra.AddRange(resource.Skip(Math.Max(0, index - context)).Take(context * 2 + 1).Where(l => seen.Add(l.Id)).Select(LogJson)); }
            result["context_logs"] = J.A(extra);
        }
        foreach (var field in new[] { "logs", "context_logs" })
            if (result[field] is JsonArray entries) foreach (var log in entries.OfType<JsonObject>()) log.Remove("log_id");
        return result;
    }
}
