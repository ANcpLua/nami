using System.Collections.ObjectModel;
using Google.Protobuf;
using Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Metrics.V1;
using ProtoSpan = Nami.Tests.Wire.OpenTelemetry.Proto.Trace.V1.Span;
using ProtoMetric = Nami.Tests.Wire.OpenTelemetry.Proto.Metrics.V1.Metric;
using ProtoResource = Nami.Tests.Wire.OpenTelemetry.Proto.Resource.V1.Resource;
using ProtoScope = Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1.InstrumentationScope;
using ProtoExemplar = Nami.Tests.Wire.OpenTelemetry.Proto.Metrics.V1.Exemplar;

namespace Nami.Tests;

internal sealed class OtlpDecode
{
    private readonly List<string> _warnings = [];
    internal IReadOnlyList<string> Warnings => _warnings;

    private ReadOnlyDictionary<string, object?> Attributes(IEnumerable<KeyValue> values)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        var original = new Dictionary<string, AnyValue?>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            // Profiling-only key references have the same non-fatal rule as indexed values:
            // https://github.com/open-telemetry/opentelemetry-proto/blob/v1.11.1/opentelemetry/proto/common/v1/common.proto
            if (pair.KeyStrindex != 0)
            {
                _warnings.Add(
                    $"KeyStrindex {pair.KeyStrindex} is intended for profiling; its key reference was ignored.");
                if (pair.Key.Length == 0) continue;
            }

            if (!original.TryAdd(pair.Key, pair.Value))
            {
                if (!Equals(original[pair.Key], pair.Value))
                    throw new InvalidDataException($"Duplicate OTLP attribute: {pair.Key}.");
                // ILogger state and scopes can repeat the same typed value. Keep the raw
                // request intact and make this projection agree with the Collector.
                _warnings.Add($"Coalesced identical OTLP attribute: {pair.Key}.");
                continue;
            }

