namespace Nami.Collector.Storage;

/// <summary>Builds the disposable float64 query projection from the authoritative observation.</summary>
internal static class MetricProjection
{
    private const int MaximumMaterializedBuckets = 4096;

    internal static MetricPointRow Project(MetricObservationRow observation, bool hasConflict)
    {
        var row = new MetricPointRow
        {
            ProjectId = observation.ProjectId, SeriesId = observation.SeriesId,
            TimeUnixNano = observation.TimeUnixNano, StartTimeUnixNano = observation.StartTimeUnixNano,
            HasConflict = hasConflict ? (byte)1 : (byte)0
        };
        if (hasConflict) return row;
        if (observation.DataJson is null)
        {
            var historical = JsonSerializer.Deserialize(observation.HistoricalProjectionJson ??
                throw new InvalidDataException("Historical metric observation has no retained projection."),
                StorageJsonSerializerContext.Default.HistoricalMetricProjection) ??
                throw new InvalidDataException("Historical metric projection is null.");
            return row with
            {
                Value = historical.Value, HistogramCount = historical.HistogramCount,
                HistogramSum = historical.HistogramSum, HistogramMin = historical.HistogramMin,
                HistogramMax = historical.HistogramMax, BucketBoundsJson = historical.BucketBoundsJson,
                BucketCountsJson = historical.BucketCountsJson
            };
        }
        if ((observation.Flags & 1) != 0) return row;
        var data = JsonSerializer.Deserialize(observation.DataJson, StorageJsonSerializerContext.Default.StoredMetricPointData) ??
            throw new InvalidDataException("Canonical metric data is null.");
        return data switch
        {
            StoredNumberPointData number => row with { Value = number.Value switch
            {
                StoredIntValue integer => integer.Value,
                StoredDoubleValue floating => floating.Value,
                null => null,
                _ => throw new InvalidDataException("Unknown metric numeric representation.")
            } },
            StoredHistogramPointData histogram => row with
            {
                HistogramCount = histogram.Count, HistogramSum = histogram.Sum,
                HistogramMin = histogram.Min, HistogramMax = histogram.Max,
                BucketBoundsJson = JsonSerializer.Serialize(histogram.ExplicitBounds, StorageJsonSerializerContext.Default.Doubles),
                BucketCountsJson = JsonSerializer.Serialize(histogram.BucketCounts, StorageJsonSerializerContext.Default.Counts),
                ProjectionUnavailableReason = histogram.Count > 0 && histogram.BucketCounts.Count == 0
                    ? "The histogram contains aggregate fields but no distribution buckets." : null
            },
            StoredExponentialHistogramPointData exponential => ProjectExponential(row, exponential),
            StoredSummaryPointData => row with { ProjectionUnavailableReason = "OTLP summary quantiles cannot be losslessly reduced across observations." },
            _ => throw new InvalidDataException("Unknown canonical metric data shape.")
        };
    }

    private static MetricPointRow ProjectExponential(MetricPointRow row, StoredExponentialHistogramPointData point)
    {
        row = row with
        {
            HistogramCount = point.Count, HistogramSum = point.Sum,
            HistogramMin = point.Min, HistogramMax = point.Max
        };
        var populated = (long)(point.Positive?.BucketCounts.Count ?? 0) +
            (point.Negative?.BucketCounts.Count ?? 0) + (point.ZeroCount > 0 ? 1 : 0);
        if (populated > MaximumMaterializedBuckets || point.Scale == int.MinValue)
            return Unavailable("Native exponential histogram exceeds the bounded bucket projection.");
        var logBase = Math.ScaleB(Math.Log(2), -point.Scale);
        var bounds = new List<double>((int)populated);
        var counts = new List<ulong>((int)populated + 1);
        if (point.Negative is { } negative)
            for (var i = negative.BucketCounts.Count - 1; i >= 0; i--)
            {
                bounds.Add(-Math.Exp(logBase * ((long)negative.Offset + i)));
                counts.Add(negative.BucketCounts[i]);
            }
        if (point.ZeroCount > 0) { bounds.Add(point.ZeroThreshold); counts.Add(point.ZeroCount); }
        if (point.Positive is { } positive)
            for (var i = 0; i < positive.BucketCounts.Count; i++)
            {
                bounds.Add(Math.Exp(logBase * ((long)positive.Offset + i + 1)));
                counts.Add(positive.BucketCounts[i]);
            }
        if (bounds.Where((bound, index) => !double.IsFinite(bound) || index > 0 && bound <= bounds[index - 1]).Any())
            return Unavailable("Native exponential histogram bounds cannot be represented as finite, distinct float64 bounds.");
        if (bounds.Count == 0) return point.Count > 0
            ? Unavailable("The exponential histogram contains aggregate fields but no distribution buckets.") : row;
        counts.Add(0);
        return row with
        {
            BucketBoundsJson = JsonSerializer.Serialize<IReadOnlyList<double>>(bounds, StorageJsonSerializerContext.Default.Doubles),
            BucketCountsJson = JsonSerializer.Serialize<IReadOnlyList<ulong>>(counts, StorageJsonSerializerContext.Default.Counts)
        };

        MetricPointRow Unavailable(string reason) => row with { ProjectionUnavailableReason = reason };
    }
}
