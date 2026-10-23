using System.Text.Json.Nodes;
using Nami.Collector.Storage;

namespace Nami.Collector.Analysis;

internal static class AnalysisReadPlan
{
    public static AnalysisReadQuery Resolve(string operation, JsonObject input, DateTimeOffset now, AnalysisHandles handles, string project)
    {
        var window = AnalysisWindow.Resolve(input, now, operation == "list_releases" ? 10080 : 60);
        if ((J.String(input, "cursor") ?? J.String(input, "phase_cursor")) is { } cursor && handles.Read(cursor, project, "cursor")["window"] is JsonObject saved)
            window = AnalysisWindow.Resolve(saved, now);
        var start = window.Start - (window.End - window.Start); var end = window.End;
        void Include(JsonNode? node)
        {
            if (node is not JsonObject obj) return;
            if (J.Date(obj, "start_time") is { } from && from < start) start = from;
            if (J.Date(obj, "end_time") is { } to && to > end) end = to;
            var offset = J.Number(obj, "offset_minutes");
            if (offset > 0 && window.Start.AddMinutes(-offset) < start) start = window.Start.AddMinutes(-offset);
        }
        Include(input["baseline"]); Include(input["group_a"]?["window"]);
        if (input["group_b"] is JsonObject group) Include(group["window"]);
        // group_b previous_window is the window of equal length that precedes group_a's.
        if (J.Text(input["group_b"]) == "previous_window" && input["group_a"]?["window"] is JsonObject cohort
            && J.Date(cohort, "start_time") is { } from && J.Date(cohort, "end_time") is { } to && from - (to - from) < start) start = from - (to - from);
        // A verification's before window is the lookback ahead of its change.
        if (J.Date(input, "since") is { } since && since - (window.End - window.Start) < start) start = since - (window.End - window.Start);
        // A verification's after window runs until now. A repeat verifies the request its verify_id recorded, and a
        // release without a since time is the change the store anchors, the first span of that version per service
        // over every retained span; the before window ahead of it may begin anywhere in retention.
        if (operation == "verify_fix" && now > end) end = now;
        var verification = operation == "verify_fix" && J.String(input, "verify_id") is { } repeat ? handles.Read(repeat, project, "verify_id") : input;
        var release = operation == "verify_fix" && J.Date(verification, "since") is null ? J.String(verification, "release") : null;
        if (release is not null) start = DateTimeOffset.UnixEpoch;
        var traces = new HashSet<string>(StringComparer.Ordinal); var sessions = new HashSet<string>(StringComparer.Ordinal);
        void Target(JsonNode? target)
        {
            if (J.String(target, "trace_id") is { } trace) traces.Add(trace);
            if (J.String(target, "session_id") is { } session) sessions.Add(session);
            if (J.String(target, "ci_run_id") is { } ci) sessions.Add(ci);
        }
        Target(input); Target(input["a"]); Target(input["b"]);
        if (operation == "ci_log" && J.String(input["baseline"], "ci_run_id") is { } priorRun) sessions.Add(priorRun);
        // Failure identity is independent of time: a verification's before window runs from the failure's first
        // retained occurrence, and get_failure_context reads a failure as the trace of its latest occurrence, which
        // the request then names. These reads may span retention, but still have the storage cap and report
        // incomplete coverage instead of false totals. list_traces keeps its window: there failure_id filters the
        // window's traces.
        if (operation == "verify_fix" && (J.String(input, "failure_id") ?? J.String(input, "verify_id")) is not null
            || operation == "get_failure_context" && J.String(input, "failure_id") is not null && J.String(input, "trace_id") is null)
            start = DateTimeOffset.UnixEpoch;
        if (start < DateTimeOffset.UnixEpoch) start = DateTimeOffset.UnixEpoch;
        return new AnalysisReadQuery
        {
            Tables = Tables(operation, input),
            Start = NamiTimeConversions.ToUnixNanoUnsigned(start), End = NamiTimeConversions.ToUnixNanoUnsigned(end),
            TraceIds = [.. traces], SessionIds = [.. sessions],
            IncludeWindowWithTargets = operation is "get_failure_context" or "ci_log",
            IncludeLinkTargets = operation == "get_trace",
            IncludeMetricPredecessors = operation is "query_metric" or "run_saved_query",
            MetricNames = operation is "query_metric" or "run_saved_query" ? MetricNames(input) : [],
            ConflictingPointsOnly = operation == "check_telemetry_health",
            Release = release
        };
    }

    // The instruments a metric answer measures: its metric and a ratio's denominator.
    private static string[] MetricNames(JsonObject input) =>
        [.. new[] { J.String(input, "metric_name"), J.String(input["ratio_of"], "metric_name") }.OfType<string>().Distinct(StringComparer.Ordinal)];

    // The tables the operation's answer comes from, so that a table it never looks at cannot make its read incomplete.
    private static AnalysisTables Tables(string operation, JsonObject input) => operation switch
    {
        "search_logs" => AnalysisTables.Logs,
        "list_metrics" => AnalysisTables.MetricSeries,
        "query_metric" => AnalysisTables.MetricSeries | AnalysisTables.MetricPoints,
        "check_telemetry_health" => AnalysisTables.Spans | AnalysisTables.Logs | AnalysisTables.MetricSeries | AnalysisTables.MetricPoints,
        "get_failure_context" or "get_trace" or "ci_log" => AnalysisTables.Spans | AnalysisTables.Logs,
        "aggregate_telemetry" or "compare_groups" or "discover_attributes" => J.String(input, "signal") == "logs" ? AnalysisTables.Logs : AnalysisTables.Spans,
        // Without an id the saved queries are listed; with one, the run reads its saved filter's signal.
        "run_saved_query" => AnalysisTables.SavedQueries | (J.String(input, "id") is null ? AnalysisTables.None : J.String(input, "signal") switch
        {
            "metrics" => AnalysisTables.MetricSeries | AnalysisTables.MetricPoints,
            "logs" => AnalysisTables.Logs,
            _ => AnalysisTables.Spans
        }),
        _ => AnalysisTables.Spans
    };
}
