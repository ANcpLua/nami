using System.Text.Json.Nodes;
using Nami.Api.Contracts.Mcp.Tools;
using Nami.Collector.Storage;

namespace Nami.Collector.Analysis;

internal sealed partial class AnalysisEngine(AnalysisSnapshot snapshot, string project, AnalysisHandles handles, DateTimeOffset now, CancellationToken ct = default)
{
    private readonly List<AnalysisRecord> _spans =
    [
        .. snapshot.Spans.Select(s =>
        {
            ct.ThrowIfCancellationRequested();
            return AnalysisRecord.From(s);
        }).OrderBy(static s => s.Start).ThenBy(static s => s.Id, StringComparer.Ordinal)
    ];
    private readonly List<AnalysisRecord> _logs =
    [
        .. snapshot.Logs.Select(s =>
        {
            ct.ThrowIfCancellationRequested();
            return AnalysisRecord.From(s);
        }).OrderBy(static s => s.Start).ThenBy(static s => s.Id, StringComparer.Ordinal)
    ];
    private Dictionary<(string? Trace, string Id), AnalysisRecord> SpanIndex => field ??= _spans.ToDictionary(static s => (s.TraceId, s.Id));
    private readonly Dictionary<(string? Trace, string Id), int> _depths = [];
    private readonly Dictionary<string, AnalysisStateRow> _state = snapshot.State.ToDictionary(static s => s.StateKey, StringComparer.Ordinal);
    private ILookup<string, MetricPointRow> PointsBySeries => field ??= snapshot.Points.ToLookup(static p => p.SeriesId, StringComparer.Ordinal);
    private AnalysisWindow _window;
    private string _operation = "";

    // How long before the engine's now a Unix-nanosecond time lies.
    private TimeSpan Age(ulong unixNano) => now - J.Instant(unixNano);

    public JsonObject Run(string operation, JsonObject input)
    {
        ct.ThrowIfCancellationRequested();
        _operation = operation;
        _window = AnalysisWindow.Resolve(input, now, operation == "list_releases" ? 10080 : 60);
        if ((J.String(input, "cursor") ?? J.String(input, "phase_cursor")) is { } continuation)
        {
            var state = handles.Read(continuation, project, "cursor");
            if (state["window"] is JsonObject window) _window = AnalysisWindow.Resolve(window, now);
        }
        AnalysisMatchers.Validate(input["matchers"]);
        var result = operation switch
        {
            "triage" => Triage(input), "get_failure_context" => FailureContext(input),
            "compare_groups" => CompareGroups(input), "compare_traces" => CompareTraces(input), "verify_fix" => VerifyFix(input),
            "aggregate_telemetry" => Aggregate(input), "discover_attributes" => Discover(input), "get_agent_run" => AgentRun(input),
            "list_releases" => Releases(input), "get_service_map" => ServiceMap(input), "check_telemetry_health" => Health(input),
            "get_mcp_stats" => McpStats(input), "list_sessions" => Sessions(input),
            "list_traces" => Traces(input), "get_trace" => Trace(input), "search_logs" => Logs(input),
            "list_metrics" => Metrics(input), "query_metric" => QueryMetric(input),
            "ci_log" => Ci(input), "run_saved_query" => RunSavedQuery(input), _ => throw new AnalysisInputException("tool", "Unknown analysis operation.")
        };
        // A verification's before and after windows bound it; the request's window would only mislead.
        if (operation != "verify_fix") result["window"] ??= _window.Json;
        result["coverage"] ??= Coverage(Select(input).Count + Select(input, "logs").Count, Select(input).Count + Select(input, "logs").Count, 1, false);
        var paged = PageCollections(input, result);
        foreach (var logs in new[] { paged["logs"] as JsonArray, paged["observed"]?["logs"] as JsonArray })
            if (logs is not null) foreach (var log in logs.OfType<JsonObject>()) log.Remove("log_id");
        return paged;
    }

