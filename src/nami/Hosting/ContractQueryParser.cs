using System.Numerics;
using Microsoft.Extensions.Primitives;
using Nami.Collector.Storage;
using MetricAggregationValues = Nami.Api.Contracts.OTel.Metrics.MetricAggregationValues;

namespace Nami.Collector.Hosting;

internal readonly record struct ParsedSessionsParameters(
    bool? IsActive,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    int? Limit,
    string? Cursor);

internal readonly record struct ParsedSessionStatsParameters(
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime);

internal readonly record struct ParsedTracesParameters(
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    string? ServiceName,
    bool? ErrorsOnly,
    int? Limit,
    string? Cursor);

/// <summary>The <c>limit</c> and <c>cursor</c> pair of a paged sub-list.</summary>
internal readonly record struct ParsedPageParameters(
    int? Limit,
    string? Cursor);

internal readonly record struct ParsedLogsParameters(
    int? SeverityMin,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    int? Limit,
    string? Cursor);

internal readonly record struct ParsedMetricsParameters(
    string? NamePrefix,
    int? Limit,
    string? Cursor);

internal readonly record struct ParsedMetricSeriesParameters(
    IReadOnlyList<MetricAttributeMatcher> Matchers,
    int? Limit,
    string? Cursor);

internal readonly record struct ParsedAnnotationsParameters(
    string? TraceId,
    string? SpanId,
    string? SessionId,
    int? Limit,
    string? Cursor);

internal readonly record struct ParsedSavedQueriesParameters(
    string? Signal,
    int? Limit,
    string? Cursor);

internal readonly record struct ParsedMetricPointParameters(DateTimeOffset? Start, DateTimeOffset? End, int? Limit, string? Cursor);

internal readonly record struct ParsedMetricQueryParameters(
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    long? StepMs,
    MetricAggregation Aggregation,
    IReadOnlyList<string> GroupBy,
    IReadOnlyList<MetricAttributeMatcher> Matchers,
    int? SeriesLimit,
    string? SeriesId);

internal static class ContractQueryParser
{
    // Query parameters carry their OpenAPI wire names verbatim: the contract is
    // what an API client codes against, and a name that only the collector
    // knows is a name no generated client can send.
    internal static IResult? ParseMetrics(HttpRequest request, out ParsedMetricsParameters parsed)
    {
        parsed = default;
        var reader = new QueryReader(request.Query);
        if (reader.ReadString("name_prefix", out var namePrefix) is { } prefixError) return prefixError;
        if (reader.ReadInteger<int>("limit", out var limit) is { } limitError) return limitError;
        if (reader.ReadString("cursor", out var cursor) is { } cursorError) return cursorError;

        parsed = new ParsedMetricsParameters(namePrefix, limit, cursor);
        return null;
    }

    internal static IResult? ParseMetricSeries(HttpRequest request, out ParsedMetricSeriesParameters parsed)
    {
        parsed = default;
        var reader = new QueryReader(request.Query);
        if (ReadMatchers(reader, out var matchers) is { } matcherError) return matcherError;
        if (reader.ReadInteger<int>("limit", out var limit) is { } limitError) return limitError;
        if (reader.ReadString("cursor", out var cursor) is { } cursorError) return cursorError;

        parsed = new ParsedMetricSeriesParameters(matchers, limit, cursor);
        return null;
    }

    internal static IResult? ParseMetricPoints(HttpRequest request, out ParsedMetricPointParameters parsed)
    {
        parsed = default;
        var reader = new QueryReader(request.Query);
        if (reader.ReadDateTime("start_time", out var start) is { } startError) return startError;
        if (reader.ReadDateTime("end_time", out var end) is { } endError) return endError;
        if (reader.ReadInteger<int>("limit", out var limit) is { } limitError) return limitError;
        if (reader.ReadString("cursor", out var cursor) is { } cursorError) return cursorError;
        parsed = new(start, end, limit, cursor);
        return null;
    }