            result.Add(pair.Key, Value(pair.Value));
        }

        return new ReadOnlyDictionary<string, object?>(result);
    }

    private object? Value(AnyValue? value)
    {
        if (value is null) return null;
        return value.ValueCase switch
        {
            AnyValue.ValueOneofCase.None => null,
            AnyValue.ValueOneofCase.StringValue => value.StringValue,
            AnyValue.ValueOneofCase.StringValueStrindex => UnsupportedStringReference(value.StringValueStrindex),
            AnyValue.ValueOneofCase.BoolValue => value.BoolValue,
            AnyValue.ValueOneofCase.IntValue => value.IntValue,
            AnyValue.ValueOneofCase.DoubleValue => value.DoubleValue,
            AnyValue.ValueOneofCase.BytesValue => value.BytesValue.ToByteArray(),
            AnyValue.ValueOneofCase.ArrayValue => value.ArrayValue.Values.Select(Value).AsReadOnlySnapshot(),
            AnyValue.ValueOneofCase.KvlistValue => Attributes(value.KvlistValue.Values),
            _ => throw new InvalidDataException($"Unsupported OTLP AnyValue case: {value.ValueCase}.")
        };
    }

    private string UnsupportedStringReference(int index)
    {
        // Non-profiling receivers must treat this as non-fatal and absent/empty, with a warning:
        // https://github.com/open-telemetry/opentelemetry-proto/blob/v1.11.1/opentelemetry/proto/common/v1/common.proto
        _warnings.Add($"StringValueStrindex {index} is intended for profiling; its value was treated as empty.");
        return string.Empty;
    }

    private static string? Identity(ByteString bytes, int size, bool required = false)
    {
        if (bytes.IsEmpty && !required) return null;
        if (bytes.Length != size || !bytes.Span.ContainsAnyExcept((byte)0))
        {
            throw new InvalidDataException(
                $"An OTLP identity must contain exactly {size} bytes and must not be all zero.");
        }

        return Convert.ToHexStringLower(bytes.Span);
    }

    internal ResourceRecord Resource(ProtoResource? resource, string schemaUrl)
    {
        return new ResourceRecord(
            Attributes(resource?.Attributes ?? []))
        {
            SchemaUrl = Empty(schemaUrl),
            DroppedAttributesCount = resource?.DroppedAttributesCount ?? 0,
            EntityReferences = (resource?.EntityRefs.Select(e =>
                    new ResourceEntityReference(e.Type, e.IdKeys.AsReadOnlySnapshot())
                    {
                        DescriptionKeys = e.DescriptionKeys.AsReadOnlySnapshot(), SchemaUrl = Empty(e.SchemaUrl)
                    })
                .ToArray() ?? []).AsReadOnlySnapshot()
        };
    }

    internal InstrumentationScopeRecord? Scope(ProtoScope? scope, string schemaUrl)
    {
        return scope is null && schemaUrl.Length == 0
            ? null
            : new InstrumentationScopeRecord
            {
                Name = Empty(scope?.Name), Version = Empty(scope?.Version), SchemaUrl = Empty(schemaUrl),
                Attributes = Attributes(scope?.Attributes ?? []),
                DroppedAttributesCount = scope?.DroppedAttributesCount ?? 0
            };
    }

    internal SpanRecord Span(ProtoSpan span, ResourceRecord resource, InstrumentationScopeRecord? scope)
    {
        return new SpanRecord(
            Identity(span.TraceId, 16, true)!, Identity(span.SpanId, 8, true)!, span.Name)
        {
            ParentSpanId = Identity(span.ParentSpanId, 8), TraceState = Empty(span.TraceState), Flags = span.Flags,
            Kind = (SpanKind)(int)span.Kind, StartTimeUnixNano = span.StartTimeUnixNano,
            EndTimeUnixNano = span.EndTimeUnixNano, Status = (ActivityStatusCode)(int)(span.Status?.Code ?? 0),
            StatusDescription = Empty(span.Status?.Message), Resource = resource, Scope = scope,
            Attributes = Attributes(span.Attributes), DroppedAttributesCount = span.DroppedAttributesCount,
            Events = span.Events.Select(e =>
                new SpanEventRecord(e.Name, e.TimeUnixNano, Attributes(e.Attributes))
                    { DroppedAttributesCount = e.DroppedAttributesCount }).AsReadOnlySnapshot(),
            DroppedEventsCount = span.DroppedEventsCount,
            Links = span.Links.Select(l =>
                new SpanLinkRecord(Identity(l.TraceId, 16, true)!, Identity(l.SpanId, 8, true)!,
                    Attributes(l.Attributes))
                {
                    TraceState = Empty(l.TraceState), Flags = l.Flags,
                    DroppedAttributesCount = l.DroppedAttributesCount
                }).AsReadOnlySnapshot(),
            DroppedLinksCount = span.DroppedLinksCount
        };
    }

    internal LogRecord Log(Wire.OpenTelemetry.Proto.Logs.V1.LogRecord log, ResourceRecord resource,
        InstrumentationScopeRecord? scope)
    {
        return new LogRecord(
            resource, scope, log.TimeUnixNano, log.ObservedTimeUnixNano, (int)log.SeverityNumber, log.SeverityText,
            Value(log.Body), Attributes(log.Attributes), log.DroppedAttributesCount, log.Flags,
            Identity(log.TraceId, 16), Identity(log.SpanId, 8), log.EventName);
    }

    internal MetricRecord Metric(ProtoMetric metric, ResourceRecord resource, InstrumentationScopeRecord? scope)
    {
        return new MetricRecord(resource, scope, metric.Name, metric.Description, metric.Unit,
            Attributes(metric.Metadata),
            DecodeMetricData(metric));
    }

    private MetricData DecodeMetricData(ProtoMetric metric)
    {
        if (metric.Gauge is { } gauge)
        {
            return new GaugeData(
                Numbers(gauge.DataPoints));
        }

        if (metric.Sum is { } sum)
        {
            return new SumData(
                Numbers(sum.DataPoints),
                (int)sum.AggregationTemporality, sum.IsMonotonic);
        }

        if (metric.Histogram is { } histogram)
        {
            return new HistogramData(histogram.DataPoints
                    .Select(p => new HistogramPoint(
                        Attributes(p.Attributes), p.StartTimeUnixNano, p.TimeUnixNano, p.Count,
                        p.HasSum ? p.Sum : null,
                        p.BucketCounts.AsReadOnlySnapshot(), p.ExplicitBounds.AsReadOnlySnapshot(),
                        Exemplars(p.Exemplars), p.Flags,
                        p.HasMin ? p.Min : null, p.HasMax ? p.Max : null)).AsReadOnlySnapshot(),
                (int)histogram.AggregationTemporality);
        }

        if (metric.ExponentialHistogram is { } exponentialHistogram)
        {
            return new ExponentialHistogramData(exponentialHistogram.DataPoints.Select(p =>
                new ExponentialHistogramPoint(
                    Attributes(p.Attributes), p.StartTimeUnixNano, p.TimeUnixNano, p.Count, p.HasSum ? p.Sum : null,
                    p.Scale, p.ZeroCount,
                    p.Positive is { } positive
                        ? new ExponentialBuckets(positive.Offset, positive.BucketCounts.AsReadOnlySnapshot())
                        : null,
                    p.Negative is { } negative
                        ? new ExponentialBuckets(negative.Offset, negative.BucketCounts.AsReadOnlySnapshot())
                        : null,
                    p.Flags, Exemplars(p.Exemplars), p.HasMin ? p.Min : null, p.HasMax ? p.Max : null,
                    p.ZeroThreshold)).AsReadOnlySnapshot(), (int)exponentialHistogram.AggregationTemporality);
        }

        if (metric.Summary is { } summary)
        {
            return new SummaryData(summary.DataPoints
                .Select(p => new SummaryPoint(
                    Attributes(p.Attributes), p.StartTimeUnixNano, p.TimeUnixNano, p.Count, p.Sum,
                    p.QuantileValues.Select(q => new QuantileValue(q.Quantile, q.Value)).AsReadOnlySnapshot(), p.Flags))
                .AsReadOnlySnapshot());
        }

        return new UnspecifiedMetricData();
    }

    private ReadOnlyCollection<NumberPoint> Numbers(IEnumerable<NumberDataPoint> points)
    {
        return points.Select(Number).AsReadOnlySnapshot();
    }

    private NumberPoint Number(NumberDataPoint point)
    {
        return new NumberPoint(
            Attributes(point.Attributes), point.StartTimeUnixNano, point.TimeUnixNano,
            point.ValueCase switch
            {
                NumberDataPoint.ValueOneofCase.AsInt => point.AsInt,
                NumberDataPoint.ValueOneofCase.AsDouble => point.AsDouble, _ => null
            },
            point.Flags, Exemplars(point.Exemplars));
    }

    private ReadOnlyCollection<ExemplarRecord> Exemplars(IEnumerable<ProtoExemplar> exemplars)
    {
        return exemplars.Select(e => new ExemplarRecord(
            Attributes(e.FilteredAttributes), e.TimeUnixNano,
            e.ValueCase switch
            {
                ProtoExemplar.ValueOneofCase.AsInt => e.AsInt,
                ProtoExemplar.ValueOneofCase.AsDouble => e.AsDouble, _ => null
            },
            Identity(e.TraceId, 16), Identity(e.SpanId, 8))).AsReadOnlySnapshot();
    }

    private static string? Empty(string? value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
