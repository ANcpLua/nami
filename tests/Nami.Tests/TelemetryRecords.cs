using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace Nami.Tests;

[PublicAPI]
public sealed record LogRecord(
    ResourceRecord Resource,
    InstrumentationScopeRecord? Scope,
    ulong TimeUnixNano,
    ulong ObservedTimeUnixNano,
    int SeverityNumber,
    string SeverityText,
    object? Body,
    IReadOnlyDictionary<string, object?> Attributes,
    uint DroppedAttributesCount,
    uint Flags,
    string? TraceId,
    string? SpanId,
    string EventName);

[PublicAPI]
public sealed record MetricRecord(
    ResourceRecord Resource,
    InstrumentationScopeRecord? Scope,
    string Name,
    string Description,
    string Unit,
    IReadOnlyDictionary<string, object?> Metadata,
    MetricData Data);

[PublicAPI]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(GaugeData), "gauge")]
[JsonDerivedType(typeof(SumData), "sum")]
[JsonDerivedType(typeof(HistogramData), "histogram")]
[JsonDerivedType(typeof(ExponentialHistogramData), "exponential")]
[JsonDerivedType(typeof(SummaryData), "summary")]
[JsonDerivedType(typeof(UnspecifiedMetricData), "unspecified")]
public abstract record MetricData;

[PublicAPI]
public sealed record GaugeData(IReadOnlyList<NumberPoint> Points) : MetricData;

[PublicAPI]
public sealed record SumData(IReadOnlyList<NumberPoint> Points, int AggregationTemporality, bool IsMonotonic)
    : MetricData;

[PublicAPI]
public sealed record HistogramData(IReadOnlyList<HistogramPoint> Points, int AggregationTemporality) : MetricData;

[PublicAPI]
public sealed record ExponentialHistogramData(
    IReadOnlyList<ExponentialHistogramPoint> Points,
    int AggregationTemporality) : MetricData;

[PublicAPI]
public sealed record SummaryData(IReadOnlyList<SummaryPoint> Points) : MetricData;

[PublicAPI]
public sealed record UnspecifiedMetricData : MetricData;

[PublicAPI]
public sealed record NumberPoint(
    IReadOnlyDictionary<string, object?> Attributes,
    ulong StartTimeUnixNano,
    ulong TimeUnixNano,
    object? Value,
    uint Flags,
    IReadOnlyList<ExemplarRecord> Exemplars);

[PublicAPI]
public sealed record HistogramPoint(
    IReadOnlyDictionary<string, object?> Attributes,
    ulong StartTimeUnixNano,
    ulong TimeUnixNano,
    ulong Count,
    double? Sum,
    IReadOnlyList<ulong> BucketCounts,
    IReadOnlyList<double> ExplicitBounds,
    IReadOnlyList<ExemplarRecord> Exemplars,
    uint Flags,
    double? Min,
    double? Max);

[PublicAPI]
public sealed record ExponentialBuckets(int Offset, IReadOnlyList<ulong> BucketCounts);

[PublicAPI]
public sealed record ExponentialHistogramPoint(
    IReadOnlyDictionary<string, object?> Attributes,
    ulong StartTimeUnixNano,
    ulong TimeUnixNano,
    ulong Count,
    double? Sum,
    int Scale,
    ulong ZeroCount,
    ExponentialBuckets? Positive,
    ExponentialBuckets? Negative,
    uint Flags,
    IReadOnlyList<ExemplarRecord> Exemplars,
    double? Min,
    double? Max,
    double ZeroThreshold);

[PublicAPI]
public sealed record QuantileValue(double Quantile, double Value);

[PublicAPI]
public sealed record SummaryPoint(
    IReadOnlyDictionary<string, object?> Attributes,
    ulong StartTimeUnixNano,
    ulong TimeUnixNano,
    ulong Count,
    double Sum,
    IReadOnlyList<QuantileValue> Quantiles,
    uint Flags);

[PublicAPI]
public sealed record ExemplarRecord(
    IReadOnlyDictionary<string, object?> FilteredAttributes,
    ulong TimeUnixNano,
    object? Value,
    string? TraceId,
    string? SpanId);

[PublicAPI]
public sealed record ExportReceipt(string Signal, long Accepted, long Rejected, string? Error)
{
    public OtlpTransport Transport { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>The uncompressed protobuf export payload, retained before record decoding.</summary>
/// <remarks>Unknown protobuf fields and records rejected by the decoder remain in this payload.</remarks>
[PublicAPI]
public sealed record OtlpExportRequest(string Signal, ReadOnlyMemory<byte> Payload)
{
    public OtlpTransport Transport { get; init; }
}

[PublicAPI]
public enum OtlpTransport
{
    HttpProtobuf,
    Grpc
}

[PublicAPI]
public sealed record TelemetrySnapshot(
    IReadOnlyList<SpanRecord> Spans,
    IReadOnlyList<LogRecord> Logs,
    IReadOnlyList<MetricRecord> Metrics,
    IReadOnlyList<ExportReceipt> Receipts)
{
    public IReadOnlyList<OtlpExportRequest> Requests { get; init; } = [];
}
