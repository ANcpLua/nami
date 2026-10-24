using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nami.Api.Contracts.Common;
using Nami.Api.Contracts.OTel.Metrics;
using ContractAttribute = Nami.Api.Contracts.Common.Attribute;

namespace Nami.Collector.Storage;

/// <summary>
///     The ingestion boundary. Decoding hands storage OTLP attribute values and generated contract metric
///     data; both become storage's own records here, before anything is encoded or hashed, so neither a
///     contract serializer nor a contract change can reach persisted bytes or identifiers.
/// </summary>
internal static class StoredRecordMapper
{
    // Decoded values map straight to storage's record, without the contract value in between; the
    // persisted bytes are the same: lossless Int64, any double, raw bytes, maps in ordinal key order.
    internal static StoredAttributeValue? ToStored(OtlpAttributeValue value) => value.Kind switch
    {
        OtlpAttributeValueKind.Empty => null,
        OtlpAttributeValueKind.String => new StoredAttributeValue.StringValue(value.AsString()!),
        OtlpAttributeValueKind.Bool => new StoredAttributeValue.BoolValue(value.AsBool()!.Value),
        OtlpAttributeValueKind.Int => new StoredAttributeValue.ObjectValue(new StoredIntValue
        {
            Value = value.AsInt64()!.Value
        }),
        OtlpAttributeValueKind.Double => new StoredAttributeValue.ObjectValue(
            new StoredDoubleValue { Value = value.AsDouble()!.Value }),
        OtlpAttributeValueKind.Bytes => new StoredAttributeValue.ObjectValue(
            new StoredBytesValue { Base64 = value.AsBytes()! }),
        OtlpAttributeValueKind.Array => new StoredAttributeValue.ArrayValue([
            .. value.AsArray()!.Select(static item => ToStored(item))
        ]),
        OtlpAttributeValueKind.KeyValueList => new StoredAttributeValue.ObjectValue(new StoredKeyValueListValue
        {
            Values = value.AsKeyValueList()!.OrderBy(static item => item.Key, StringComparer.Ordinal)
                .ToDictionary(static item => item.Key, static item => ToStored(item.Value), StringComparer.Ordinal)
        }),
        _ => throw new InvalidDataException("Unknown OTLP attribute value shape.")
    };

    internal static StoredAttributeValue? ToStored(AttributeValue? value) => value switch
    {
        null => null,
        AttributeValue.StringValue text => new StoredAttributeValue.StringValue(text.Value),
        AttributeValue.BoolValue flag => new StoredAttributeValue.BoolValue(flag.Value),
        AttributeValue.ArrayValue array => new StoredAttributeValue.ArrayValue([
            .. array.Value.Select(static item => ToStored(item))
        ]),
        AttributeValue.ObjectValue tagged => new StoredAttributeValue.ObjectValue(ToStoredObject(tagged.Value))
    };

    internal static StoredMetricPointData ToStored(MetricPointData data) => data.Value switch
    {
        MetricNumberPointData number => new StoredNumberPointData
        {
            Value = number.Value is { } value ? ToStoredNumber(value) : null
        },
        MetricHistogramPointData histogram => new StoredHistogramPointData
        {
            Count = histogram.Count,
            Sum = histogram.Sum,
            Min = histogram.Min,
            Max = histogram.Max,
            ExplicitBounds = histogram.ExplicitBounds,
            BucketCounts = histogram.BucketCounts
        },
        MetricExponentialHistogramPointData exponential => new StoredExponentialHistogramPointData
        {
            Count = exponential.Count,
            Sum = exponential.Sum,
            Min = exponential.Min,
            Max = exponential.Max,
            Scale = exponential.Scale,
            ZeroCount = exponential.ZeroCount,
            ZeroThreshold = exponential.ZeroThreshold,
            Positive = ToStored(exponential.Positive),
            Negative = ToStored(exponential.Negative)
        },
        MetricSummaryPointData summary => new StoredSummaryPointData
        {
            Count = summary.Count,
            Sum = summary.Sum,
            QuantileValues =
            [
                .. summary.QuantileValues.Select(static quantile =>
                    new StoredQuantileValue { Quantile = quantile.Quantile, Value = quantile.Value })
            ]
        },
        _ => throw new InvalidDataException("Unknown OTLP metric data shape.")
    };

    /// <summary>Exemplars keep producer order; it is part of the observation.</summary>
    internal static IReadOnlyList<StoredExemplar> ToStored(IReadOnlyList<MetricExemplar> exemplars) =>
    [
        .. exemplars.Select(static exemplar => new StoredExemplar
        {
            TimeUnixNano = exemplar.TimeUnixNano,
            Value = ToStoredNumber(exemplar.Value),
            FilteredAttributes = ToStored(exemplar.FilteredAttributes),
            TraceId = exemplar.TraceId,
            SpanId = exemplar.SpanId
        })
    ];

    internal static IReadOnlyList<StoredAttribute> ToStored(IReadOnlyList<ContractAttribute> attributes) =>
    [
        .. attributes.OrderBy(static attribute => attribute.Key, StringComparer.Ordinal)
            .Select(static attribute => new StoredAttribute { Key = attribute.Key, Value = ToStored(attribute.Value) })
    ];

    private static StoredObjectValue ToStoredObject(AttributeObjectValue value) => value.Value switch
    {
        AttributeIntValue integer => new StoredIntValue { Value = integer.Value },
        AttributeDoubleValue number => new StoredDoubleValue { Value = number.Value },
        AttributeBytesValue bytes => new StoredBytesValue { Base64 = bytes.Base64 },
        // Persisted maps keep ordinal key order, whatever order the producer used.
        AttributeKeyValueListValue map => new StoredKeyValueListValue
        {
            Values = map.Values.OrderBy(static item => item.Key, StringComparer.Ordinal)
                .ToDictionary(static item => item.Key, static item => ToStored(item.Value), StringComparer.Ordinal)
        },
        _ => throw new InvalidDataException("Unknown OTLP attribute object shape.")
    };

    private static StoredNumberValue ToStoredNumber(MetricNumberValue value) => value.Value switch
    {
        AttributeIntValue integer => new StoredIntValue { Value = integer.Value },
        AttributeDoubleValue number => new StoredDoubleValue { Value = number.Value },
        _ => throw new InvalidDataException("Unknown OTLP metric numeric representation.")
    };

    private static StoredExponentialBuckets? ToStored(MetricExponentialBuckets? buckets) => buckets is null
        ? null
        : new StoredExponentialBuckets { Offset = buckets.Offset, BucketCounts = buckets.BucketCounts };
}
