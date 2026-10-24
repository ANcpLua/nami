using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Nami.Collector.Ingestion.Primitives;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;
using CommonContract = Nami.Api.Contracts.Common;
using MetricContract = Nami.Api.Contracts.OTel.Metrics;
using ProtoExemplar = OpenTelemetry.Proto.Metrics.V1.Exemplar;
using ProtoMetric = OpenTelemetry.Proto.Metrics.V1.Metric;
using ProtoNumberDataPoint = OpenTelemetry.Proto.Metrics.V1.NumberDataPoint;
using ProtoTemporality = OpenTelemetry.Proto.Metrics.V1.AggregationTemporality;

namespace Nami.Collector.Ingestion;

internal static partial class OtlpConverter
{
    public static MetricIngestionBatch ConvertMetrics(ExportMetricsServiceRequest request)
    {
        var points = new List<MetricPointIngestionRecord>();
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        long rejected = 0;

        void Reject(long count, InvalidDataException error)
        {
            rejected += count;
            reasons.Add(error.Message);
        }

        foreach (var resourceMetrics in request.ResourceMetrics)
        {
            ResourceProjection resource;
            string resourceIdentity;
            try
            {
                resource = ExtractResourceProjection(resourceMetrics.Resource);
                resourceIdentity = MetricAttributeIdentity(resourceMetrics.Resource?.Attributes ?? []);
            }
            catch (InvalidDataException error)
            {
                Reject(resourceMetrics.ScopeMetrics.Sum(static scope => scope.Metrics.Sum(CountMetricPoints)), error);
                continue;
            }

            var serviceName = ExtractServiceNameFromProto(resourceMetrics.Resource);
            var projectId = ExtractProjectIdHintFromProto(resourceMetrics.Resource);
            foreach (var scopeMetrics in resourceMetrics.ScopeMetrics)
            {
                Dictionary<string, OtlpAttributeValue> scopeAttributes;
                string scopeIdentity;
                try
                {
                    scopeAttributes = ConvertMetadataAttributes(scopeMetrics.Scope?.Attributes ?? []);
                    scopeIdentity = MetricAttributeIdentity(scopeMetrics.Scope?.Attributes ?? []);
                }
                catch (InvalidDataException error)
                {
                    Reject(scopeMetrics.Metrics.Sum(CountMetricPoints), error);
                    continue;
                }

                foreach (var metric in scopeMetrics.Metrics)
                {
                    MetricStreamMetadata stream;
                    try
                    {
                        if (string.IsNullOrWhiteSpace(metric.Name))
                            throw new InvalidDataException("OTLP metric name must not be empty.");
                        stream = new MetricStreamMetadata(projectId, metric.Name, NullIfEmpty(metric.Unit),
                            NullIfEmpty(metric.Description), serviceName,
                            NullIfEmpty(scopeMetrics.SchemaUrl) ?? NullIfEmpty(resourceMetrics.SchemaUrl),
                            resource.Attributes, resource.EntityRefs, NullIfEmpty(resourceMetrics.SchemaUrl),
                            resourceMetrics.Resource?.DroppedAttributesCount ?? 0,
                            NullIfEmpty(scopeMetrics.Scope?.Name),
                            NullIfEmpty(scopeMetrics.Scope?.Version), NullIfEmpty(scopeMetrics.SchemaUrl),
                            scopeAttributes,
                            scopeMetrics.Scope?.DroppedAttributesCount ?? 0,
                            ToContractAttributes(ConvertMetadataAttributes(metric.Metadata)), resourceIdentity,
                            scopeIdentity);
                    }
                    catch (InvalidDataException error)
                    {
                        Reject(CountMetricPoints(metric), error);
                        continue;
                    }

                    // A malformed point must not discard valid siblings. End time is required
                    // for every metric kind, unlike optional log event time.
                    // https://opentelemetry.io/docs/specs/otlp/#partial-success
                    // https://github.com/open-telemetry/opentelemetry-proto/blob/v1.11.1/opentelemetry/proto/metrics/v1/metrics.proto
                    void AddPoints<T>(IEnumerable<T> source, Func<T, MetricPointIngestionRecord> convert)
                    {
                        foreach (var point in source)
                        {
                            try
                            {
                                var record = convert(point);
                                if (record.TimeUnixNano == 0)
                                    throw new InvalidDataException(
                                        $"Metric '{metric.Name}' requires a nonzero time_unix_nano.");
                                points.Add(record);
                            }
                            catch (InvalidDataException error) { Reject(1, error); }
                        }
                    }

                    switch (metric.DataCase)
                    {
                        case Metric.DataOneofCase.Gauge:
                            AddPoints(metric.Gauge.DataPoints, point => CreateNumberPoint(stream, point,
                                MetricKind.Gauge, MetricTemporality.Unspecified, false));
                            break;
                        case Metric.DataOneofCase.Sum:
                            AddPoints(metric.Sum.DataPoints, point => CreateNumberPoint(stream, point, MetricKind.Sum,
                                ConvertTemporality(metric.Sum.AggregationTemporality), metric.Sum.IsMonotonic));
                            break;
                        case Metric.DataOneofCase.Histogram:
                            AddPoints(metric.Histogram.DataPoints, point => CreateHistogramPoint(stream, point,
                                ConvertTemporality(metric.Histogram.AggregationTemporality)));
                            break;
                        case Metric.DataOneofCase.ExponentialHistogram:
                            AddPoints(metric.ExponentialHistogram.DataPoints, point => CreateExponentialHistogramPoint(
                                stream, point,
                                ConvertTemporality(metric.ExponentialHistogram.AggregationTemporality)));
                            break;
                        case Metric.DataOneofCase.Summary:
                            AddPoints(metric.Summary.DataPoints, point => CreateSummaryPoint(stream, point));
                            break;
                        default:
                            Reject(CountMetricPoints(metric),
                                new InvalidDataException(
                                    $"OTLP metric '{metric.Name}' carries an unknown data point set."));
                            break;
                    }
                }
            }
        }

        return new MetricIngestionBatch(points, rejected, reasons.Count == 0 ? null : string.Join("; ", reasons));
    }

