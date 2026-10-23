using System.Text.Json.Nodes;
using OpenTelemetry.Proto.Trace.V1;

namespace Nami.Collector.Analysis;

internal sealed partial class AnalysisEngine
{
    private JsonObject Aggregate(JsonObject input)
    {
        var signal = J.String(input, "signal") ?? "spans"; var rows = Select(input, signal);
        var keys = (input["group_by"] as JsonArray)?.Select(J.Text).ToArray() ?? [];
        var measures = (input["measures"] as JsonArray)?.Select(J.Text).ToArray() ?? ["count"];
        var valid = new[] { "count", "error_count", "error_rate", "duration_avg", "duration_p50", "duration_p90", "duration_p95", "duration_p99", "duration_max" };
        if (measures.Any(m => !valid.Contains(m) && !m.StartsWith("sum:", StringComparison.Ordinal))) throw new AnalysisInputException("measures", "Choose documented count, error, duration or sum:<attribute> measures.") { Allowed = [.. valid, "sum:<attribute>"] };
        JsonObject Measure(IEnumerable<AnalysisRecord> source)
        {
            var values = source.ToList(); var result = new JsonObject();
            foreach (var m in measures)
            {
                double? value = m switch
                {
                    "count" => values.Count, "error_count" => values.Count(s => s.Error), "error_rate" => values.Count is 0 ? null : values.Count(s => s.Error) / (double)values.Count,
                    "duration_avg" => values.Count is 0 ? null : values.Average(s => s.Duration), "duration_max" => values.Count is 0 ? null : values.Max(s => s.Duration),
                    "duration_p50" => J.Percentile(values.Select(s => s.Duration), .50), "duration_p90" => J.Percentile(values.Select(s => s.Duration), .90), "duration_p95" => J.Percentile(values.Select(s => s.Duration), .95), "duration_p99" => J.Percentile(values.Select(s => s.Duration), .99),
                    _ => values.Sum(s => double.TryParse(J.Text(s.Value(m[4..])), CultureInfo.InvariantCulture, out var n) ? n : 0)
                };
                result[m] = J.N(value);
            }
            return result;
        }
        var step = (long)J.Number(input, "step_ms");
        // A key no record carries is a typo, not one group of everything.
        if (rows.Count > 0 && keys.FirstOrDefault(k => rows.All(r => r.Value(k) is null)) is { } unrecorded)
            throw new AnalysisInputException("group_by", $"No record in this window carries {unrecorded}.", "group_by.unrecorded") { Hint = "discover_attributes lists the keys the records carry." };
        var order = J.String(input, "order_by") ?? measures[0]; var descending = !order.EndsWith(" asc", StringComparison.Ordinal); order = order.Split(' ')[0];
        if (!measures.Contains(order, StringComparer.Ordinal))
            throw new AnalysisInputException("order_by", $"order_by names one of the requested measures, optionally followed by ' asc'.", "order_by.unknown") { Allowed = measures };
        // A record without the key groups apart from one whose value is empty, under a null key.
        var groups = rows.GroupBy(r => string.Join('\0', keys.Select(k => r.Value(k)?.ToJsonString() ?? "\u0001"))).Select(g =>
        {
            var key = new JsonObject(); foreach (var k in keys) key[k] = g.First().Value(k)?.DeepClone();
            var result = J.O(("key", key), ("measures", Measure(g)));
            if (step > 0) result["buckets"] = J.A(g.GroupBy(r => (r.Start - _window.StartNs) / ((ulong)step * 1_000_000)).OrderBy(b => b.Key).Select(b => J.O(("start_time", J.Time(_window.StartNs + b.Key * (ulong)step * 1_000_000)), ("measures", Measure(b)))));
            return (Group: result, Size: g.Count());
        }).ToList();
        groups = descending
            ? [.. groups.OrderByDescending(g => J.Number(g.Group["measures"], order)).ThenBy(g => g.Group["key"]!.ToJsonString(), StringComparer.Ordinal)]
            : [.. groups.OrderBy(g => J.Number(g.Group["measures"], order)).ThenBy(g => g.Group["key"]!.ToJsonString(), StringComparer.Ordinal)];
        // Paging writes other from these sizes for the groups after the returned page.
        _groupSizes = [.. groups.Select(static g => g.Size)];
        var units = new JsonObject(); foreach (var m in measures) units[m] = m.StartsWith("duration", StringComparison.Ordinal) ? "ms" : m == "error_rate" ? "1" : signal == "spans" ? "spans" : "logs";
        return J.O(("groups", J.A(groups.Select(static g => g.Group))), ("units", units), ("coverage", Coverage(signal == "logs" ? _logs.Count : _spans.Count, rows.Count, groups.Count, false)));
    }
    private static string AttributeType(JsonNode? value) => value?.GetValueKind() switch
    {
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Number => long.TryParse(value.ToJsonString(), CultureInfo.InvariantCulture, out _) ? "int" : "double",
        JsonValueKind.String => "string", JsonValueKind.Array => "array", JsonValueKind.Object => "object", _ => "null"
    };
    private JsonObject Discover(JsonObject input)
    {
        var signal = J.String(input, "signal") ?? "spans"; var rows = Select(input, signal == "logs" ? "logs" : "spans");
        var attrs = rows.SelectMany(row => (signal == "resources" ? new[] { (Scope: "resource", Map: row.Resource) } : new[] { (Scope: signal == "logs" ? "log" : "span", Map: row.Attributes), (Scope: "resource", Map: row.Resource) })
            .SelectMany(map => map.Map.Select(p => (p.Key, map.Scope, p.Value)))).Where(p => (J.String(input, "key_prefix") is not { } prefix || p.Key.StartsWith(prefix, StringComparison.Ordinal)) && (J.String(input, "key") is not { } key || key == p.Key))
            .GroupBy(p => (p.Key, p.Scope)).Select(g =>
            {
                var values = g.GroupBy(p => p.Value?.ToJsonString() ?? "null").OrderByDescending(v => v.Count()).ThenBy(v => v.Key, StringComparer.Ordinal).ToList();
                return J.O(("key", g.Key.Key), ("scope", g.Key.Scope), ("type", AttributeType(g.First().Value)),
                    ("coverage_share", g.Count() / (double)Math.Max(1, rows.Count)), ("cardinality", values.Count),
                    ("top_values", J.A(values.Take(10).Select(v => J.O(("value", ContractValue(v.First().Value)), ("count", v.Count()), ("share", v.Count() / (double)Math.Max(1, rows.Count)))))), ("semconv", SemanticMetadata.Find(g.Key.Key)));
            }).OrderBy(g => J.String(g, "key"), StringComparer.Ordinal).ThenBy(g => J.String(g, "scope"), StringComparer.Ordinal).ToList();
        return J.O(("attributes", J.A(attrs)), ("coverage", Coverage(_spans.Count + _logs.Count, rows.Count, attrs.Count, false)));
    }
    private static string ReleaseOf(AnalysisRecord s) => J.Text(s.Value(CollectorSemanticAttributeCatalog.ServiceVersion, "resource"));
    private static string EnvironmentOf(AnalysisRecord s) => J.Text(s.Value(CollectorSemanticAttributeCatalog.DeploymentEnvironmentName, "resource"));
    private List<JsonObject> ReleaseList(List<AnalysisRecord> rows)
    {
        // A release runs in one environment: its cohorts, traffic share and currency come from that
        // environment's spans of its service.
        var byDeployment = rows.ToLookup(s => (s.Service, Env: EnvironmentOf(s)));
        return
        [
            .. rows.Where(s => ReleaseOf(s).Length > 0)
                .GroupBy(s => (s.Service, Version: ReleaseOf(s), Env: EnvironmentOf(s)))
                .Select(g =>
                {
                    var first = g.Min(s => s.Start);
                    var last = g.Max(s => s.Start);
                    var serviceRows = byDeployment[(g.Key.Service, g.Key.Env)].ToList();
                    var before = serviceRows.Where(s => s.Start < first).ToList();
                    var after = serviceRows.Where(s => s.Start >= first && ReleaseOf(s) == g.Key.Version).ToList();
                    var previousFailures =
                        before.Where(s => s.Error).Select(Signature).ToHashSet(StringComparer.Ordinal);
                    var failures = after.Where(s => s.Error).GroupBy(Signature)
                        .Where(f => !previousFailures.Contains(f.Key)).Select(f => FailureId(f.Key));
                    var concurrent = serviceRows.Any(s => ReleaseOf(s) != g.Key.Version && s.Start >= first && s.Start <= last);
                    return J.O(("service", g.Key.Service), ("environment", g.Key.Env is { Length: > 0 } env ? env : null), ("version", g.Key.Version),
                        ("revision",
                            g.Select(s =>
                                    J.Text(s.Value(CollectorSemanticAttributeCatalog.VcsRefHeadRevision, "resource")))
                                .FirstOrDefault(s => s.Length > 0)),
                        ("first_seen", J.Time(first)), ("last_seen", J.Time(last)),
                        ("current", last == serviceRows.Max(s => s.Start) || concurrent), ("concurrent", concurrent),
                        ("traffic_share", g.Count() / (double)serviceRows.Count),
                        ("before", Perf(before)), ("after", Perf(after)), ("new_failure_ids", failures));

                    JsonObject Perf(List<AnalysisRecord> items) =>
                        J.O(("error_rate", items.Count is 0 ? null : items.Count(s => s.Error) / (double)items.Count),
                            ("p95_ms", P95Ms(items)));
                }).OrderByDescending(r => J.String(r, "first_seen"), StringComparer.Ordinal)
        ];
    }
    private JsonObject Releases(JsonObject input) => Page(input, "releases", ReleaseList(Select(input)));
    // A release's and a service map edge's or node's p95 duration in milliseconds, rounded to the microsecond.
    private static double P95Ms(IEnumerable<AnalysisRecord> rows) =>
        Math.Round(J.Percentile(rows.Select(static r => r.Duration), .95), 3, MidpointRounding.AwayFromZero);
    private JsonObject ServiceMap(JsonObject input)
    {
        var mapInput = input.DeepClone().AsObject(); mapInput.Remove("service_name");
        var rows = Select(mapInput); var edges = new List<(string From, string To, AnalysisRecord Span)>(); var kinds = new Dictionary<string, string>();
        // A client span whose callee reports its own span already has its edge through that span.
        var calledThrough = rows.Where(s => SpanIndex.GetValueOrDefault((s.TraceId, s.Span?.ParentSpanId ?? "")) is { } parent && parent.Service != s.Service)
            .Select(static s => (s.TraceId, s.Span?.ParentSpanId)).ToHashSet();
        foreach (var s in rows)
        {
            ct.ThrowIfCancellationRequested();
            kinds[s.Service] = "service";
            var parent = SpanIndex.GetValueOrDefault((s.TraceId, s.Span?.ParentSpanId ?? ""));
            if (parent is not null && parent.Service != s.Service) edges.Add((parent.Service, s.Service, s));
            if (J.Bool(input, "include_external", true) && s.Span?.Kind is 3 or 4 or 5 && !calledThrough.Contains((s.TraceId, s.SpanId)))
            {
                var peer = J.Text(s.Value(CollectorSemanticAttributeCatalog.ServicePeerName)); var server = J.Text(s.Value(CollectorSemanticAttributeCatalog.ServerAddress));
                var db = J.Text(s.Value(CollectorSemanticAttributeCatalog.DbSystemName)); var messaging = J.Text(s.Value(CollectorSemanticAttributeCatalog.MessagingSystem));
                var target = peer.Length > 0 ? peer : server.Length > 0 ? server : db.Length > 0 ? db : messaging;
                if (target.Length > 0 && target != s.Service)
                {
                    if (kinds.GetValueOrDefault(target) is not "service") kinds[target] = db.Length > 0 ? "database" : messaging.Length > 0 ? "messaging" : "external";
                    edges.Add((s.Service, target, s));
                }
            }
        }
        if (J.String(input, "service_name") is { } focus)
        {
            var names = new HashSet<string> { focus }; var depth = (int)J.Number(input, "depth", 1);
            for (var i = 0; i < depth; i++) { var next = edges.Where(e => names.Contains(e.From) || names.Contains(e.To)).SelectMany(e => new[] { e.From, e.To }).ToList(); names.UnionWith(next); }
            edges = [.. edges.Where(e => names.Contains(e.From) && names.Contains(e.To))]; kinds = kinds.Where(p => names.Contains(p.Key)).ToDictionary();
        }
        var grouped = edges.GroupBy(e => (e.From, e.To));
        var resultEdges = grouped.Select(g => J.O(("from", g.Key.From), ("to", g.Key.To), ("calls", g.Count()), ("error_rate", g.Count(e => e.Span.Error) / (double)g.Count()), ("p95_ms", P95Ms(g.Select(e => e.Span))), ("example_operations", g.Select(e => e.Span.Name).Distinct().Take(3)), ("unit", "spans")));
        var serviceRowsByName = rows.ToLookup(s => s.Service, StringComparer.Ordinal);
        var incomingByName = edges.ToLookup(e => e.To, StringComparer.Ordinal);
        var nodes = kinds.Select(k => { var nodeRows = serviceRowsByName[k.Key].Concat(k.Value == "service" ? [] : incomingByName[k.Key].Select(e => e.Span)).ToList(); return J.O(("name", k.Key), ("kind", k.Value), ("calls", nodeRows.Count), ("error_rate", nodeRows.Count is 0 ? 0 : nodeRows.Count(s => s.Error) / (double)nodeRows.Count), ("p95_ms", P95Ms(nodeRows)), ("unit", "spans")); });
        return J.O(("nodes", J.A(nodes)), ("edges", J.A(resultEdges)), ("unknown_callers", rows.Count is 0 ? 0 : rows.Count(s => s.Span?.ParentSpanId is { Length: > 0 } p && !SpanIndex.ContainsKey((s.TraceId, p))) / (double)rows.Count));
    }
    private JsonObject Health(JsonObject input)
    {
        // A service that sent nothing in the window or its baseline is unknown here, not healthy.
        if (J.String(input, "service_name") is { } service && _spans.All(s => s.Service != service) && _logs.All(l => l.Service != service) && snapshot.Series.All(s => s.ServiceName != service))
            throw new AnalysisInputException("service_name", $"Service {service} sent no telemetry in this window or its baseline; get_service_map lists the services that did.", "service.not_found") { Missing = service };
        var rows = Select(input);
        var orphans = rows.Where(s => s.Span?.ParentSpanId is { Length: > 0 } p && !SpanIndex.ContainsKey((s.TraceId, p)) && !HasRemoteParent(s)).ToList();
        // How far back the read reaches for the services, environment and matchers the rest of the report covers.
        var oldest = _spans.Concat(_logs).Where(r => InScope(input, r)).Select(static r => r.Start).DefaultIfEmpty().Min();
        // Ingestion lag runs from a producer's observation to the Collector's receipt. A record whose producer set
        // no observed time carries its receipt there, so it says nothing about lag.
        var lag = Select(input, "logs").Select(static l => l.Log!)
            .Where(static l => l.ReceivedTimeUnixNano is { } received && l.ObservedTimeUnixNano is { } observed && observed is > 0 && observed < received)
            .Select(static l => (l.ReceivedTimeUnixNano!.Value - l.ObservedTimeUnixNano!.Value) / 1_000_000d).ToList();
        // A conflicting point keeps its first measurement; query_metric still refuses to aggregate it.
        var series = snapshot.Series.ToDictionary(static s => s.SeriesId, StringComparer.Ordinal);
        var conflicts = snapshot.Points.Where(p => p.HasConflict != 0 && _window.Contains(p.TimeUnixNano)).GroupBy(static p => p.SeriesId)
            .Select(g => (Series: series.GetValueOrDefault(g.Key), Times: g.Select(static p => p.TimeUnixNano).ToList()))
            .Where(c => c.Series is not null && InScope(input, SeriesRecord(c.Series)))
            .OrderBy(c => c.Series!.MetricName, StringComparer.Ordinal).ThenBy(c => c.Series!.SeriesId, StringComparer.Ordinal).ToList();
        var metricConflicts = J.O(("points", conflicts.Sum(static c => c.Times.Count)), ("unit", "points"), ("series", J.A(conflicts.Select(c => J.O(
            ("metric_name", c.Series!.MetricName), ("series_id", c.Series.SeriesId), ("service", c.Series.ServiceName ?? "unknown_service"),
            ("points", c.Times.Count), ("first_time", J.Time(c.Times.Min())), ("last_time", J.Time(c.Times.Max())))))));
        return J.O(("silent_services", Silent(input)), ("metric_conflicts", metricConflicts), ("orphan_spans", J.O(("count", orphans.Count), ("share", rows.Count is 0 ? 0 : orphans.Count / (double)rows.Count), ("services", orphans.Select(s => s.Service).Distinct()), ("unit", "spans"))),
            ("semconv_gaps", SemanticGaps(rows)), ("unknown_service_share", rows.Count is 0 ? 0 : rows.Count(s => s.Service.StartsWith("unknown", StringComparison.Ordinal)) / (double)rows.Count),
            ("schema_urls", rows.Select(s => s.Span?.SchemaUrl).OfType<string>().Where(static url => url.Length > 0).Distinct()), ("data_span", J.O(("oldest", oldest is 0 ? null : J.Time(oldest)), ("days", oldest is 0 ? null : Age(oldest).TotalDays))),
            ("ingestion_lag_ms", lag.Count is 0 ? null : lag.Average()));
    }
    // OTLP span flags bits 8 and 9: the producer knew whether the parent was remote, and it was. A remote parent
    // runs in the calling process, which need not report to nami, so its absence does not orphan the span.
    // https://github.com/open-telemetry/opentelemetry-proto/blob/v1.11.1/opentelemetry/proto/trace/v1/trace.proto
    private const uint RemoteParent = (uint)(SpanFlags.ContextHasIsRemoteMask | SpanFlags.ContextIsRemoteMask);
    private static bool HasRemoteParent(AnalysisRecord s) => ((s.Span?.Flags ?? 0) & RemoteParent) == RemoteParent;
    private static JsonArray SemanticGaps(List<AnalysisRecord> rows)
    {
        var gaps = new List<JsonObject>();
        foreach (var rule in SemanticMetadata.SpanRequirements.OfType<JsonObject>())
        {
            var type = J.String(rule, "type")!; var separator = type.LastIndexOf('.');
            var prefix = separator > 0 ? type[..separator] : type;
            var kind = J.String(rule, "kind") switch { "internal" => 1, "server" => 2, "client" => 3, "producer" => 4, "consumer" => 5, _ => 0 };
            foreach (var group in rows.Where(row => row.Span?.Kind == kind && row.Attributes.Any(a => a.Key.StartsWith(prefix + ".", StringComparison.Ordinal))).GroupBy(row => (row.Service, row.Name)))
                // An attribute the collector does not persist is absent whether or not the producer sent it.
                foreach (var required in rule["required"]!.AsArray().Select(J.Text).Where(AttributeKeySets.ShouldCaptureSpanAttribute))
                {
                    var missing = group.Count(row => row.Value(required) is null);
                    if (missing > 0) gaps.Add(J.O(("service", group.Key.Service), ("operation", group.Key.Name), ("attribute", required), ("requirement_level", "required"), ("missing_share", missing / (double)group.Count())));
                }
        }
        return J.A(gaps.DistinctBy(g => g.ToJsonString()));
    }
    private static string McpFailureClass(AnalysisRecord s)
    {
        var error = (J.Text(s.Value(CollectorSemanticAttributeCatalog.ErrorType)) + " " + s.Span?.StatusMessage).ToLowerInvariant();
        return error.Contains("timeout", StringComparison.Ordinal) ? "timeout" : error.Contains("cancel", StringComparison.Ordinal) ? "cancellation" : error.Contains("auth", StringComparison.Ordinal) || error.Contains("permission", StringComparison.Ordinal) ? "authentication" : error.Contains("transport", StringComparison.Ordinal) || error.Contains("connection", StringComparison.Ordinal) ? "transport" : J.Text(s.Value("mcp.response.is_error")) == "true" || J.Text(s.Value("mcp.is_error")) == "true" ? "tool_result" : "protocol";
    }
    private static string McpServer(AnalysisRecord s) => J.Text(s.Value("mcp.server.name")) is { Length: > 0 } value ? value : s.Service;
    private static string McpTransport(AnalysisRecord s) => J.Text(s.Value("mcp.transport")) is { Length: > 0 } value ? value : J.Text(s.Value(CollectorSemanticAttributeCatalog.NetworkTransport));
    private List<AnalysisRecord> McpRows(JsonObject input)
    {
        return
        [
            .. Select(input).Where(s => s.Value(CollectorSemanticAttributeCatalog.McpMethodName) is not null
                                        && (J.String(input, "server_name") is not { } server ||
                                            J.Text(s.Value("mcp.server.name")) == server || s.Service == server)
                                        && (J.String(input, "server_version") is not { } version ||
                                            J.Text(s.Value("mcp.server.version")) == version || ReleaseOf(s) == version)
                                        && (J.String(input, "tool_name") is not { } tool ||
                                            J.Text(s.Value(CollectorSemanticAttributeCatalog.GenAiToolName)) == tool)
                                        && (J.String(input, "transport") is not { } transport ||
                                            J.Text(s.Value("mcp.transport")) == transport ||
                                            J.Text(s.Value(CollectorSemanticAttributeCatalog.NetworkTransport)) ==
                                            transport))
        ];
    }
    private JsonObject McpStats(JsonObject input)
    {
        var rows = McpRows(input);
        var output = McpTraffic(input, rows);
        // On request, the window's 60 buckets that saw requests; they, the breakdowns and the per-tool
        // statistics come from the same spans as the totals, and a comparison window carries only its traffic.
        if (J.Bool(input, "buckets"))
        {
            var bucketMs = Math.Max(1000L, (long)(_window.End - _window.Start).TotalMilliseconds / 60);
            var bucketNs = (ulong)bucketMs * 1_000_000;
            var buckets = new (int Requests, int Errors)[(int)((_window.EndNs - _window.StartNs + bucketNs - 1) / bucketNs)];
            foreach (var span in rows)
            {
                ref var bucket = ref buckets[(span.Start - _window.StartNs) / bucketNs];
                bucket.Requests++;
                if (span.Error) bucket.Errors++;
            }
            output["bucket_ms"] = bucketMs;
            output["buckets"] = J.A(buckets.Select((bucket, index) => (bucket, index)).Where(static b => b.bucket.Requests > 0)
                .Select(b => J.O(("start", _window.Start.AddMilliseconds(b.index * bucketMs).ToString("O")), ("requests", b.bucket.Requests), ("errors", b.bucket.Errors))));
        }
        output["by_server"] = RequestCounts(rows, McpServer);
        output["by_transport"] = RequestCounts(rows, McpTransport);
        output["by_method"] = RequestCounts(rows, static s => J.Text(s.Value(CollectorSemanticAttributeCatalog.McpMethodName)));
        output["tools"] = J.A(rows.GroupBy(static s => J.Text(s.Value(CollectorSemanticAttributeCatalog.GenAiToolName))).Where(static g => g.Key.Length > 0)
            .OrderByDescending(static g => g.Count()).ThenBy(static g => g.Key, StringComparer.Ordinal)
            .Select(static g =>
            {
                var errors = g.Count(static s => s.Error);
                return J.O(("name", g.Key), ("requests", g.Count()), ("errors", errors), ("error_rate", errors / (double)g.Count()),
                    ("avg_ms", g.Average(static s => s.Duration)), ("p95_ms", J.Percentile(g.Select(static s => s.Duration), .95)));
            }));
        if (input["baseline"] is { } baseline)
        {
            // Another server version is compared over the same window, an earlier window with the same filters.
            var copy = input.DeepClone().AsObject(); copy.Remove("baseline");
            var current = _window;
            if (J.String(baseline, "server_version") is { } version) copy["server_version"] = version;
            else _window = Baseline(input);
            var old = McpTraffic(copy, McpRows(copy)); old.Remove("coverage"); old["window"] = _window.Json; output["comparison"] = old; _window = current;
        }
        return output;
    }
    private JsonObject McpTraffic(JsonObject input, List<AnalysisRecord> rows)
    {
        static JsonObject Totals(List<AnalysisRecord> items) => J.O(("requests", items.Count), ("errors", items.Count(s => s.Error)), ("error_rate", items.Count is 0 ? 0 : items.Count(s => s.Error) / (double)items.Count), ("p50_ms", J.Percentile(items.Select(s => s.Duration), .50)), ("p95_ms", J.Percentile(items.Select(s => s.Duration), .95)), ("unit", "requests"));
        var groupBy = (input["group_by"] as JsonArray)?.Select(J.Text).FirstOrDefault() ?? "tool";
        var groups = rows.GroupBy(s => groupBy switch { "server" => McpServer(s), "method" => J.Text(s.Value(CollectorSemanticAttributeCatalog.McpMethodName)), "transport" => McpTransport(s), "failure_class" => s.Error ? McpFailureClass(s) : "success", _ => J.Text(s.Value(CollectorSemanticAttributeCatalog.GenAiToolName)) })
            .OrderByDescending(g => g.Count()).Select(g => { var result = Totals([.. g]); result["name"] = g.Key; result["example_trace_ids"] = J.N(g.OrderByDescending(s => s.Error).ThenByDescending(s => s.Duration).Select(s => s.TraceId).OfType<string>().Distinct().Take(3)); return result; }).ToList();
        var classes = new JsonObject(); foreach (var kind in new[] { "transport", "protocol", "tool_result", "authentication", "cancellation", "timeout" }) classes[kind] = rows.Count(s => s.Error && McpFailureClass(s) == kind);
        return J.O(("totals", Totals(rows)), ("groups", J.A(groups)), ("failure_classes", classes), ("coverage", Coverage(_spans.Count, rows.Count, groups.Count, false)));
    }
    private static JsonArray RequestCounts(List<AnalysisRecord> rows, Func<AnalysisRecord, string> name) =>
        J.A(rows.GroupBy(name).OrderByDescending(static g => g.Count()).ThenBy(static g => g.Key, StringComparer.Ordinal)
            .Select(static g => J.O(("name", g.Key), ("requests", g.Count()))));
}