    internal static IResult? ParseMetricQuery(HttpRequest request, out ParsedMetricQueryParameters parsed)
    {
        parsed = default;
        var reader = new QueryReader(request.Query);
        if (reader.ReadDateTime("start_time", out var startTime) is { } startError) return startError;
        if (reader.ReadDateTime("end_time", out var endTime) is { } endError) return endError;
        if (reader.ReadInteger<long>("step_ms", out var stepMs) is { } stepError) return stepError;
        if (reader.ReadStringList("group_by", out var groupBy) is { } groupError) return groupError;
        if (ReadMatchers(reader, out var matchers) is { } matcherError) return matcherError;
        if (reader.ReadInteger<int>("series_limit", out var seriesLimit) is { } seriesError) return seriesError;
        if (reader.ReadString("series_id", out var seriesId) is { } seriesIdError) return seriesIdError;

        var aggregation = MetricAggregation.Avg;
        if (reader.ReadString("aggregation", out var rawAggregation) is { } aggregationError)
            return aggregationError;
        if (rawAggregation is not null && !TryParseAggregation(rawAggregation, out aggregation))
        {
            return Invalid(
                "aggregation",
                "Value must be one of avg, min, max, sum, count, last, p50, p90, p95, p99.",
                "aggregation.invalid",
                rawAggregation);
        }

        parsed = new ParsedMetricQueryParameters(
            startTime,
            endTime,
            stepMs,
            aggregation,
            groupBy,
            matchers,
            seriesLimit,
            seriesId);
        return null;
    }

    // The contract spells a matcher `key=value`, splitting on the first '=' so a value may
    // itself contain one. An entry with no '=' selects nothing knowable, so it is rejected
    // rather than silently ignored.
    private static IResult? ReadMatchers(QueryReader reader, out IReadOnlyList<MetricAttributeMatcher> matchers)
    {
        matchers = [];
        if (reader.ReadStringList("attr", out var exact) is { } exactError) return exactError;
        if (reader.ReadStringList("attr_prefix", out var prefixed) is { } prefixError) return prefixError;

        var parsed = new List<MetricAttributeMatcher>(exact.Count + prefixed.Count);
        foreach (var (name, values, isPrefix) in
                 new[] { ("attr", exact, false), ("attr_prefix", prefixed, true) })
        {
            foreach (var value in values)
            {
                var separator = value.IndexOf('=', StringComparison.Ordinal);
                if (separator <= 0)
                {
                    return Invalid(
                        name,
                        "Value must be 'key=value' with a non-empty key.",
                        "matcher.invalid",
                        value);
                }

                parsed.Add(new MetricAttributeMatcher(
                    value[..separator],
                    value[(separator + 1)..],
                    isPrefix));
            }
        }

        matchers = parsed;
        return null;
    }

    // The contract's closed set, matched exactly as the other enum parameters are: Enum.TryParse
    // would also take a comma list (min,max is Min|Max, which is Sum) and a decimal ordinal.
    private static bool TryParseAggregation(string raw, out MetricAggregation aggregation)
    {
        MetricAggregation? parsed = raw switch
        {
            MetricAggregationValues.Avg => MetricAggregation.Avg,
            MetricAggregationValues.Min => MetricAggregation.Min,
            MetricAggregationValues.Max => MetricAggregation.Max,
            MetricAggregationValues.Sum => MetricAggregation.Sum,
            MetricAggregationValues.Count => MetricAggregation.Count,
            MetricAggregationValues.Last => MetricAggregation.Last,
            MetricAggregationValues.P50 => MetricAggregation.P50,
            MetricAggregationValues.P90 => MetricAggregation.P90,
            MetricAggregationValues.P95 => MetricAggregation.P95,
            MetricAggregationValues.P99 => MetricAggregation.P99,
            _ => null
        };
        aggregation = parsed.GetValueOrDefault();
        return parsed.HasValue;
    }

    internal static IResult? ParseSessions(HttpRequest request, out ParsedSessionsParameters parsed)
    {
        parsed = default;
        var reader = new QueryReader(request.Query);
        if (reader.ReadBoolean("is_active", out var isActive) is { } error) return error;
        if (reader.ReadDateTime("start_time", out var startTime) is { } startError) return startError;
        if (reader.ReadDateTime("end_time", out var endTime) is { } endError) return endError;
        if (reader.ReadInteger<int>("limit", out var limit) is { } limitError) return limitError;
        if (reader.ReadString("cursor", out var cursor) is { } cursorError) return cursorError;

        parsed = new ParsedSessionsParameters(isActive, startTime, endTime, limit, cursor);
        return null;
    }