    private static long CountMetricPoints(ProtoMetric metric) => metric.DataCase switch
    {
        Metric.DataOneofCase.Gauge => metric.Gauge.DataPoints.Count,
        Metric.DataOneofCase.Sum => metric.Sum.DataPoints.Count,
        Metric.DataOneofCase.Histogram => metric.Histogram.DataPoints.Count,
        Metric.DataOneofCase.ExponentialHistogram => metric.ExponentialHistogram.DataPoints.Count,
        Metric.DataOneofCase.Summary => metric.Summary.DataPoints.Count,
        _ => 0
    };

    private static MetricPointIngestionRecord CreateHistogramPoint(MetricStreamMetadata stream,
        HistogramDataPoint point, MetricTemporality temporality)
    {
        if (point.BucketCounts.Count != 0 && point.ExplicitBounds.Count + 1 != point.BucketCounts.Count)
            throw new InvalidDataException(
                $"OTLP histogram '{stream.MetricName}' must carry one more count than explicit bound.");
        ValidateAscendingBounds(stream.MetricName, point.ExplicitBounds);
        return CreateRecord(stream, point.Attributes, MetricKind.Histogram, temporality, false,
                new MetricContract.MetricHistogramPointData
                {
                    Count = point.Count,
                    Sum = point.HasSum ? point.Sum : null,
                    Min = point.HasMin ? point.Min : null,
                    Max = point.HasMax ? point.Max : null,
                    ExplicitBounds = point.ExplicitBounds.ToArray(),
                    BucketCounts = point.BucketCounts.ToArray()
                }) with
            {
                TimeUnixNano = point.TimeUnixNano,
                StartTimeUnixNano = NullIfZero(point.StartTimeUnixNano),
                Flags = point.Flags,
                Exemplars = ConvertExemplars(point.Exemplars)
            };
    }

