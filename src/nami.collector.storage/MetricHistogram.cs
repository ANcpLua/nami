using System.Text.Json.Nodes;

namespace Nami.Collector.Storage;

// Shared by product metric queries and agent analysis; temporality and bucket math have one owner.
internal static class MetricHistogram
{
    // Cumulative exports overlap. Subtract within each series before merging streams.
    // Without a prior observation, only a sample started inside this window is attributable.
    // The delta's start time is the start of the interval it covers, null when that is unknown.
    internal static MetricPointRow? HistogramDelta(MetricPointRow point, MetricPointRow? previous, ulong windowStart)
    {
        if (point.HistogramCount is null) return null;
        if (previous is null) return point.StartTimeUnixNano >= windowStart ? point : null;
        // A new start time restarted the series there.
        if (point.StartTimeUnixNano != previous.StartTimeUnixNano) return point;
        // A decrease without a new start time is a reset at an unknown time.
        if (point.HistogramCount < previous.HistogramCount) return point with { StartTimeUnixNano = null };
        var current = JsonNode.Parse(point.BucketCountsJson ?? "[]")!.AsArray().Select(v => v!.GetValue<ulong>()).ToArray();
        var before = JsonNode.Parse(previous.BucketCountsJson ?? "[]")!.AsArray().Select(v => v!.GetValue<ulong>()).ToArray();
        if (point.BucketBoundsJson != previous.BucketBoundsJson || current.Length != before.Length) return null;
        if (current.Where((count, index) => count < before[index]).Any()) return point with { StartTimeUnixNano = null };
        return point with
        {
            StartTimeUnixNano = previous.TimeUnixNano,
            HistogramCount = point.HistogramCount - previous.HistogramCount,
            HistogramSum = point.HistogramSum - previous.HistogramSum,
            // Extrema of an interval cannot be recovered by subtracting cumulative extrema.
            HistogramMin = null, HistogramMax = null,
            BucketCountsJson = "[" + string.Join(",", current.Select((count, index) => (count - before[index]).ToString(CultureInfo.InvariantCulture))) + "]"
        };
    }
    /// <summary>
    /// Reduces one bucket's histogram points. A point without a recorded distribution (NO_RECORDED_VALUE)
    /// leaves the bucket unknown, and a sum or extremum is claimed only when every point with observations
    /// recorded it. A percentile interpolates inside its bucket, narrowed by the recorded extrema; the first
    /// and the overflow bucket have no other bound, so without them such a percentile is unknown.
    /// </summary>
    internal static double? Histogram(List<MetricPointRow> points, string aggregation)
    {
        if (points.Count is 0 || points.Any(static p => p.HistogramCount is null)) return null;
        var observed = points.Where(static p => p.HistogramCount > 0).ToList();
        double? sum = observed.Any(static p => p.HistogramSum is null) ? null : observed.Sum(static p => p.HistogramSum!.Value);
        double? min = observed.Count is 0 || observed.Any(static p => p.HistogramMin is null) ? null : observed.Min(static p => p.HistogramMin!.Value);
        double? max = observed.Count is 0 || observed.Any(static p => p.HistogramMax is null) ? null : observed.Max(static p => p.HistogramMax!.Value);
        var count = points.Sum(static p => (double)p.HistogramCount!.Value);
        switch (aggregation)
        {
            case "count": return count;
            case "sum": return sum;
            case "avg": return count is 0 ? null : sum / count;
            case "min": return min;
            case "max": return max;
            case "last": return Latest(points.Select(static p => (p.TimeUnixNano, p.HistogramSum)));
        }
        if (!double.TryParse(aggregation.AsSpan(1), CultureInfo.InvariantCulture, out var percentile)) throw new ArgumentOutOfRangeException(nameof(aggregation), "Choose a supported histogram aggregation.");
        if (points.Select(static p => p.BucketBoundsJson).Distinct(StringComparer.Ordinal).Skip(1).Any()) return null;
        var bounds = JsonNode.Parse(points[0].BucketBoundsJson ?? "[]")!.AsArray().Select(static v => v!.GetValue<double>()).ToArray();
        // Explicit bounds delimit buckets only when they are finite and strictly increasing.
        if (bounds.Where((bound, index) => !double.IsFinite(bound) || index > 0 && bound <= bounds[index - 1]).Any()) return null;
        var merged = new double[bounds.Length + 1];
        foreach (var point in points)
        {
            var counts = JsonNode.Parse(point.BucketCountsJson ?? "[]")!.AsArray().Select(static v => v!.GetValue<double>()).ToArray();
            if (counts.Length != merged.Length) return null;
            for (var i = 0; i < counts.Length; i++) merged[i] += counts[i];
        }
        var target = merged.Sum() * percentile / 100; double cumulative = 0;
        for (var i = 0; i < merged.Length; i++)
        {
            var amount = merged[i];
            if (amount is 0 || cumulative + amount < target) { cumulative += amount; continue; }
            double? lower = i > 0 ? bounds[i - 1] : null, upper = i < bounds.Length ? bounds[i] : null;
            if (min is { } low) lower = lower is { } bound ? Math.Max(bound, low) : low;
            if (max is { } high) upper = upper is { } bound ? Math.Min(bound, high) : high;
            if (lower is not { } from || upper is not { } to || from > to) return null;
            return from + (to - from) * (target - cumulative) / amount;
        }
        return null;
    }

    /// <summary>
    /// The value of the latest point by timestamp, across every series in the bucket. Points recorded at
    /// that same instant with different values leave no single latest value.
    /// </summary>
    internal static double? Latest(IEnumerable<(ulong Time, double? Value)> points)
    {
        var all = points.ToList();
        if (all.Count is 0) return null;
        var latest = all.Max(static p => p.Time);
        var values = all.Where(p => p.Time == latest).Select(static p => p.Value).Distinct().ToList();
        return values.Count == 1 ? values[0] : null;
    }
}