    internal static IResult? ParseSessionStats(HttpRequest request, out ParsedSessionStatsParameters parsed)
    {
        parsed = default;
        var reader = new QueryReader(request.Query);
        if (reader.ReadDateTime("start_time", out var startTime) is { } startError) return startError;
        if (reader.ReadDateTime("end_time", out var endTime) is { } endError) return endError;

        parsed = new ParsedSessionStatsParameters(startTime, endTime);
        return null;
    }

    internal static IResult? ParseTraces(HttpRequest request, out ParsedTracesParameters parsed)
    {
        parsed = default;
        var reader = new QueryReader(request.Query);
        if (reader.ReadDateTime("start_time", out var startTime) is { } startError) return startError;
        if (reader.ReadDateTime("end_time", out var endTime) is { } endError) return endError;
        if (reader.ReadString("service_name", out var serviceName) is { } serviceError) return serviceError;
        if (reader.ReadBoolean("errors_only", out var errorsOnly) is { } errorsError) return errorsError;
        if (reader.ReadInteger<int>("limit", out var limit) is { } limitError) return limitError;
        if (reader.ReadString("cursor", out var cursor) is { } cursorError) return cursorError;
        if (RejectInvertedRange(startTime, endTime) is { } rangeError) return rangeError;

        parsed = new ParsedTracesParameters(startTime, endTime, serviceName, errorsOnly, limit, cursor);
        return null;
    }

    internal static IResult? ParsePage(HttpRequest request, out ParsedPageParameters parsed)
    {
        parsed = default;
        var reader = new QueryReader(request.Query);
        if (reader.ReadInteger<int>("limit", out var limit) is { } limitError) return limitError;
        if (reader.ReadString("cursor", out var cursor) is { } cursorError) return cursorError;

        parsed = new ParsedPageParameters(limit, cursor);
        return null;
    }

    internal static IResult? ParseAnnotations(HttpRequest request, out ParsedAnnotationsParameters parsed)
    {
        parsed = default;
        var reader = new QueryReader(request.Query);
        if (reader.ReadString("trace_id", out var traceId) is { } traceError) return traceError;
        if (reader.ReadString("span_id", out var spanId) is { } spanError) return spanError;
        if (reader.ReadString("session_id", out var sessionId) is { } sessionError) return sessionError;
        if (reader.ReadInteger<int>("limit", out var limit) is { } limitError) return limitError;
        if (reader.ReadString("cursor", out var cursor) is { } cursorError) return cursorError;

        // The ids' lengths and patterns are the contract's, checked before the route runs (RouteParameterRules).
        if (spanId is not null && traceId is null)
            return Invalid("span_id", "span_id requires trace_id.", "span_id.requires_trace_id", spanId);

        parsed = new ParsedAnnotationsParameters(traceId, spanId, sessionId, limit, cursor);
        return null;
    }

    internal static IResult? ParseSavedQueries(HttpRequest request, out ParsedSavedQueriesParameters parsed)
    {
        parsed = default;
        var reader = new QueryReader(request.Query);
        if (reader.ReadString("signal", out var signal) is { } signalError) return signalError;
        if (reader.ReadInteger<int>("limit", out var limit) is { } limitError) return limitError;
        if (reader.ReadString("cursor", out var cursor) is { } cursorError) return cursorError;

        if (signal is not null and not ("traces" or "logs" or "metrics"))
            return Invalid("signal", "Value must be one of traces, logs, metrics.", "signal.invalid", signal);

        parsed = new ParsedSavedQueriesParameters(signal, limit, cursor);
        return null;
    }

    private static IResult? RejectInvertedRange(DateTimeOffset? startTime, DateTimeOffset? endTime) =>
        startTime is { } start && endTime is { } end && end < start
            ? Invalid(
                "end_time",
                "The range end must not precede its start.",
                "range.inverted",
                end.ToString("O", CultureInfo.InvariantCulture))
            : null;

    internal static IResult? ParseLogs(HttpRequest request, out ParsedLogsParameters parsed)
    {
        parsed = default;
        var reader = new QueryReader(request.Query);
        if (reader.ReadInteger<int>("severity_min", out var severityMin) is { } severityError) return severityError;
        if (reader.ReadDateTime("start_time", out var startTime) is { } startError) return startError;
        if (reader.ReadDateTime("end_time", out var endTime) is { } endError) return endError;
        if (reader.ReadInteger<int>("limit", out var limit) is { } limitError) return limitError;
        if (reader.ReadString("cursor", out var cursor) is { } cursorError) return cursorError;

        parsed = new ParsedLogsParameters(severityMin, startTime, endTime, limit, cursor);
        return null;
    }

