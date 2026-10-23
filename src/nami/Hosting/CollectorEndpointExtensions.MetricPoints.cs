using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Nami.Api.Contracts.Common.Pagination;
using Nami.Collector.Analysis;
using Nami.Collector.Storage;

namespace Nami.Collector.Hosting;

internal static partial class CollectorEndpointExtensions
{
    private static async Task<IResult> GetMetricPointsAsync(HttpContext context,
        [FromRoute(Name = "metric_name")] string metricName,
        [FromRoute(Name = "series_id")] string seriesId, INamiStore store, CancellationToken ct)
    {
        if (ContractQueryParser.ParseMetricPoints(context.Request, out var query) is { } error) return error;
        var project = ResolveProjectScope(context);
        if ((await store.ListMetricSeriesAsync(project, metricName, [], 1, seriesId: seriesId, ct: ct)).SingleOrDefault() is not { } series) return ContractErrorResults.NotFound("metric series", seriesId);
        try { return ContractHttp.Json(await ReadMetricPointPageAsync(store, series, project, query, ct)); }
        catch (AnalysisInputException invalid) { return AnalysisError(invalid); }
    }

    private static async Task<CursorPageMetricPoint> ReadMetricPointPageAsync(INamiStore store, MetricSeriesRow series,
        string project, ParsedMetricPointParameters query, CancellationToken ct)
    {
        if (!ContractLimits.TryResolve(query.Limit, ContractLimits.DefaultPageLimit, ContractLimits.MaxPageLimit, ContractLimits.MinimumPageLimit, out var limit))
            throw new AnalysisInputException("limit", $"Limit must be between {ContractLimits.MinimumPageLimit} and {ContractLimits.MaxPageLimit}.", "limit.out_of_range");
        if (query.Start < DateTimeOffset.UnixEpoch || query.End < DateTimeOffset.UnixEpoch)
            throw new AnalysisInputException("start_time", "Metric times must be on or after the Unix epoch.", "range.invalid");
        if (query.Start is { } start && query.End is { } end && start > end)
            throw new AnalysisInputException("end_time", "The range end must not precede its start.", "range.inverted");
        MetricPointCursor? cursor = null;
        if (query.Cursor is { } encoded)
        {
            // Bind continuations to their exact project, instrument and stream; they cannot silently page another series.
            if (!PageCursorCodec.TryDecode(encoded, PageCursorKind.MetricPoints, 5, out var fields) ||
                fields[0] != project || fields[1] != series.MetricName || fields[2] != series.SeriesId ||
                !PageCursorCodec.TryParseHex(fields[3], out var time))
                throw new AnalysisInputException("cursor", "The metric point cursor is invalid for this series.", "cursor.invalid");
            cursor = new MetricPointCursor(time, fields[4]);
        }
        var observations = await store.ListMetricPointsAsync(new MetricPointQuery
        {
            ProjectId = project, MetricName = series.MetricName, SeriesId = series.SeriesId, Limit = limit + 1,
            StartUnixNano = query.Start is { } from ? NamiTimeConversions.ToUnixNanoUnsigned(from) : null,
            EndUnixNano = query.End is { } until ? NamiTimeConversions.ToUnixNanoUnsigned(until) : null, Cursor = cursor
        }, ct);
        var (page, more) = SplitPage(observations, limit);
        return new CursorPageMetricPoint
        {
            Items = page.Select(item => MetricMapper.ToContract(item, series)).ToArray(), HasMore = more,
            NextCursor = more ? PageCursorCodec.Encode(PageCursorKind.MetricPoints, project, series.MetricName,
                series.SeriesId, PageCursorCodec.Hex(page[^1].Observation.TimeUnixNano), page[^1].Observation.ObservationId) : null
        };
    }

    // A raw read selects among at most this many series; more of them is a request to narrow the matchers.
    private const int RawSeriesCandidates = 20;

