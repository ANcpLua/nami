using System.Collections.Generic;

namespace Nami.Collector.Storage;

/// <summary>
///     An original metric data point as metric_observations.data_json persists it and the observation and
///     measurement identifiers hash it. Counts and int64 values are decimal text, floats keep NaN and the
///     infinities as named literals, and the shapes are discriminated by <c>type</c>. Storage owns this
///     format; the API contract maps from it on read.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(StoredNumberPointData), "number")]
[JsonDerivedType(typeof(StoredHistogramPointData), "histogram")]
[JsonDerivedType(typeof(StoredExponentialHistogramPointData), "exponential_histogram")]
[JsonDerivedType(typeof(StoredSummaryPointData), "summary")]
internal abstract class StoredMetricPointData
{
    private protected StoredMetricPointData()
    {
    }
}

internal sealed class StoredNumberPointData : StoredMetricPointData
{
    /// <summary>Absent when the producer recorded neither an int64 nor a float64.</summary>
    [JsonPropertyName("value")]
    public StoredNumberValue? Value { get; init; }
}

internal sealed class StoredHistogramPointData : StoredMetricPointData
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    [JsonPropertyName("count")]
    public required ulong Count { get; init; }

    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    [JsonPropertyName("sum")]
    public double? Sum { get; init; }

    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    [JsonPropertyName("min")]
    public double? Min { get; init; }

    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    [JsonPropertyName("max")]
    public double? Max { get; init; }

    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    [JsonPropertyName("explicit_bounds")]
    public required IReadOnlyList<double> ExplicitBounds { get; init; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    [JsonPropertyName("bucket_counts")]
    public required IReadOnlyList<ulong> BucketCounts { get; init; }
}

internal sealed class StoredExponentialHistogramPointData : StoredMetricPointData
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    [JsonPropertyName("count")]
    public required ulong Count { get; init; }

    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    [JsonPropertyName("sum")]
    public double? Sum { get; init; }

    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    [JsonPropertyName("min")]
    public double? Min { get; init; }

    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    [JsonPropertyName("max")]
    public double? Max { get; init; }

    [JsonPropertyName("scale")] public required int Scale { get; init; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    [JsonPropertyName("zero_count")]
    public required ulong ZeroCount { get; init; }

    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    [JsonPropertyName("zero_threshold")]
    public required double ZeroThreshold { get; init; }

    [JsonPropertyName("positive")] public StoredExponentialBuckets? Positive { get; init; }

    [JsonPropertyName("negative")] public StoredExponentialBuckets? Negative { get; init; }
}

internal sealed class StoredExponentialBuckets
{
    [JsonPropertyName("offset")] public required int Offset { get; init; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    [JsonPropertyName("bucket_counts")]
    public required IReadOnlyList<ulong> BucketCounts { get; init; }
}

internal sealed class StoredSummaryPointData : StoredMetricPointData
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    [JsonPropertyName("count")]
    public required ulong Count { get; init; }

    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    [JsonPropertyName("sum")]
    public required double Sum { get; init; }

    [JsonPropertyName("quantile_values")]
    public required IReadOnlyList<StoredQuantileValue> QuantileValues { get; init; }
}

internal sealed class StoredQuantileValue
{
    [JsonPropertyName("quantile")] public required double Quantile { get; init; }

    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    [JsonPropertyName("value")]
    public required double Value { get; init; }
}

/// <summary>A complete exemplar as metric_observations.exemplars_json persists it.</summary>
internal sealed class StoredExemplar
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    [JsonPropertyName("time_unix_nano")]
    public required ulong TimeUnixNano { get; init; }

    [JsonPropertyName("value")] public required StoredNumberValue Value { get; init; }

    [JsonPropertyName("filtered_attributes")]
    public required IReadOnlyList<StoredAttribute> FilteredAttributes { get; init; }

    [JsonPropertyName("trace_id")] public string? TraceId { get; init; }

    [JsonPropertyName("span_id")] public string? SpanId { get; init; }
}

/// <summary>A keyed attribute in a persisted list: exemplar filtered attributes and instrument metadata.</summary>
internal sealed class StoredAttribute
{
    [JsonPropertyName("key")] public required string Key { get; init; }

    /// <summary>The empty value is written as <c>null</c>, never omitted.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [JsonPropertyName("value")]
    public required StoredAttributeValue? Value { get; init; }
}