    internal static IResult? ParseLogStream(HttpRequest request, out int? minSeverity) =>
        new QueryReader(request.Query).ReadInteger<int>("min_severity", out minSeverity);

    private readonly struct QueryReader(IQueryCollection query)
    {
        public IResult? ReadInteger<T>(string name, out T? value) where T : struct, IBinaryInteger<T>
        {
            value = null;
            if (!TryReadSingle(name, out var raw, out var rejectedValue)) return null;
            if (raw is not null && T.TryParse(
                    raw,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out var parsed))
            {
                value = parsed;
                return null;
            }

            return Invalid(
                name,
                $"Value must be a single {T.Zero.GetByteCount() * 8}-bit integer.",
                "query.invalid_integer",
                rejectedValue);
        }

        // Repeatable parameters are the one place multiple values are legal, so they bypass
        // TryReadSingle rather than being rejected by it.
        public IResult? ReadStringList(string name, out IReadOnlyList<string> values)
        {
            values = [];
            if (!query.TryGetValue(name, out var raw)) return null;

            var parsed = new List<string>(raw.Count);
            foreach (var value in raw)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return Invalid(
                        name,
                        "Each value must be a non-empty string.",
                        "query.invalid_string",
                        JoinValues(raw));
                }

                parsed.Add(value);
            }

            values = parsed;
            return null;
        }

        public IResult? ReadBoolean(string name, out bool? value)
        {
            value = null;
            if (!TryReadSingle(name, out var raw, out var rejectedValue)) return null;
            if (raw is not null && bool.TryParse(raw, out var parsed))
            {
                value = parsed;
                return null;
            }

            return Invalid(
                name,
                "Value must be a single boolean ('true' or 'false').",
                "query.invalid_boolean",
                rejectedValue);
        }

        public IResult? ReadString(string name, out string? value)
        {
            value = null;
            if (!TryReadSingle(name, out var raw, out var rejectedValue)) return null;
            if (raw is not null)
            {
                value = raw;
                return null;
            }

            return Invalid(
                name,
                "Value must be a single non-empty string.",
                "query.invalid_string",
                rejectedValue);
        }

        public IResult? ReadDateTime(string name, out DateTimeOffset? value)
        {
            value = null;
            if (!TryReadSingle(name, out var raw, out var rejectedValue)) return null;
            if (raw is not null && ContractDateTime.Read(raw) is { } time)
            {
                // An instant holds 100 ns; a finer boundary is rejected rather than silently moved.
                if (ContractDateTime.FinerThanTicks(time.Fraction))
                    return Invalid(name, "Value is finer than 100 nanoseconds; use at most seven fractional digits.", "time.precision_unsupported", rejectedValue);
                if (time.Instant is { } instant && NamiTimeConversions.TryToUnixNanoUnsigned(instant.ToUniversalTime(), out _))
                {
                    value = instant;
                    return null;
                }
            }

            return Invalid(
                name,
                "Value must be a single RFC 3339 date-time with an explicit UTC offset.",
                "query.invalid_date_time",
                rejectedValue);
        }

        private bool TryReadSingle(string name, out string? value, out string? rejectedValue)
        {
            value = null;
            rejectedValue = null;
            if (!query.TryGetValue(name, out var values)) return false;

            rejectedValue = JoinValues(values);
            if (values.Count == 1 && !string.IsNullOrEmpty(values[0])) value = values[0];
            return true;
        }

        private static string JoinValues(StringValues values) =>
            string.Join(',', values.ToArray());
    }

    private static IResult Invalid(string field, string message, string code, string? rejectedValue) =>
        ContractErrorResults.Validation(field, message, code, rejectedValue);
}

/// <summary>The trace and span id formats a page cursor this Collector issued carries (common/types.tsp).</summary>
internal static class ContractIdentifiers
{
    internal static bool IsTraceId(string? value) =>
        Nami.Api.Contracts.ContractJson.Validate("Common.TraceId", System.Text.Json.Nodes.JsonValue.Create(value)) is null;

    internal static bool IsSpanId(string? value) =>
        Nami.Api.Contracts.ContractJson.Validate("Common.SpanId", System.Text.Json.Nodes.JsonValue.Create(value)) is null;
}
