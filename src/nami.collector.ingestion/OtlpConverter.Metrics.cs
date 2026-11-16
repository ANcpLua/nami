using Google.Protobuf.Collections;
using Nami.Collector.Ingestion.Primitives;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;
using CommonContract = Nami.Collector.Ingestion.Models;
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
            var resourceSchema = KeptOrNull(resourceMetrics.SchemaUrl);
            foreach (var scopeMetrics in resourceMetrics.ScopeMetrics)
            {
                NormalizedAttributes scopeAttributes;
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

                var scopeName = KeptOrNull(scopeMetrics.Scope?.Name);
                var scopeVersion = KeptOrNull(scopeMetrics.Scope?.Version);
                var scopeSchema = KeptOrNull(scopeMetrics.SchemaUrl);
                foreach (var metric in scopeMetrics.Metrics)
                {
                    MetricStreamMetadata stream;
                    try
                    {
                        if (string.IsNullOrWhiteSpace(metric.Name))
                            throw new InvalidDataException("OTLP metric name must not be empty.");
                        stream = new MetricStreamMetadata(projectId, Kept(metric.Name), KeptOrNull(metric.Unit),
                            KeptOrNull(metric.Description), serviceName,
                            scopeSchema ?? resourceSchema,
                            resource.Attributes, resource.EntityRefs, resourceSchema,
                            resourceMetrics.Resource?.DroppedAttributesCount ?? 0,
                            scopeName, scopeVersion, scopeSchema,
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

                    switch (metric.DataCase)
                    {
                        case ProtoMetric.DataOneofCase.Gauge:
                            AddPoints(metric.Gauge.DataPoints, point => CreateNumberPoint(stream, point,
                                MetricKind.Gauge, MetricTemporality.Unspecified, false));
                            break;
                        case ProtoMetric.DataOneofCase.Sum:
                            AddPoints(metric.Sum.DataPoints, point => CreateNumberPoint(stream, point, MetricKind.Sum,
                                ConvertTemporality(metric.Sum.AggregationTemporality), metric.Sum.IsMonotonic));
                            break;
                        case ProtoMetric.DataOneofCase.Histogram:
                            AddPoints(metric.Histogram.DataPoints, point => CreateHistogramPoint(stream, point,
                                ConvertTemporality(metric.Histogram.AggregationTemporality)));
                            break;
                        case ProtoMetric.DataOneofCase.ExponentialHistogram:
                            AddPoints(metric.ExponentialHistogram.DataPoints, point => CreateExponentialHistogramPoint(
                                stream, point,
                                ConvertTemporality(metric.ExponentialHistogram.AggregationTemporality)));
                            break;
                        case ProtoMetric.DataOneofCase.Summary:
                            AddPoints(metric.Summary.DataPoints, point => CreateSummaryPoint(stream, point));
                            break;
                        case ProtoMetric.DataOneofCase.None:
                        default:
                            Reject(CountMetricPoints(metric),
                                new InvalidDataException(
                                    $"OTLP metric '{stream.MetricName}' carries an unknown data point set."));
                            break;
                    }

                    continue;

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
                                if (record.TimeUnixNano is 0)
                                {
                                    throw new InvalidDataException(
                                        $"Metric '{stream.MetricName}' requires a nonzero time_unix_nano.");
                                }

                                points.Add(record);
                            }
                            catch (InvalidDataException error) { Reject(1, error); }
                        }
                    }
                }
            }
        }

        return new MetricIngestionBatch(points, rejected, reasons.Count is 0 ? null : string.Join("; ", reasons));

        void Reject(long count, InvalidDataException error)
        {
            rejected += count;
            reasons.Add(error.Message);
        }
    }

    private static long CountMetricPoints(ProtoMetric metric) => metric.DataCase switch
    {
        ProtoMetric.DataOneofCase.Gauge => metric.Gauge.DataPoints.Count,
        ProtoMetric.DataOneofCase.Sum => metric.Sum.DataPoints.Count,
        ProtoMetric.DataOneofCase.Histogram => metric.Histogram.DataPoints.Count,
        ProtoMetric.DataOneofCase.ExponentialHistogram => metric.ExponentialHistogram.DataPoints.Count,
        ProtoMetric.DataOneofCase.Summary => metric.Summary.DataPoints.Count,
        _ => 0
    };

    private static MetricPointIngestionRecord CreateHistogramPoint(MetricStreamMetadata stream,
        HistogramDataPoint point, MetricTemporality temporality)
    {
        if (point.BucketCounts.Count is not 0 && point.ExplicitBounds.Count + 1 != point.BucketCounts.Count)
        {
            throw new InvalidDataException(
                $"OTLP histogram '{stream.MetricName}' must carry one more count than explicit bound.");
        }

        ValidateAscendingBounds(stream.MetricName, point.ExplicitBounds);
        return CreateRecord(stream, point.Attributes, MetricKind.Histogram, temporality, false,
                new CommonContract.MetricHistogramPointData
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
            Exemplars = ConvertExemplars(point.Exemplars, ProducerOfScope(stream.ScopeName))
        };
    }

    private static MetricPointIngestionRecord CreateExponentialHistogramPoint(MetricStreamMetadata stream,
        ExponentialHistogramDataPoint point, MetricTemporality temporality) =>
        CreateRecord(stream, point.Attributes, MetricKind.ExponentialHistogram, temporality, false,
                new CommonContract.MetricExponentialHistogramPointData
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
                            : new CommonContract.MetricExponentialBuckets
                            {
                                Offset = point.Positive.Offset, BucketCounts = point.Positive.BucketCounts.ToArray()
                            },
                    Negative =
                        point.Negative is null
                            ? null
                            : new CommonContract.MetricExponentialBuckets
                            {
                                Offset = point.Negative.Offset, BucketCounts = point.Negative.BucketCounts.ToArray()
                            }
                }) with
            {
                TimeUnixNano = point.TimeUnixNano,
                StartTimeUnixNano = NullIfZero(point.StartTimeUnixNano),
                Flags = point.Flags,
                Exemplars = ConvertExemplars(point.Exemplars, ProducerOfScope(stream.ScopeName))
            };

    private static MetricPointIngestionRecord CreateSummaryPoint(MetricStreamMetadata stream,
        SummaryDataPoint point) =>
        CreateRecord(stream, point.Attributes, MetricKind.Summary, MetricTemporality.Unspecified, false,
                new CommonContract.MetricSummaryPointData
                {
                    Count = point.Count,
                    Sum = point.Sum,
                    QuantileValues =
                        point.QuantileValues.Select(static value =>
                                new CommonContract.MetricQuantileValue
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
        CreateRecord(stream, point.Attributes, kind, temporality, monotonic, new CommonContract.MetricNumberPointData
            {
                Value = point.ValueCase switch
                {
                    ProtoNumberDataPoint.ValueOneofCase.AsInt =>
                        new CommonContract.AttributeIntValue { Value = point.AsInt },
                    ProtoNumberDataPoint.ValueOneofCase.AsDouble => new CommonContract.AttributeDoubleValue
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
                Exemplars = ConvertExemplars(point.Exemplars, ProducerOfScope(stream.ScopeName))
            };

    private static NormalizedMetricExemplars ConvertExemplars(IEnumerable<ProtoExemplar> exemplars, string? producer)
    {
        var converted = new NormalizedMetricExemplars();
        foreach (var exemplar in exemplars)
        {
            var attributes = ConvertRecordAttributes(exemplar.FilteredAttributes, producer);
            converted.Renames.AddRange(NormalizedAttributes.Collect([attributes]));
            converted.Add(new CommonContract.MetricExemplar
            {
                TimeUnixNano = exemplar.TimeUnixNano,
                Value = exemplar.ValueCase switch
                {
                    ProtoExemplar.ValueOneofCase.AsInt => new CommonContract.AttributeIntValue { Value = exemplar.AsInt },
                    ProtoExemplar.ValueOneofCase.AsDouble => new CommonContract.AttributeDoubleValue
                    {
                        Value = exemplar.AsDouble
                    },
                    ProtoExemplar.ValueOneofCase.None =>
                        throw new InvalidDataException("OTLP metric exemplars require a numeric value."),
                    _ => throw new InvalidDataException("OTLP metric exemplars require a numeric value.")
                },
                FilteredAttributes = ToContractAttributes(attributes),
                TraceId = RequireIdOrAbsent(exemplar.TraceId, 16, "exemplar.trace_id"),
                SpanId = RequireIdOrAbsent(exemplar.SpanId, 8, "exemplar.span_id")
            });
        }

        return converted;
    }

    private static NormalizedContractAttributes ToContractAttributes(
        NormalizedAttributes attributes)
    {
        var converted = new NormalizedContractAttributes(NormalizedAttributes.Collect([attributes]));
        foreach (var pair in attributes.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            converted.Add(new CommonContract.Attribute { Key = pair.Key, Value = pair.Value });
        return converted;
    }

    private static MetricPointIngestionRecord CreateRecord(MetricStreamMetadata stream,
        RepeatedField<KeyValue> attributes, MetricKind kind,
        MetricTemporality temporality, bool monotonic, CommonContract.MetricPointData data) => new()
    {
        ProjectIdHint = stream.ProjectIdHint,
        MetricName = stream.MetricName,
        Shape = (kind, data) switch
        {
            (MetricKind.Gauge, CommonContract.MetricNumberPointData value) => new CommonContract.GaugeMetric
            {
                Data = value
            },
            (MetricKind.Sum, CommonContract.MetricNumberPointData value) => new CommonContract.SumMetric
            {
                Data = value, Temporality = temporality, IsMonotonic = monotonic
            },
            (MetricKind.Histogram, CommonContract.MetricHistogramPointData value) => new CommonContract.HistogramMetric
            {
                Data = value, Temporality = temporality
            },
            (MetricKind.ExponentialHistogram, CommonContract.MetricExponentialHistogramPointData value) =>
                new CommonContract.ExponentialHistogramMetric { Data = value, Temporality = temporality },
            (MetricKind.Summary, CommonContract.MetricSummaryPointData value) => new CommonContract.SummaryMetric
            {
                Data = value
            },
            _ => throw new InvalidDataException("Metric kind and payload do not agree.")
        },
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
        Attributes = ConvertRecordAttributes(attributes, ProducerOfScope(stream.ScopeName)),
        ResourceAttributes = stream.ResourceAttributes,
        ResourceEntityRefs = stream.ResourceEntityRefs,
        ResourceIdentity = stream.ResourceIdentity,
        ScopeIdentity = stream.ScopeIdentity,
        AttributesIdentity = MetricAttributeIdentity(attributes),
        InstrumentMetadata = stream.InstrumentMetadata,
        TimeUnixNano = 0
    };

    private static void ValidateAscendingBounds(string metricName, RepeatedField<double> bounds)
    {
        var previous = double.NegativeInfinity;
        foreach (var bound in bounds)
        {
            if (!double.IsFinite(bound) || bound <= previous)
            {
                throw new InvalidDataException(
                    $"OTLP histogram '{metricName}' explicit bounds must be finite and strictly ascending.");
            }

            previous = bound;
        }
    }

    private static MetricTemporality ConvertTemporality(ProtoTemporality temporality) => temporality switch
    {
        ProtoTemporality.Delta => MetricTemporality.Delta,
        ProtoTemporality.Cumulative => MetricTemporality.Cumulative,
        _ => MetricTemporality.Unspecified
    };

    private static ulong? NullIfZero(ulong value) => value is 0 ? null : value;


}
