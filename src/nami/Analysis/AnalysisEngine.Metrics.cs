using System.Text.Json.Nodes;
using Nami.Api.Contracts.Mcp.Tools;
using Nami.Collector.Storage;

namespace Nami.Collector.Analysis;

internal sealed partial class AnalysisEngine
{
    internal static string MetricKind(byte kind) => kind switch { 1 => "gauge", 2 => "sum", 3 => "histogram", 4 => "exponential_histogram", 5 => "summary", _ => throw new InvalidDataException("Unknown persisted metric kind.") };
    internal static AnalysisRecord SeriesRecord(MetricSeriesRow s) => new(s.SeriesId, null, null, null, s.ServiceName ?? "unknown_service", s.MetricName, s.LastSeenUnixNano, s.LastSeenUnixNano, false, 0, J.Attributes(s.AttributesJson), J.Attributes(s.ResourceJson));
    private List<MetricSeriesRow> SeriesRows(JsonObject input) =>
    [
        .. snapshot.Series.Where(s =>
            (J.String(input, "metric_name") is not { } name || s.MetricName == name)
            && (J.String(input, "series_id") is not { } seriesId || s.SeriesId == seriesId)
            && (J.String(input, "name_prefix") is not { } prefix ||
                s.MetricName.StartsWith(prefix, StringComparison.Ordinal))
            && (J.String(input, "service_name") is not { } service || s.ServiceName == service)
            && (J.String(input, "query") is not { } query ||
                s.MetricName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
            && (J.String(input, "kind") is not { } kind || MetricKind(s.MetricKind) == kind)
            && (J.String(input, "unit") is not { } unit || s.Unit == unit)
            && AnalysisMatchers.Match(SeriesRecord(s), input["matchers"]))
    ];
    private JsonObject Metrics(JsonObject input)
    {
        var rows = SeriesRows(input);
        // A name recorded with another kind, unit or monotonicity needs a series_id in query_metric; another temporality
        // does not, since query_metric reads each series by its own.
        var conflicting = rows.GroupBy(static row => row.MetricName, StringComparer.Ordinal)
            .Where(static name => name.Select(static row => (row.MetricKind, row.Unit, row.IsMonotonic)).Distinct().Skip(1).Any())
            .Select(static name => name.Key).ToHashSet(StringComparer.Ordinal);
        // One row per metric descriptor across every resource and scope version that reports it: a restart's new
        // service.instance.id and a deploy's new service and scope versions add series to a row, not rows.
        var items = rows.GroupBy(static row => (row.MetricName, row.MetricKind, row.Unit, row.Temporality, row.IsMonotonic))
            .OrderBy(static descriptor => descriptor.Key.MetricName, StringComparer.Ordinal)
            .ThenBy(static descriptor => descriptor.Key.MetricKind).ThenBy(static descriptor => descriptor.Key.Temporality)
            .ThenBy(static descriptor => descriptor.Key.IsMonotonic).ThenBy(static descriptor => descriptor.Key.Unit, StringComparer.Ordinal)
            .Select(descriptor =>
            {
                // Its series counted, their latest last_seen, their services and how many values each key holds across them.
                var full = JsonSerializer.SerializeToNode(MetricMapper.ToContract(MetricStorageMapper.ToCatalogEntry(descriptor.ToArray())), global::Nami.Api.Contracts.ContractTypes.TypeInfo<global::Nami.Api.Contracts.OTel.Metrics.MetricDescriptor>())!.AsObject();
                var keys = descriptor.SelectMany(static s => SeriesRecord(s).Attributes).GroupBy(static p => p.Key, StringComparer.Ordinal)
                    .Select(static g => (Key: g.Key, Cardinality: g.Select(static p => p.Value?.ToJsonString() ?? "null").Distinct(StringComparer.Ordinal).Count()))
                    .OrderByDescending(static k => k.Cardinality).ThenBy(static k => k.Key, StringComparer.Ordinal).ToList();
                return J.O(("name", full["name"]), ("kind", full["kind"]), ("unit", full["unit"]), ("temporality", full["temporality"]), ("monotonic", full["monotonic"]),
                    ("services", full["services"] ?? new JsonArray()), ("series_count", full["series_count"]), ("last_seen", full["last_seen"]),
                    ("supported_aggregations", full["supported_aggregations"] ?? new JsonArray()), ("has_conflict", conflicting.Contains(descriptor.Key.MetricName)),
                    ("keys", J.A(keys.Select(static k => (JsonNode?)J.O(("key", k.Key), ("cardinality", k.Cardinality))))),
                    ("suggested_group_by", J.N(keys.Where(static k => k.Cardinality is > 1 and <= 20).Select(static k => k.Key))));
            });
        return Page(input, "items", items, snapshot.Series.Count);
    }
    internal static JsonArray ContractAttributes(JsonObject source)
    {
        return J.A(source.Select(static p => J.O(("key", p.Key), ("value", ContractValue(p.Value)))));
    }
    private static JsonNode? ContractValue(JsonNode? value)
    {
        if (value is JsonArray array) return J.A(array.Select(ContractValue));
        if (value?.GetValueKind() != JsonValueKind.Number) return value?.DeepClone();
        var text = value.ToJsonString();
        return long.TryParse(text, CultureInfo.InvariantCulture, out var integer)
            ? J.O(("type", "int"), ("value", integer.ToString(CultureInfo.InvariantCulture)))
            : J.O(("type", "double"), ("value", value));
    }
    // A query without series names why: a metric the project never recorded, one recorded only before the window, or
    // filters that exclude every series the window holds.
    private AnalysisInputException NoMetricSeries(string name) =>
        snapshot.Series.Any(s => s.MetricName == name)
            ? new("metric_name", $"No series of metric {name} in this window matches series_id and matchers; list_metrics with query {name} lists its series' attribute keys.", "metric.not_found") { Missing = name }
            : snapshot.MetricLastSeen.TryGetValue(name, out var lastSeen)
                ? new("metric_name", $"Metric {name} has no points in this window; its last point is at {J.Time(lastSeen)}. Widen lookback_minutes or pass start_time to cover it.", "metric.not_found") { Missing = name }
                : new("metric_name", $"Metric {name} was not found in this project; list_metrics lists the recorded metrics.", "metric.not_found") { Missing = name };
    private JsonObject QueryMetric(JsonObject input)
    {
        var rows = SeriesRows(input); var name = J.String(input, "metric_name")!;
        if (rows.Count is 0) throw NoMetricSeries(name);
        var aggregation = J.String(input, "aggregation") ?? "avg"; var function = J.String(input, "function") ?? "none";
        // Each series is read by its own temporality below, so series may differ in it, but not in what they measure.
        if (rows.Select(static row => (row.MetricKind, row.Unit, row.IsMonotonic)).Distinct().Skip(1).Any())
            throw new MetricQueryConflictException();
        if (rows.SelectMany(row => PointsBySeries[row.SeriesId]).Any(point => _window.Contains(point.TimeUnixNano) && point.HasConflict != 0))
            throw new MetricQueryConflictException("Different measurements exist at one series timestamp; inspect the original observations before aggregating this window.");
        var supported = MetricMapper.SupportedAggregations(rows.Select(static row => row.MetricKind))
            .Select(static aggregation => aggregation.ToString().ToLowerInvariant()).ToArray();
        if (!supported.Contains(aggregation, StringComparer.Ordinal))
            throw new AnalysisInputException("aggregation", $"'{aggregation}' does not apply to this metric; use one of: {string.Join(", ", supported)}.",
                aggregation.StartsWith('p') ? "aggregation.requires_histogram" : "aggregation.unsupported") { Allowed = supported, Hint = "list_metrics shows each metric's supported_aggregations; percentiles need a histogram." };
        if (aggregation.StartsWith('p') && rows.SelectMany(row => PointsBySeries[row.SeriesId])
                .FirstOrDefault(point => _window.Contains(point.TimeUnixNano) && point.ProjectionUnavailableReason is not null) is { } unavailable)
            throw new AnalysisInputException("aggregation", unavailable.ProjectionUnavailableReason!, "metric.projection_unavailable");
        // A histogram's points are already the changes of their intervals; a change or a rate applies to their count or sum.
        var histogram = rows[0].MetricKind >= 3;
        if (histogram && function != "none" && aggregation is not ("count" or "sum"))
            throw new AnalysisInputException("function", $"function={function} applies to a histogram's count or sum; use aggregation count or sum, or function none.", "function.unsupported") { Allowed = ["none"], Hint = "With aggregation count or sum a histogram also takes delta, increase or rate." };
        // A scalar's recorded values are totals in a cumulative series and changes in a delta one: only a function that
        // reads each series' changes puts them on one footing.
        if (!histogram && function == "none" && rows.Select(static row => row.Temporality).Distinct().Skip(1).Any())
            throw new AnalysisInputException("function", "This metric's series differ in temporality: a cumulative series records totals and a delta series changes, so their recorded values do not aggregate together; use function delta, increase or rate, or select one series_id.", "function.mixed_temporality")
                { Allowed = ["delta", "increase", "rate"], Hint = "Pass function increase for each bucket's change, or a series_id of one temporality." };
        var groupBy = (input["group_by"] as JsonArray)?.Select(J.Text).ToArray() ?? [];
        // Without a step, whole seconds split the window into at most 60 buckets.
        var step = input["step_ms"] is null
            ? Math.Min(QueryMetricInput.StepMsMaximum, (long)Math.Ceiling((_window.End - _window.Start).TotalSeconds / 60) * 1000)
            : (long)J.Number(input, "step_ms");
        var streams = rows.GroupBy(s => string.Join('\0', groupBy.Select(key => J.Text(SeriesRecord(s).Value(key))))).ToList();
        var output = new List<JsonObject>();
        double? Reduce(List<(ulong Time, double? Value, MetricPointRow Point)> points)
        {
            if (histogram) return function == "rate" ? HistogramRate(points.Select(static p => p.Point), aggregation) : MetricHistogram.Histogram([.. points.Select(static p => p.Point)], aggregation);
            var recorded = points.Where(static p => p.Value.HasValue).ToList(); var numbers = recorded.Select(static p => p.Value!.Value).ToList();
            return numbers.Count is 0 ? null : function == "increase" ? numbers.Sum() : aggregation switch
            {
                "min" => numbers.Min(), "max" => numbers.Max(), "sum" => numbers.Sum(), "count" => numbers.Count,
                "last" => MetricHistogram.Latest(recorded.Select(static p => (p.Time, p.Value))), _ => numbers.Average()
            };
        }
        foreach (var stream in streams)
        {
            var values = new List<(ulong Time, double? Value, MetricPointRow Point)>();
            foreach (var series in stream)
            {
                var points = PointsBySeries[series.SeriesId].OrderBy(p => p.TimeUnixNano).ToList();
                // The last point that recorded a measurement; a NO_RECORDED_VALUE point carries none.
                MetricPointRow? previous = null;
                foreach (var point in points)
                {
                    ct.ThrowIfCancellationRequested();
                    var value = function == "none" ? point.Value : Change(series, point, previous, function);
                    var recorded = series is { MetricKind: >= 3, Temporality: 2 }
                        ? MetricHistogram.HistogramDelta(point, previous, _window.StartNs) : point;
                    if (_window.Contains(point.TimeUnixNano) && recorded is not null) values.Add((point.TimeUnixNano, value, recorded));
                    if (point.Value.HasValue || point.HistogramCount.HasValue) previous = point;
                }
            }
            if ((_window.End - _window.Start).TotalMilliseconds / step > 10000) throw new AnalysisInputException("step_ms", "Increase step_ms so the window contains no more than 10000 buckets.") { Hint = "Omit step_ms for at most 60 buckets over the window." };
            // Only buckets that hold points; the series' value reduces the whole window as one bucket.
            var buckets = J.A(values.GroupBy(p => (p.Time - _window.StartNs) / ((ulong)step * 1_000_000)).OrderBy(static g => g.Key).Select(g =>
                (JsonNode)J.O(("bucket_start", J.Time(_window.StartNs + g.Key * (ulong)step * 1_000_000)), ("value", Reduce([.. g])), ("point_count", g.Count()))));
            var attributes = new JsonObject(); foreach (var key in groupBy) attributes[key] = SeriesRecord(stream.First()).Value(key)?.DeepClone() ?? JsonValue.Create("");
            output.Add(J.O(("attributes", ContractAttributes(attributes)), ("value", Reduce(values)), ("buckets", buckets)));
        }
        var limit = (int)J.Number(input, "limit", QueryMetricInput.LimitDefault);
        // A count is of points or observations, not of the metric's unit; a rate is per second.
        var unit = aggregation == "count" ? "1" : rows[0].Unit;
        if (function == "rate" && (histogram || aggregation != "count")) unit += "/s";
        var result = J.O(("name", name), ("kind", MetricKind(rows[0].MetricKind)), ("unit", unit), ("aggregation", aggregation), ("step_ms", step), ("series", J.A(output.Take(limit))),
            ("method", histogram ? function == "rate" ? "per-series histogram deltas over the intervals they recorded, summed across series; an unknown interval leaves its bucket unknown" : "per-series histogram deltas merged, linear interpolation; unknown intervals omitted" : function == "none" ? "recorded scalar points aggregated" : "per-series counter differences across resets"), ("numeric_representation", "float64"), ("approximate", histogram && aggregation.StartsWith('p')), ("exemplar_trace_ids", snapshot.Exemplars.Where(e => _window.Contains(e.TimeUnixNano) && rows.Any(r => r.SeriesId == e.SeriesId)).Select(e => e.TraceId).Distinct(StringComparer.Ordinal).Take(3)), ("coverage", Coverage(snapshot.Points.Count, rows.Sum(s => PointsBySeries[s.SeriesId].Count(p => _window.Contains(p.TimeUnixNano))), Math.Min(output.Count, limit), output.Count > limit)));
        if (input["baseline"] is not null)
        {
            var copy = input.DeepClone().AsObject(); copy.Remove("baseline"); copy.Remove("ratio_of"); var current = _window;
            _window = Baseline(input); var comparison = QueryMetric(copy); result["comparison"] = J.O(("window", _window.Json), ("series", comparison["series"])); _window = current;
        }
        if (input["ratio_of"] is JsonObject ratio)
        {
            // The denominator is another metric: the numerator's exact series selects nothing in it.
            var copy = input.DeepClone().AsObject(); copy.Remove("ratio_of"); copy.Remove("series_id"); copy["metric_name"] = ratio["metric_name"]?.DeepClone(); copy["aggregation"] = ratio["aggregation"]?.DeepClone() ?? JsonValue.Create(aggregation); copy["matchers"] = ratio["matchers"]?.DeepClone();
            copy["limit"] = QueryMetricInput.LimitMaximum;
            var denominator = QueryMetric(copy);
            DivideMetricSeries(result["series"], denominator["series"]);
            DivideMetricSeries(result["comparison"]?["series"], denominator["comparison"]?["series"]);
            if (J.Bool(denominator["coverage"], "truncated"))
            {
                result["coverage"]!["truncated"] = true;
            }
            result["unit"] = "1";
        }
        return result;
    }
    /// <summary>
    /// The change one point records for <paramref name="function"/>: a delta point's own value, or the
    /// difference to the last recorded point. A cumulative sum restarted at its recorded start time, so
    /// its value is the change since then; a first point claims that interval only when it began inside
    /// the window. A rate divides by the interval the points recorded and is unknown where none was.
    /// https://github.com/open-telemetry/opentelemetry-proto/blob/v1.11.1/opentelemetry/proto/metrics/v1/metrics.proto
    /// </summary>
    private double? Change(MetricSeriesRow series, MetricPointRow point, MetricPointRow? previous, string function)
    {
        if (point.Value is not { } value) return null;
        var cumulativeSum = series is { MetricKind: 2, Temporality: 2 };
        double change; ulong? interval;
        if (series.Temporality == 1)
        {
            change = value;
            interval = point.StartTimeUnixNano is { } start && start < point.TimeUnixNano ? point.TimeUnixNano - start : null;
        }
        else if (previous?.Value is not { } prior)
        {
            if (function == "delta" || !cumulativeSum || point.StartTimeUnixNano is not { } start || start >= point.TimeUnixNano || start < _window.StartNs) return null;
            change = value; interval = point.TimeUnixNano - start;
        }
        else if (function == "delta") { change = value - prior; interval = point.TimeUnixNano - previous.TimeUnixNano; }
        else if (cumulativeSum && point.StartTimeUnixNano is { } start && previous.StartTimeUnixNano is { } priorStart && start != priorStart)
        {
            change = value; interval = start < point.TimeUnixNano ? point.TimeUnixNano - start : null;
        }
        // A monotonic counter that decreased without a recorded new start was reset at an unknown time.
        else if (series.IsMonotonic != 0 && value < prior) { change = value; interval = null; }
        else { change = value - prior; interval = point.TimeUnixNano - previous.TimeUnixNano; }
        return function != "rate" ? change : interval is { } elapsed and > 0 ? change / (elapsed / 1_000_000_000d) : null;
    }
    /// <summary>
    /// A histogram's count or sum per second: per series, what its delta points recorded over the time they
    /// recorded it, summed across series. A point without a recorded measurement or a known interval start
    /// leaves the rate unknown.
    /// </summary>
    private static double? HistogramRate(IEnumerable<MetricPointRow> points, string aggregation)
    {
        double? rate = null;
        foreach (var series in points.GroupBy(static p => p.SeriesId, StringComparer.Ordinal))
        {
            double measure = 0, seconds = 0;
            foreach (var point in series)
            {
                if (point.HistogramCount is not { } count || point.StartTimeUnixNano is not { } start || start >= point.TimeUnixNano) return null;
                if (aggregation == "count") measure += count;
                else if (count > 0) measure += point.HistogramSum ?? double.NaN;
                seconds += (point.TimeUnixNano - start) / 1_000_000_000d;
            }
            if (double.IsNaN(measure)) return null;
            rate = (rate ?? 0) + measure / seconds;
        }
        return rate;
    }
    private static void DivideMetricSeries(JsonNode? numerators, JsonNode? denominators)
    {
        if (numerators is not JsonArray series) return;
        var byAttributes = (denominators as JsonArray ?? []).OfType<JsonObject>()
            .ToDictionary(static s => s["attributes"]!.ToJsonString(), StringComparer.Ordinal);
        foreach (var numerator in series.OfType<JsonObject>())
        {
            var denominator = byAttributes.GetValueOrDefault(numerator["attributes"]!.ToJsonString());
            var whole = denominator?["value"]?.GetValue<double>();
            numerator["value"] = whole is null or 0 || numerator["value"] is null ? null : JsonValue.Create(numerator["value"]!.GetValue<double>() / whole.Value);
            var buckets = (denominator?["buckets"] as JsonArray ?? []).OfType<JsonObject>()
                .ToDictionary(static b => J.String(b, "bucket_start")!, StringComparer.Ordinal);
            foreach (var bucket in numerator["buckets"]!.AsArray().OfType<JsonObject>())
            {
                var value = buckets.GetValueOrDefault(J.String(bucket, "bucket_start")!)?["value"]?.GetValue<double>();
                bucket["value"] = value is null or 0 || bucket["value"] is null ? null
                    : JsonValue.Create(bucket["value"]!.GetValue<double>() / value.Value);
            }
        }
    }

    private JsonObject Ci(JsonObject input)
    {
        var prefix = J.String(input, "service_prefix") ?? "nami-ci";
        var all = _spans.Where(s => s.Service.StartsWith(prefix, StringComparison.Ordinal)).GroupBy(s => s.SessionId ?? s.TraceId!).ToList();
        bool Matches(IGrouping<string, AnalysisRecord> g) => g.Any(s => _window.Contains(s.Start) && AnalysisMatchers.Match(s, input["matchers"]))
            && (J.String(input, "branch") is not { } branch || g.Any(s => J.Text(s.Value(CollectorSemanticAttributeCatalog.VcsRefHeadName)) == branch))
            && (J.String(input, "commit") is not { } commit || g.Any(s => J.Text(s.Value(CollectorSemanticAttributeCatalog.VcsRefHeadRevision)) == commit))
            && (!J.Bool(input, "errors_only") || g.Any(s => s.Error));
        var groups = all.Where(Matches).OrderByDescending(g => g.Max(s => s.Start)).ToList();
        var runId = J.String(input, "ci_run_id"); var selected = runId is not null ? all.FirstOrDefault(g => g.Key == runId) : groups.FirstOrDefault();
        if (runId is not null && selected is null)
            throw new AnalysisInputException("ci_run_id", $"CI run {runId} was not found in this project; " + (groups.Count is 0 ? "ci_log without ci_run_id lists the runs." : $"recent runs: {string.Join(", ", groups.Take(5).Select(static g => g.Key))}."), "ci_run.not_found") { Missing = runId };
        // Index the recorded provider runs once. Attempt numbers describe their
        // observed chronological order, rather than reporting every rerun as 1.
        var runInfo = all.ToDictionary(g => g.Key, g => new
        {
            Start = g.Min(s => s.Start), End = g.Max(s => s.End),
            ProviderId = g.Select(s => J.Text(s.Value(CollectorSemanticAttributeCatalog.CicdPipelineRunId))).FirstOrDefault(v => v.Length > 0),
            ProviderUrl = g.Select(s => J.Text(s.Value(CollectorSemanticAttributeCatalog.CicdPipelineRunUrlFull))).FirstOrDefault(v => v.Length > 0)
        }, StringComparer.Ordinal);
        var attempts = runInfo.Where(p => p.Value.ProviderId is not null).GroupBy(p => p.Value.ProviderId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.Value.Start).ThenBy(p => p.Key, StringComparer.Ordinal)
                .Select((p, index) => (RunId: p.Key, Attempt: index + 1)).ToArray(), StringComparer.Ordinal);
        var runs = groups.Select(g =>
        {
            ct.ThrowIfCancellationRequested();
            var info = runInfo[g.Key];
            var observed = info.ProviderId is { } provider ? attempts[provider] : [];
            return J.O(("ci_run_id", g.Key),
                (CollectorSemanticAttributeCatalog.State, Age(info.End) < TimeSpan.FromSeconds(5) ? "active" : "ended"),
                ("start_time", J.Time(info.Start)), ("duration_ms", (info.End - info.Start) / 1_000_000d),
                ("error_count", g.Count(s => s.Error)), ("services", g.Select(s => s.Service).Distinct()),
                ("attempt", observed.Length > 0 ? observed.First(p => p.RunId == g.Key).Attempt : (int?)null),
                ("attempts", J.A(observed.Where(p => p.RunId != g.Key).Select(p => J.O(("ci_run_id", p.RunId), ("attempt", p.Attempt))))),
                ("provider_run_id", info.ProviderId), ("provider_run_url", info.ProviderUrl));
        });
        var runInput = input.DeepClone().AsObject(); runInput.Remove("phase_cursor");
        var result = Page(runInput, "runs", runs);
        if (selected is not null)
        {
            string Leg(AnalysisRecord s) => J.Text(s.Value(CollectorSemanticAttributeCatalog.CicdPipelineTaskName)) is { Length: > 0 } leg ? leg : s.Service;
            var taskSpans = selected.Where(s => s.Value(CollectorSemanticAttributeCatalog.CicdPipelineTaskName) is not null || s.Value(CollectorSemanticAttributeCatalog.CicdPipelineTaskType) is not null).ToList();
            var errorLogs = _logs.Where(l => l.Error).ToLookup(l => l.TraceId);
            var phases = taskSpans.OrderBy(s => s.Start).ThenBy(s => s.Id, StringComparer.Ordinal).Where(s => (J.String(input, "leg") is not { } leg || Leg(s) == leg) && (J.String(input, "phase") is not { } phase || s.Name == phase)).Select(s =>
            { var logs = errorLogs[s.TraceId].Where(l => l.SpanId == s.Id || l.SpanId is null).ToList(); return J.O(("leg", Leg(s)), ("phase", s.Name), ("status", Status(s)), ("duration_ms", s.Duration), ("message", s.Span?.StatusMessage), ("span_id", s.Id), ("trace_ids", new[] { s.TraceId! }), ("error_log_count", logs.Count), ("error_logs", J.A(logs.Take(3).Select(l => J.O(("id", l.Id), ("body", l.Log?.Body), ("trace_id", l.TraceId), ("span_id", l.SpanId)))))); }).ToList();
            result["ci_run_id"] = selected.Key;
            // The run's phases page by phase_limit, whatever limit pages the runs.
            var phaseInput = input.DeepClone().AsObject(); phaseInput.Remove("cursor"); phaseInput.Remove("phase_cursor"); phaseInput.Remove("limit"); phaseInput.Remove("phase_limit"); phaseInput["ci_run_id"] = selected.Key;
            if (input["phase_limit"] is { } phaseLimit) phaseInput["limit"] = phaseLimit.DeepClone();
            if (input["phase_cursor"] is { } phaseCursor) phaseInput["cursor"] = phaseCursor.DeepClone();
            _operation = "ci_log.phases";
            JsonObject phasePage;
            try { phasePage = Page(phaseInput, "phases", phases); } finally { _operation = "ci_log"; }
            result["phases"] = phasePage["phases"]?.DeepClone();
            result["next_phase_cursor"] = phasePage["next_cursor"]?.DeepClone();
            result["has_more"] = J.Bool(result, "has_more") || J.Bool(phasePage, "has_more");
            result["page_totals"] = J.O(("phases", phases.Count));
            if (result["coverage"] is JsonObject phaseCoverage) phaseCoverage["truncated"] = J.Bool(result, "has_more");
            if (input["baseline"] is { } requested)
            {
                var baseline = J.Text(requested) == "last_success" ? all.Where(g => g.Key != selected.Key && g.Max(s => s.Start) < selected.Min(s => s.Start) && g.All(s => !s.Error)).OrderByDescending(g => g.Max(s => s.Start)).FirstOrDefault() : all.FirstOrDefault(g => g.Key == J.String(requested, "ci_run_id"));
                var fresh = new List<JsonObject>(); var fixedItems = new List<JsonObject>(); var regressions = new List<JsonObject>();
                if (baseline is not null)
                {
                    foreach (var phase in taskSpans)
                    {
                        var old = baseline.FirstOrDefault(s => s.Name == phase.Name && Leg(s) == Leg(phase));
                        var change = J.O(("leg", Leg(phase)), ("phase", phase.Name), ("current_duration_ms", phase.Duration), ("baseline_duration_ms", old?.Duration), ("ratio", old?.Duration > 0 ? phase.Duration / old.Duration : null), ("trace_ids", new[] { phase.TraceId! }));
                        if (phase.Error && old?.Error != true) fresh.Add(change);
                        if (!phase.Error && old?.Error == true) fixedItems.Add(change);
                        if (old?.Duration > 0 && phase.Duration > Math.Max(old.Duration * 1.2, old.Duration + 10)) regressions.Add(change);
                    }
                }
                result["comparison"] = J.O(("baseline_ci_run_id", baseline?.Key), ("new_failures", J.A(fresh)), ("fixed", J.A(fixedItems)), ("duration_regressions", J.A(regressions)));
            }
        }
        return result;
    }
}
