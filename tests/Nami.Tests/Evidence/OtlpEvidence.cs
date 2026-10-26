using Google.Protobuf;
using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Logs.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Metrics.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Trace.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Logs.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Metrics.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Trace.V1;
using EntityRef = Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1.EntityRef;
using ProtoLog = Nami.Tests.Wire.OpenTelemetry.Proto.Logs.V1.LogRecord;
using ProtoResource = Nami.Tests.Wire.OpenTelemetry.Proto.Resource.V1.Resource;

namespace Nami.Tests.Evidence;

internal static class OtlpEvidence
{
    /// <summary>The trace of <see cref="RichSpan" />, which the other wire fixtures share.</summary>
    internal const string TraceId = "11111111111111111111111111111111";

    /// <summary>The span id of <see cref="RichSpan" />.</summary>
    internal const string SpanId = "2222222222222222";

    /// <summary>The parent span id of <see cref="RichSpan" />.</summary>
    internal const string ParentSpanId = "3333333333333333";

    /// <summary>A trace that no fixture exports.</summary>
    internal const string UnrelatedTraceId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    internal static ExportTraceServiceRequest RichSpan()
    {
        var resource = new ProtoResource
        {
            DroppedAttributesCount = 3,
            Attributes =
            {
                Pair("service.name", new AnyValue { StringValue = "rich-service" }),
                Pair("service.version", new AnyValue { StringValue = "1.2.3" })
            },
            EntityRefs =
            {
                new EntityRef
                {
                    Type = "service", SchemaUrl = "entity-schema", IdKeys = { "service.name" },
                    DescriptionKeys = { "service.version" }
                }
            }
        };
        var scope = new ScopeSpans
        {
            SchemaUrl = "scope-schema",
            Scope = new InstrumentationScope
            {
                Name = "rich-scope", Version = "3.2.1", DroppedAttributesCount = 4,
                Attributes = { Pair("scope.enabled", new AnyValue { BoolValue = true }) }
            }
        };
        scope.Spans.Add(new Span
        {
            Name = "rich.operation", TraceId = Id(TraceId), SpanId = Id(SpanId), ParentSpanId = Id(ParentSpanId),
            Kind = Span.Types.SpanKind.Consumer, Flags = 0x80000301, TraceState = "vendor=value",
            StartTimeUnixNano = 1000000001, EndTimeUnixNano = 1000000099,
            Status = new Status { Code = Status.Types.StatusCode.Error, Message = "failed exactly" },
            DroppedAttributesCount = 5, DroppedEventsCount = 6, DroppedLinksCount = 7,
            Events =
            {
                new Span.Types.Event
                {
                    Name = "exception", TimeUnixNano = 1000000002, DroppedAttributesCount = 8,
                    Attributes = { Pair("exception.message", new AnyValue { StringValue = "keep message" }) }
                },
                new Span.Types.Event { Name = "completed", TimeUnixNano = 1000000003 }
            },
            Links =
            {
                new Span.Types.Link
                {
                    TraceId = Id("44444444444444444444444444444444"), SpanId = Id("5555555555555555"),
                    TraceState = "linked=state", Flags = 0x80000301, DroppedAttributesCount = 9,
                    Attributes = { Pair("link.value", new AnyValue { IntValue = 42 }) }
                }
            }
        });
        return new ExportTraceServiceRequest
        {
            ResourceSpans =
                { new ResourceSpans { Resource = resource, SchemaUrl = "resource-schema", ScopeSpans = { scope } } }
        };
    }

    /// <summary>One trace export of <paramref name="spans" /> in a single scope without scope metadata.</summary>
    internal static ExportTraceServiceRequest Traces(ProtoResource? resource, params IEnumerable<Span> spans)
    {
        return Traces(resource, new ScopeSpans { Spans = { spans } });
    }

    /// <summary>One trace export of <paramref name="spans" /> in the single scope <paramref name="scope" />.</summary>
    internal static ExportTraceServiceRequest Traces(ProtoResource? resource, InstrumentationScope scope,
        params IEnumerable<Span> spans)
    {
        return Traces(resource, new ScopeSpans { Scope = scope, Spans = { spans } });
    }

    /// <summary>One log export of <paramref name="records" /> in a single scope without scope metadata.</summary>
    internal static ExportLogsServiceRequest Logs(ProtoResource? resource, params IEnumerable<ProtoLog> records)
    {
        return Logs(resource, new ScopeLogs { LogRecords = { records } });
    }

    /// <summary>One log export of <paramref name="records" /> in the single scope <paramref name="scope" />.</summary>
    internal static ExportLogsServiceRequest Logs(ProtoResource? resource, InstrumentationScope scope,
        params IEnumerable<ProtoLog> records)
    {
        return Logs(resource, new ScopeLogs { Scope = scope, LogRecords = { records } });
    }

    /// <summary>One metric export of <paramref name="metrics" /> in a single scope without scope metadata.</summary>
    internal static ExportMetricsServiceRequest Metrics(ProtoResource? resource, params IEnumerable<Metric> metrics)
    {
        return new ExportMetricsServiceRequest
        {
            ResourceMetrics =
            {
                new ResourceMetrics
                    { Resource = resource, ScopeMetrics = { new ScopeMetrics { Metrics = { metrics } } } }
            }
        };
    }

    private static ExportTraceServiceRequest Traces(ProtoResource? resource, ScopeSpans scope)
    {
        return new ExportTraceServiceRequest
            { ResourceSpans = { new ResourceSpans { Resource = resource, ScopeSpans = { scope } } } };
    }

    private static ExportLogsServiceRequest Logs(ProtoResource? resource, ScopeLogs scope)
    {
        return new ExportLogsServiceRequest
            { ResourceLogs = { new ResourceLogs { Resource = resource, ScopeLogs = { scope } } } };
    }

    internal static KeyValue Pair(string key, AnyValue value)
    {
        return new KeyValue { Key = key, Value = value };
    }

    /// <summary>An attribute whose CLR value is mapped to the matching OTLP value kind.</summary>
    internal static KeyValue Pair(string key, object value)
    {
        return Pair(key, Value(value));
    }

    /// <summary>The OTLP value of a string, boolean, 32- or 64-bit integer, double or string array.</summary>
    private static AnyValue Value(object value)
    {
        return value switch
        {
            string text => new AnyValue { StringValue = text },
            bool flag => new AnyValue { BoolValue = flag },
            int number => new AnyValue { IntValue = number },
            long number => new AnyValue { IntValue = number },
            double number => new AnyValue { DoubleValue = number },
            string[] items => new AnyValue { ArrayValue = new ArrayValue { Values = { items.Select(Value) } } },
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported OTLP attribute value.")
        };
    }

    internal static ByteString Id(string hex)
    {
        return ByteString.CopyFrom(Convert.FromHexString(hex));
    }
}
