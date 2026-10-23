using Nami.Api.Contracts.Common;
using Nami.Api.Contracts.OTel.Metrics;
using Nami.Collector.Storage;
using ContractAttribute = Nami.Api.Contracts.Common.Attribute;

namespace Nami.Collector.Mapping;

/// <summary>
/// The read boundary for persisted values: storage decodes its own records and they become the
/// generated contract types here. Persisted bytes never pass through a contract converter.
/// </summary>
internal static class StoredValueMapper
{
    public static AttributeValue? ToContract(StoredAttributeValue? value) => value switch
    {
        null => null,
        StoredAttributeValue.StringValue text => new AttributeValue.StringValue(text.Value),
        StoredAttributeValue.BoolValue flag => new AttributeValue.BoolValue(flag.Value),
        StoredAttributeValue.ArrayValue array => new AttributeValue.ArrayValue([.. array.Items.Select(static item => ToContract(item))]),
        StoredAttributeValue.ObjectValue tagged => new AttributeValue.ObjectValue(ToContract(tagged.Value)),
        _ => throw new InvalidDataException("Unknown stored attribute value shape.")
    };

    public static MetricPointData? ToContract(StoredMetricPointData? data) => data switch
    {
        null => null,
        StoredNumberPointData number => new MetricNumberPointData
        {
            Value = number.Value is { } value ? ToContract(value) : null
        },
        StoredHistogramPointData histogram => new MetricHistogramPointData
        {
            Count = histogram.Count, Sum = histogram.Sum, Min = histogram.Min, Max = histogram.Max,
            ExplicitBounds = histogram.ExplicitBounds, BucketCounts = histogram.BucketCounts
        },
        StoredExponentialHistogramPointData exponential => new MetricExponentialHistogramPointData
        {
            Count = exponential.Count, Sum = exponential.Sum, Min = exponential.Min, Max = exponential.Max,
            Scale = exponential.Scale, ZeroCount = exponential.ZeroCount, ZeroThreshold = exponential.ZeroThreshold,
            Positive = ToContract(exponential.Positive), Negative = ToContract(exponential.Negative)
        },
        StoredSummaryPointData summary => new MetricSummaryPointData
        {
            Count = summary.Count, Sum = summary.Sum,
            QuantileValues = [.. summary.QuantileValues.Select(static quantile => new MetricQuantileValue
            {
                Quantile = quantile.Quantile, Value = quantile.Value
            })]
        },
        _ => throw new InvalidDataException("Unknown stored metric data shape.")
    };

    public static IReadOnlyList<MetricExemplar>? ToContract(IReadOnlyList<StoredExemplar>? exemplars) => exemplars is null
        ? null
        :
        [
            .. exemplars.Select(static exemplar => new MetricExemplar
            {
                TimeUnixNano = exemplar.TimeUnixNano, Value = ToContract(exemplar.Value),
                FilteredAttributes = ToContract(exemplar.FilteredAttributes)!,
                TraceId = exemplar.TraceId, SpanId = exemplar.SpanId
            })
        ];

    public static IReadOnlyList<ContractAttribute>? ToContract(IReadOnlyList<StoredAttribute>? attributes) => attributes is null
        ? null
        : [.. attributes.Select(static attribute => new ContractAttribute { Key = attribute.Key, Value = ToContract(attribute.Value) })];

    private static AttributeObjectValue ToContract(StoredObjectValue value) => value switch
    {
        StoredIntValue integer => new AttributeIntValue { Value = integer.Value },
        StoredDoubleValue number => new AttributeDoubleValue { Value = number.Value },
        StoredBytesValue bytes => new AttributeBytesValue { Base64 = bytes.Base64 },
        StoredKeyValueListValue map => new AttributeKeyValueListValue
        {
            Values = map.Values.ToDictionary(static item => item.Key, static item => ToContract(item.Value), StringComparer.Ordinal)
        },
        _ => throw new InvalidDataException("Unknown stored attribute object shape.")
    };

    private static MetricNumberValue ToContract(StoredNumberValue value) => value switch
    {
        StoredIntValue integer => new AttributeIntValue { Value = integer.Value },
        StoredDoubleValue number => new AttributeDoubleValue { Value = number.Value },
        _ => throw new InvalidDataException("Unknown stored metric numeric representation.")
    };

    private static MetricExponentialBuckets? ToContract(StoredExponentialBuckets? buckets) => buckets is null
        ? null
        : new MetricExponentialBuckets { Offset = buckets.Offset, BucketCounts = buckets.BucketCounts };
}