    private static MetricPointIngestionRecord CreateExponentialHistogramPoint(MetricStreamMetadata stream,
        ExponentialHistogramDataPoint point, MetricTemporality temporality) =>
        CreateRecord(stream, point.Attributes, MetricKind.ExponentialHistogram, temporality, false,
                new MetricContract.MetricExponentialHistogramPointData
                {
                    Count = point.Count,
                    Sum = point.HasSum ? point.Sum : null,
                    Min = point.HasMin ? point.Min : null,
                    Max = point.HasMax ? point.Max : null,
                    Scale = point.Scale,
                    ZeroCount = point.ZeroCount,
                    ZeroThreshold = point.ZeroThreshold,
                    Positive =
                        point.Positive is null
                            ? null
                            : new MetricContract.MetricExponentialBuckets
                            {
                                Offset = point.Positive.Offset, BucketCounts = point.Positive.BucketCounts.ToArray()
                            },
                    Negative =
                        point.Negative is null
                            ? null
                            : new MetricContract.MetricExponentialBuckets
                            {
                                Offset = point.Negative.Offset, BucketCounts = point.Negative.BucketCounts.ToArray()
                            }
                }) with
            {
                TimeUnixNano = point.TimeUnixNano,
                StartTimeUnixNano = NullIfZero(point.StartTimeUnixNano),
                Flags = point.Flags,
                Exemplars = ConvertExemplars(point.Exemplars)
            };

    private static MetricPointIngestionRecord CreateSummaryPoint(MetricStreamMetadata stream,
        SummaryDataPoint point) =>
        CreateRecord(stream, point.Attributes, MetricKind.Summary, MetricTemporality.Unspecified, false,
                new MetricContract.MetricSummaryPointData
                {
                    Count = point.Count,
                    Sum = point.Sum,
                    QuantileValues =
                        point.QuantileValues.Select(static value =>
                                new MetricContract.MetricQuantileValue
                                    {
                                        Quantile = value.Quantile, Value = value.Value
                                    })
                            .ToArray()
                }) with
            {
                TimeUnixNano = point.TimeUnixNano,
                StartTimeUnixNano = NullIfZero(point.StartTimeUnixNano),
                Flags = point.Flags
            };

    private static MetricPointIngestionRecord CreateNumberPoint(MetricStreamMetadata stream, ProtoNumberDataPoint point,
        MetricKind kind, MetricTemporality temporality, bool monotonic) =>
        CreateRecord(stream, point.Attributes, kind, temporality, monotonic, new MetricContract.MetricNumberPointData
            {
                Value = point.ValueCase switch
                {
                    NumberDataPoint.ValueOneofCase.AsInt =>
                        new CommonContract.AttributeIntValue { Value = point.AsInt },
                    NumberDataPoint.ValueOneofCase.AsDouble => new CommonContract.AttributeDoubleValue
                    {
                        Value = point.AsDouble
                    },
                    _ => null
                }
            }) with
            {
                TimeUnixNano = point.TimeUnixNano,
                StartTimeUnixNano = NullIfZero(point.StartTimeUnixNano),
                Flags = point.Flags,
                Exemplars = ConvertExemplars(point.Exemplars)
            };

    private static IReadOnlyList<MetricContract.MetricExemplar> ConvertExemplars(IEnumerable<ProtoExemplar> exemplars)
    {
        var converted = new NormalizedMetricExemplars();
        foreach (var exemplar in exemplars)
        {
            var attributes = ExtractMetricAttributes(exemplar.FilteredAttributes);
            converted.Renames.AddRange(NormalizedAttributes.Collect([attributes]));
            converted.Add(new MetricContract.MetricExemplar
            {
                TimeUnixNano = exemplar.TimeUnixNano,
                Value = exemplar.ValueCase switch
                {
                    Exemplar.ValueOneofCase.AsInt => new CommonContract.AttributeIntValue { Value = exemplar.AsInt },
                    Exemplar.ValueOneofCase.AsDouble => new CommonContract.AttributeDoubleValue
                    {
                        Value = exemplar.AsDouble
                    },
                    _ => throw new InvalidDataException("OTLP metric exemplars require a numeric value.")
                },
                FilteredAttributes = ToContractAttributes(attributes),
                TraceId = MetricExemplarId(exemplar.TraceId, 16, "trace_id"),
                SpanId = MetricExemplarId(exemplar.SpanId, 8, "span_id")
            });
        }

        return converted;
    }