    // scan_complete says whether the read held every record the answer could come from, truncated whether the answer
    // left matching results out; a matched count is unknown only when the read was cut.
    private JsonObject Coverage(int examined, int matched, int returned, bool truncated) => J.O(
        ("examined", examined), ("matched", snapshot.ScanComplete ? matched : null), ("returned", returned), ("truncated", truncated),
        ("scan_complete", snapshot.ScanComplete), ("as_of", _window.End.ToString("O")));
    // Spans starting in [from, to) are all in the snapshot: a store read reports the range it covered, any
    // other snapshot only when its scan was complete.
    private bool SpansComplete(ulong from, ulong to) => snapshot.SpanCoverage is { } coverage ? coverage.Covers(from, to) : snapshot.ScanComplete;
    private List<AnalysisRecord> Select(JsonObject input, string signal = "spans", AnalysisWindow? window = null)
    {
        var w = window ?? _window;
        return [.. (signal == "logs" ? _logs : _spans).Where(r => Checked(r) && w.Contains(r.Start) && InScope(input, r))];
    }
    // The request's service, environment, release and matcher filters. nami's own MCP server exports its spans into the
    // project it reads as service nami.mcp: its failures, releases and pauses are not the caller's, so triage and
    // check_telemetry_health leave that service out unless service_name names it.
    private bool InScope(JsonObject input, AnalysisRecord r) =>
        (J.String(input, "service_name") is { } service ? r.Service == service : r.Service != NamiServiceValues.Mcp || _operation is not ("triage" or "check_telemetry_health"))
        && (J.String(input, "environment") is not { } environment
            || J.Text(r.Value(CollectorSemanticAttributeCatalog.DeploymentEnvironmentName, "resource")) == environment
            || J.Text(r.Value(CollectorSemanticAttributeCatalog.DeploymentEnvironment, "resource")) == environment)
        && (J.String(input, "release") is not { } version || ReleaseOf(r) == version)
        && AnalysisMatchers.Match(r, input["matchers"]);
    private bool Checked(AnalysisRecord _) { ct.ThrowIfCancellationRequested(); return true; }
    private AnalysisWindow Baseline(JsonObject input)
    {
        if (input["baseline"] is JsonObject custom)
        {
            if (custom["offset_minutes"] is not null) { var offset = J.Number(custom, "offset_minutes"); return new(_window.Start.AddMinutes(-offset), _window.End.AddMinutes(-offset)); }
            return AnalysisWindow.Resolve(custom, _window.Start);
        }
        return new(_window.Start - (_window.End - _window.Start), _window.Start);
    }
    private static string FailureKind(AnalysisRecord s) => J.Text(s.Value(CollectorSemanticAttributeCatalog.RecordedFailureKind)) is { Length: > 0 } ex ? ex : J.Text(s.Value(CollectorSemanticAttributeCatalog.ErrorType));
    private static string LogBody(AnalysisRecord s) => ContractJson.ParseLogBody(s.Log?.Body) switch
    {
        Api.Contracts.Common.AttributeValue.StringValue text => text.Value,
        null => "",
        var value => JsonSerializer.Serialize(value, global::Nami.Api.Contracts.ContractTypes.TypeInfo<global::Nami.Api.Contracts.Common.AttributeValue>())
    };
    private static string Message(AnalysisRecord s) => s.Span?.StatusMessage ?? LogBody(s);
    private static string Operation(AnalysisRecord s) => J.Text(s.Value(CollectorSemanticAttributeCatalog.HttpRoute)) is { Length: > 0 } route ? route : s.Name;
    private static string Signature(AnalysisRecord s) => string.Join("|", s.Service, Operation(s), FailureKind(s), J.Normalize(Message(s)));
    private string FailureId(string signature) => FailureIdentity(project, signature);
    private static string FailureIdentity(string projectId, string signature) => "fail_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(projectId + "\0" + signature)));
    internal static string? FailureIdentity(string projectId, SpanStorageRow row) => row.StatusCode == 2 ? FailureIdentity(projectId, Signature(AnalysisRecord.From(row))) : null;
    private JsonObject State(string id) => _state.GetValueOrDefault(id) is { } row
        ? JsonNode.Parse(row.ValueJson)!.AsObject() : J.O(("status", "open"));
    private string Etag(string id) => _state.GetValueOrDefault(id)?.Etag ?? '"' + id + '"';
    private List<AnalysisRecord> FailureRecords(string id) =>
        [.. _spans.Where(s => s.Error && FailureId(Signature(s)) == id)];
    public bool OwnsFailure(string id) => FailureRecords(id).Count > 0;
    // The trace of a failure's latest occurrence, which get_failure_context examines.
    public string? LatestOccurrenceTrace(string id) => FailureRecords(id).OrderByDescending(static s => s.Start).FirstOrDefault()?.TraceId;
    internal static AnalysisInputException NoFailure(string id) =>
        new("failure_id", $"Failure {id} was not found in this project; triage lists the failures.", "failure.not_found") { Missing = id };
    // A failure over every occurrence the snapshot holds, as update_failure_status returns it.
    public JsonObject FailureSummary(string id) => Failure(FailureRecords(id).GroupBy(Signature).Single());
    private JsonObject Failure(IGrouping<string, AnalysisRecord> group)
    {
        var records = group.ToList(); var first = records[0]; var id = FailureId(group.Key); var state = State(id);
        var notes = snapshot.Annotations.Count(n => AnnotationTargetsFailure(n, id, records));
        return J.O(("failure_id", id), ("etag", Etag(id)), ("exception_type", NonEmpty(FailureKind(first))),
            ("normalized_message", NonEmpty(J.Normalize(Message(first)))), ("operation", Operation(first)), ("service", first.Service),
            ("first_seen", J.Time(records.Min(s => s.Start))), ("last_seen", J.Time(records.Max(s => s.Start))),
            ("count", records.Count), ("affected_traces", records.Select(s => s.TraceId).Distinct().Count()),
            ("affected_sessions", records.Select(s => s.SessionId).Where(s => s is not null).Distinct().Count()),
            ("releases_seen", records.Select(ReleaseOf).Where(s => s.Length > 0).Distinct()),
            ("status", J.String(state, "status") ?? "open"), ("updated_at", J.String(state, "updated_at") ?? J.Time(records.Min(s => s.Start))), ("ignore_until", state["ignore_until"]), ("assignee", state["assignee"]),
            ("annotation_count", notes), ("example_trace_ids", records.Select(s => s.TraceId).OfType<string>().Distinct().Take(3)));
    }
    private static JsonObject AsTriageFailure(JsonObject failure, string? exampleTraceId)
    {
        failure["example_trace_id"] = exampleTraceId;
        failure.Remove("affected_sessions");
        failure.Remove("releases_seen");
        failure.Remove("updated_at");
        failure.Remove("example_trace_ids");
        return failure;
    }
    private static string? NonEmpty(string value) => value.Length > 0 ? value : null;
    private bool AnnotationTargetsFailure(AnnotationRow note, string id, List<AnalysisRecord> records) => FailureNote(note, id)
        || note.TargetKind switch { "trace" => records.Any(r => r.TraceId == note.TraceId), "span" => records.Any(r => r.SpanId == note.SpanId && r.TraceId == note.TraceId), "session" => records.Any(r => r.SessionId == note.SessionId), _ => false };
    // A note whose target is the failure itself; its failure id lives in the annotation's stored metadata.
    private bool FailureNote(AnnotationRow note, string id) => _state.GetValueOrDefault("annotation:" + note.AnnotationId) is { } metadata
        && J.String(JsonNode.Parse(metadata.ValueJson)?["target"], "failure_id") == id;
    private JsonObject Triage(JsonObject input)
    {
        var current = Select(input); var before = Select(input, window: Baseline(input));
        var releases = ReleaseList([.. current, .. before]);
        var baselineOperations = before.GroupBy(s => (s.Service, Op: Operation(s))).ToDictionary(g => g.Key, g => g.ToList());
        var errors = current.Where(s => s.Error).GroupBy(Signature); var baseline = before.Where(s => s.Error).GroupBy(Signature).ToDictionary(g => g.Key, g => g.Count());
        var listed = J.String(input, "status") ?? "open";
        var failures = errors.Select(g =>
        {
            var f = AsTriageFailure(Failure(g), g.First().TraceId); var id = J.String(f, "failure_id")!; var state = State(id);
            var old = baseline.GetValueOrDefault(g.Key); var count = g.Count(); var status = J.String(state, "status");
            var changedAfter = J.Date(state, "updated_at");
            var change = status == "resolved" && changedAfter.HasValue && g.Any(s => s.Start > NamiTimeConversions.ToUnixNanoUnsigned(changedAfter.Value))
                ? "regressed" : old is 0 ? "new" : count > old ? "worsening" : count < old ? "recovering" : "ongoing";
            f["change"] = change; f["baseline_count"] = old;
            f["coinciding_release"] = CoincidingRelease(g, releases)?.DeepClone();
            f["root_cause_failure_id"] = RootCauseFailure(g);
            return f;
        }).Where(f => listed switch
        {
            "all" => true,
            // Open is what needs attention: a resolved failure that recurred and an ignored one whose ignore_until passed.
            "open" => J.String(f, "status") switch
            {
                "resolved" => J.String(f, "change") == "regressed",
                "ignored" => J.Date(f, "ignore_until") is { } expiry && expiry <= now,
                _ => true
            },
            _ => J.String(f, "status") == listed
        }).OrderByDescending(f => J.Number(f, "count")).ThenBy(f => J.String(f, "failure_id"), StringComparer.Ordinal).ToList();
        var regressions = new List<JsonObject>();
        foreach (var g in current.GroupBy(s => (s.Service, Op: Operation(s))))
        {
            var b = baselineOperations.GetValueOrDefault(g.Key); if (b is null || b.Count < 20 || g.Count() < 20) continue;
            var rate = g.Count(s => s.Error) / (double)g.Count(); var oldRate = b.Count(s => s.Error) / (double)b.Count;
            foreach (var m in new[] { ("error_rate", rate, oldRate), ("p95_ms", J.Percentile(g.Select(s => s.Duration), .95), J.Percentile(b.Select(s => s.Duration), .95)), ("throughput", g.Count(), b.Count) })
                if (m.Item1 == "throughput" ? m.Item2 < m.Item3 * .8 : m.Item1 == "error_rate" ? m.Item2 >= m.Item3 + .05 : m.Item2 > Math.Max(m.Item3 * 1.2, m.Item3 + 10))
                    regressions.Add(J.O(("service", g.Key.Service), ("operation", g.Key.Op), ("measure", m.Item1), ("value", m.Item2), ("baseline_value", m.Item3), ("denominator", g.Count()), ("unit", m.Item1 == "p95_ms" ? "ms" : m.Item1 == "error_rate" ? "1" : "spans")));
        }
        // New failures lead the ranking, so they keep their places on the first page even when their count is low.
        return J.O(("failures", J.A(failures.OrderBy(f => J.String(f, "change") == "new" ? 0 : 1))), ("regressions", J.A(regressions)), ("releases", J.A(releases.Where(r => J.Date(r, "first_seen") >= _window.Start).Select(ReleaseReference))),
            ("silent_services", Silent(input)), ("coverage", Coverage(_spans.Count, current.Count, failures.Count, false)));
    }
    // The release the failing spans run on: their most common version, the later one among equals, when the
    // release was first seen in the window.
    private JsonObject? CoincidingRelease(IEnumerable<AnalysisRecord> failing, List<JsonObject> releases)
    {
        if (failing.Where(static s => ReleaseOf(s).Length > 0).GroupBy(static s => (s.Service, Version: ReleaseOf(s), Env: EnvironmentOf(s)))
                .OrderByDescending(static g => g.Count()).ThenByDescending(static g => g.Max(static s => s.Start)).FirstOrDefault()?.Key is not { } run) return null;
        var release = releases.FirstOrDefault(r => J.String(r, "service") == run.Service && J.String(r, "version") == run.Version && (J.String(r, "environment") ?? "") == run.Env
            && _window.Contains(NamiTimeConversions.ToUnixNanoUnsigned(J.Date(r, "first_seen")!.Value)));
        return release is null ? null : ReleaseReference(release);
    }
    private static JsonObject ReleaseReference(JsonObject release) => J.O(("service", release["service"]), ("version", release["version"]),
        ("first_seen", release["first_seen"]), ("revision", release["revision"]));
    private ILookup<string?, AnalysisRecord> ErrorsByTrace => field ??= _spans.Where(static s => s.Error).ToLookup(static s => s.TraceId);
    // The failure the occurrences propagated from: in each of their traces the earliest error span without a failing
    // child, by majority.
    private string? RootCauseFailure(IEnumerable<AnalysisRecord> occurrences) => occurrences.Select(static s => s.TraceId).Distinct().Select(trace =>
        {
            var errors = ErrorsByTrace[trace].ToList();
            var parents = errors.Select(static s => s.Span?.ParentSpanId).OfType<string>().ToHashSet(StringComparer.Ordinal);
            return errors.Where(s => !parents.Contains(s.Id)).OrderBy(static s => s.Start).ThenBy(static s => s.Id, StringComparer.Ordinal).FirstOrDefault();
        }).OfType<AnalysisRecord>().GroupBy(s => FailureId(Signature(s))).OrderByDescending(static g => g.Count()).ThenBy(static g => g.Key, StringComparer.Ordinal).FirstOrDefault()?.Key;
    // A service is silent when its last span over the baseline and the window is older than the window's end minus
    // 15 minutes or five of its usual intervals between spans, whichever is longer.
    private JsonArray Silent(JsonObject input)
    {
        var baseline = Baseline(input); var minutes = (baseline.End - baseline.Start + (_window.End - _window.Start)).TotalMinutes;
        var end = (double)NamiTimeConversions.ToUnixNanoUnsigned(_window.End);
        return J.A(Select(input, window: baseline).Concat(Select(input)).DistinctBy(static s => (s.TraceId, s.Id)).GroupBy(static s => s.Service)
            .Select(static g =>
            {
                var starts = g.Select(static s => s.Start).Order().ToList();
                var usual = starts.Count > 1 ? (starts[^1] - starts[0]) / (double)(starts.Count - 1) : 0;
                return (Service: g.Key, Last: starts[^1], Count: starts.Count, Quiet: Math.Max(15 * 60_000_000_000d, 5 * usual));
            })
            .Where(s => s.Last < end - s.Quiet).OrderBy(static s => s.Service, StringComparer.Ordinal)
            .Select(s => J.O(("service", s.Service), ("last_seen", J.Time(s.Last)), ("usual_rate", s.Count / minutes), ("unit", "spans/minute"))));
    }
    // An evidence span: what failed, where and when; its message is its status description, its type an exception or error type.
    private static JsonObject SpanJson(AnalysisRecord r) => J.O(("span_id", r.SpanId), ("trace_id", r.TraceId), ("session_id", r.SessionId),
        ("service", r.Service), ("name", r.Name), ("start_time", J.Time(r.Start)), ("duration_ms", r.Duration), ("status", Status(r)),
        ("status_message", r.Span?.StatusMessage is { Length: > 0 } message ? message : null),
        ("parent_span_id", r.Span?.ParentSpanId is { Length: > 0 } parent ? parent : null),
        ("exception_type", NonEmpty(FailureKind(r))), ("key_attributes", ContractAttributes(r.Attributes)));
}