    // query_metric with raw: one series' original points, a page at a time. The signed cursor fixes the window and
    // the series, so a continuation never pages another one.
    private static async Task<JsonObject> RawMetricPointsAsync(JsonObject input, INamiStore store, string project,
        AnalysisHandles handles, DateTimeOffset now, CancellationToken ct)
    {
        AnalysisMatchers.Validate(input["matchers"]);
        var name = J.String(input, "metric_name")!;
        var seriesId = J.String(input, "series_id");
        var window = AnalysisWindow.Resolve(input, now);
        string? pointCursor = null;
        if (J.String(input, "cursor") is { } continuation)
        {
            var saved = handles.Read(continuation, project, "metric-points");
            if (J.String(saved, "metric_name") != name || seriesId is not null && J.String(saved, "series_id") != seriesId)
                throw new AnalysisInputException("cursor", "The cursor continues another series' points.", "cursor.invalid");
            window = AnalysisWindow.Resolve(saved["window"]!.AsObject(), now);
            seriesId = J.String(saved, "series_id");
            pointCursor = J.String(saved, "cursor");
        }
        var candidates = (await store.ListMetricSeriesAsync(project, name, [], 1000, seriesId: seriesId, ct: ct))
            .Where(s => AnalysisMatchers.Match(AnalysisEngine.SeriesRecord(s), input["matchers"])).Take(RawSeriesCandidates + 1).ToList();
        if (candidates.Count is 0)
            throw seriesId is not null
                ? new AnalysisInputException("series_id", $"Series {seriesId} of metric {name} was not found in this project; query_metric without raw lists the metric's values per group_by.", "series.not_found") { Missing = seriesId }
                : new AnalysisInputException("metric_name", $"Metric {name} has no series in this project that the matchers select; list_metrics lists the recorded metrics and their attribute keys.", "metric.not_found") { Missing = name };
        if (candidates.Count > 1)
            throw new AnalysisInputException("series_id", $"{(candidates.Count > RawSeriesCandidates ? "More than " + RawSeriesCandidates : candidates.Count.ToString(CultureInfo.InvariantCulture))} series of {name} match; raw reads one: pass one of their series_id values, or matchers that select one series.", "series_id.ambiguous")
                { Allowed = [.. candidates.Take(RawSeriesCandidates).Select(static s => s.SeriesId)], Hint = "query_metric with group_by shows how the series differ." };
        var series = candidates[0];
        var page = await ReadMetricPointPageAsync(store, series, project,
            new ParsedMetricPointParameters(window.Start, window.End, (int)J.Number(input, "limit", Api.Contracts.Mcp.Tools.QueryMetricInput.LimitDefault), pointCursor), ct);
        return J.O(("name", name), ("kind", AnalysisEngine.MetricKind(series.MetricKind)), ("unit", series.Unit),
            ("series_id", series.SeriesId), ("attributes", AnalysisEngine.ContractAttributes(AnalysisEngine.SeriesRecord(series).Attributes)),
            ("points", J.A(page.Items.Select(RawPoint))), ("has_more", page.HasMore),
            ("next_cursor", page.NextCursor is { } next ? handles.Issue(project, "metric-points", J.O(("window", window.Json), ("metric_name", name), ("series_id", series.SeriesId), ("cursor", next))) : null),
            ("window", window.Json), ("coverage", J.O(("examined", page.Items.Count), ("matched", page.HasMore ? null : (int?)page.Items.Count),
                ("returned", page.Items.Count), ("truncated", page.HasMore), ("scan_complete", true), ("as_of", window.End.ToString("O")))));
    }

    // An original point with RFC 3339 times; the series' attributes are on the answer, not on every point.
    private static JsonObject RawPoint(Api.Contracts.OTel.Metrics.MetricPoint point)
    {
        var json = JsonSerializer.SerializeToNode(point, global::Nami.Api.Contracts.ContractTypes.TypeInfo<global::Nami.Api.Contracts.OTel.Metrics.MetricPoint>())!.AsObject();
        var exemplars = (json["exemplars"] as JsonArray ?? []).OfType<JsonObject>().Zip(point.Exemplars ?? []).Select(static pair => J.O(
            ("time", J.Time(pair.Second.TimeUnixNano)), ("value", pair.First["value"]), ("trace_id", pair.Second.TraceId), ("span_id", pair.Second.SpanId),
            ("filtered_attributes", pair.Second.FilteredAttributes.Count > 0 ? pair.First["filtered_attributes"] : null))).ToList();
        return J.O(("time", J.Time(point.TimeUnixNano)), ("start_time", point.StartTimeUnixNano is { } start ? J.Time(start) : null), ("data", json["data"]),
            ("has_conflict", point.HasConflict), ("exemplars", exemplars.Count > 0 ? J.A(exemplars) : null));
    }
}