    private static string? MetricExemplarId(ByteString bytes, int length, string field)
    {
        if (bytes.IsEmpty) return null;
        if (bytes.Length != length || bytes.Span.IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException($"OTLP exemplar {field} must be a nonzero {length}-byte identifier.");
        return Convert.ToHexStringLower(bytes.Span);
    }

    private static IReadOnlyList<CommonContract.Attribute> ToContractAttributes(
        IReadOnlyDictionary<string, OtlpAttributeValue> attributes)
    {
        var converted = new NormalizedContractAttributes(NormalizedAttributes.Collect([attributes]));
        foreach (var pair in attributes.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            converted.Add(new CommonContract.Attribute { Key = pair.Key, Value = pair.Value.ToContract() });
        return converted;
    }

    private static MetricPointIngestionRecord CreateRecord(MetricStreamMetadata stream,
        RepeatedField<KeyValue> attributes, MetricKind kind,
        MetricTemporality temporality, bool monotonic, MetricContract.MetricPointData data) => new()
    {
        ProjectIdHint = stream.ProjectIdHint,
        MetricName = stream.MetricName,
        Kind = kind,
        Temporality = temporality,
        IsMonotonic = monotonic,
        Unit = stream.Unit,
        Description = stream.Description,
        ServiceName = stream.ServiceName,
        SchemaUrl = stream.SchemaUrl,
        ResourceSchemaUrl = stream.ResourceSchemaUrl,
        ResourceDroppedAttributesCount = stream.ResourceDroppedAttributesCount,
        ScopeName = stream.ScopeName,
        ScopeVersion = stream.ScopeVersion,
        ScopeSchemaUrl = stream.ScopeSchemaUrl,
        ScopeAttributes = stream.ScopeAttributes,
        ScopeDroppedAttributesCount = stream.ScopeDroppedAttributesCount,
        Attributes = ExtractMetricAttributes(attributes),
        ResourceAttributes = stream.ResourceAttributes,
        ResourceEntityRefs = stream.ResourceEntityRefs,
        ResourceIdentity = stream.ResourceIdentity,
        ScopeIdentity = stream.ScopeIdentity,
        AttributesIdentity = MetricAttributeIdentity(attributes),
        InstrumentMetadata = stream.InstrumentMetadata,
        TimeUnixNano = 0,
        Data = data
    };

    private static Dictionary<string, OtlpAttributeValue> ExtractMetricAttributes(
        RepeatedField<KeyValue> attributes)
    {
        ValidateAttributeKeys(attributes);
        var result = new NormalizedAttributes();
        foreach (var attribute in attributes)
        {
            if (string.IsNullOrEmpty(attribute.Key)) continue;
            var renamed = DeprecatedAttributeNormalizer.TryNormalize(attribute.Key, out var key);
            NormalizedAttributes.RecordRename(result, attribute.Key, key);
            if (!AttributeKeySets.IsPersistable(key))
            {
                NamiCollectorMetrics.AttributeDropped(key);
                continue;
            }

            SetNormalizedAttribute(result, key, ConvertProtoAnyValue(attribute.Value, key), renamed);
        }

        return result;
    }

    private static void ValidateAscendingBounds(string metricName, RepeatedField<double> bounds)
    {
        var previous = double.NegativeInfinity;
        foreach (var bound in bounds)
        {
            if (!double.IsFinite(bound) || bound <= previous)
                throw new InvalidDataException(
                    $"OTLP histogram '{metricName}' explicit bounds must be finite and strictly ascending.");
            previous = bound;
        }
    }

    private static MetricTemporality ConvertTemporality(ProtoTemporality temporality) => temporality switch
    {
        ProtoTemporality.Delta => MetricTemporality.Delta,
        ProtoTemporality.Cumulative => MetricTemporality.Cumulative,
        _ => MetricTemporality.Unspecified
    };

    private static ulong? NullIfZero(ulong value) => value == 0 ? null : value;

    private readonly record struct MetricStreamMetadata(
        string? ProjectIdHint,
        string MetricName,
        string? Unit,
        string? Description,
        string ServiceName,
        string? SchemaUrl,
        Dictionary<string, OtlpAttributeValue> ResourceAttributes,
        IReadOnlyList<ResourceEntityRefIngestionRecord> ResourceEntityRefs,
        string? ResourceSchemaUrl,
        uint ResourceDroppedAttributesCount,
        string? ScopeName,
        string? ScopeVersion,
        string? ScopeSchemaUrl,
        IReadOnlyDictionary<string, OtlpAttributeValue> ScopeAttributes,
        uint ScopeDroppedAttributesCount,
        IReadOnlyList<CommonContract.Attribute> InstrumentMetadata,
        string ResourceIdentity,
        string ScopeIdentity);
}
