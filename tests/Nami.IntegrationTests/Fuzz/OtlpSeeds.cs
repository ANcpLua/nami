using Google.Protobuf;
using Nami.Tests.Infrastructure;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using ProtoLogRecord = OpenTelemetry.Proto.Logs.V1.LogRecord;

namespace Nami.IntegrationTests.Fuzz;

internal enum FuzzSignal
{
    Traces,
    Logs,
    Metrics
}

/// <summary>One OTLP export body the mutator starts from, kept as protobuf bytes so every case clones it.</summary>
internal sealed record OtlpSeed(FuzzSignal Signal, string Name, byte[] Protobuf)
{
    internal IMessage Parse() => Signal switch
    {
        FuzzSignal.Traces => ExportTraceServiceRequest.Parser.ParseFrom(Protobuf),
        FuzzSignal.Logs => ExportLogsServiceRequest.Parser.ParseFrom(Protobuf),
        _ => ExportMetricsServiceRequest.Parser.ParseFrom(Protobuf)
    };
}

/// <summary>
///     The seed corpus: synthetic exports that reach every converter branch, and the captured Claude Code bodies
///     (<c>Fixtures/ClaudeCodeOtlp</c>) or any directory of <c>NNNNN-signal.pb</c> files named by
///     <c>NAMI_FUZZ_CORPUS</c>.
/// </summary>
internal static class OtlpSeeds
{
    private static readonly byte[] s_traceA = Convert.FromHexString("0af7651916cd43dd8448eb211c80319c");
    private static readonly byte[] s_traceB = Convert.FromHexString("4bf92f3577b34da6a3ce929d0e0e4736");
    private static readonly byte[] s_rootSpan = Convert.FromHexString("b7ad6b7169203331");
    private static readonly byte[] s_childSpan = Convert.FromHexString("00f067aa0ba902b7");
    private static readonly byte[] s_linkedSpan = Convert.FromHexString("53995c3f42cd8ad8");

    internal static IReadOnlyList<OtlpSeed> Load(ulong nowNanos, string? corpus, bool fixtures = true)
    {
        var seeds = new List<OtlpSeed>
        {
            new(FuzzSignal.Traces, "synthetic-traces", Traces(nowNanos).ToByteArray()),
            new(FuzzSignal.Logs, "synthetic-logs", Logs(nowNanos).ToByteArray()),
            new(FuzzSignal.Metrics, "synthetic-metrics", Metrics(nowNanos).ToByteArray())
        };
        foreach (var directory in fixtures ? CorpusDirectories(corpus) : [])
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.pb", SearchOption.AllDirectories)
                         .Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                FuzzSignal? signal = name.EndsWith("-traces", StringComparison.Ordinal) ? FuzzSignal.Traces
                    : name.EndsWith("-logs", StringComparison.Ordinal) ? FuzzSignal.Logs
                    : name.EndsWith("-metrics", StringComparison.Ordinal) ? FuzzSignal.Metrics
                    : null;
                if (signal is { } known)
                    seeds.Add(new OtlpSeed(known, Path.GetRelativePath(directory, file), File.ReadAllBytes(file)));
            }
        }

        return seeds;
    }

    private static IEnumerable<string> CorpusDirectories(string? corpus)
    {
        if (!string.IsNullOrWhiteSpace(corpus) && Directory.Exists(corpus))
            yield return corpus;
        var fixture = Path.Combine(ProcessRunner.RepositoryRoot, "tests", "Nami.IntegrationTests", "Fixtures",
            "ClaudeCodeOtlp");
        if (Directory.Exists(fixture) && !string.Equals(Path.GetFullPath(fixture), corpus is null ? null :
                Path.GetFullPath(corpus), StringComparison.Ordinal))
            yield return fixture;
    }

    internal static KeyValue Kv(string key, AnyValue value) => new() { Key = key, Value = value };

    internal static AnyValue Str(string value) => new() { StringValue = value };

    private static Resource ServiceResource() => new()
    {
        Attributes =
        {
            Kv("service.name", Str("checkout")),
            Kv("service.version", Str("1.2.3")),
            Kv("vcs.ref.head.revision", Str("4f2c1d0")),
            Kv("deployment.environment.name", Str("production")),
            Kv("cicd.pipeline.name", Str("deploy")),
            Kv("gen_ai.agent.name", Str("fuzzer"))
        },
        EntityRefs = { new EntityRef { Type = "service", IdKeys = { "service.name" }, DescriptionKeys = { "service.version" } } }
    };

    private static InstrumentationScope Scope() => new()
    {
        Name = "nami.fuzz",
        Version = "1.0.0",
        Attributes = { Kv("code.function.name", Str("Handle")) }
    };

    private static IEnumerable<KeyValue> RichAttributes() =>
    [
        Kv("http.request.method", Str("GET")),
        Kv("http.response.status_code", new AnyValue { IntValue = 500 }),
        Kv("error.type", Str("System.TimeoutException")),
        Kv("gen_ai.request.model", Str("claude-haiku-5-5")),
        Kv("gen_ai.usage.input_tokens", new AnyValue { IntValue = 1234 }),
        Kv("gen_ai.conversation.id", Str("conversation-1")),
        Kv("user.id", Str("user-7")),
        Kv("session.id", Str("session-42")),
        Kv("http.request.header.x-trace", new AnyValue
        {
            ArrayValue = new ArrayValue { Values = { Str("a"), new AnyValue { DoubleValue = 1.5 } } }
        }),
        Kv("gen_ai.input.messages", new AnyValue
        {
            KvlistValue = new KeyValueList
            {
                Values =
                {
                    Kv("role", Str("user")),
                    Kv("parts", new AnyValue
                    {
                        ArrayValue = new ArrayValue
                        {
                            Values =
                            {
                                new AnyValue { BoolValue = true },
                                new AnyValue { BytesValue = ByteString.CopyFromUtf8("bytes") },
                                new AnyValue { IntValue = -7 }
                            }
                        }
                    })
                }
            }
        })
    ];

    private static ExportTraceServiceRequest Traces(ulong now)
    {
        var root = new Span
        {
            TraceId = ByteString.CopyFrom(s_traceA),
            SpanId = ByteString.CopyFrom(s_rootSpan),
            Name = "GET /cart",
            Kind = Span.Types.SpanKind.Server,
            StartTimeUnixNano = now - 5_000_000_000UL,
            EndTimeUnixNano = now - 1_000_000_000UL,
            Status = new OpenTelemetry.Proto.Trace.V1.Status
            {
                Code = OpenTelemetry.Proto.Trace.V1.Status.Types.StatusCode.Error,
                Message = "upstream timed out"
            },
            TraceState = "vendor=value",
            Flags = 1,
            Events =
            {
                new Span.Types.Event
                {
                    Name = "exception",
                    TimeUnixNano = now - 2_000_000_000UL,
                    Attributes =
                    {
                        Kv("exception.type", Str("System.TimeoutException")),
                        Kv("exception.message", Str("token=secret timed out after 30s")),
                        Kv("exception.stacktrace", Str("at Checkout.Handle()\n at Program.Main()"))
                    }
                }
            }
        };
        root.Attributes.AddRange(RichAttributes());
        var child = new Span
        {
            TraceId = ByteString.CopyFrom(s_traceA),
            SpanId = ByteString.CopyFrom(s_childSpan),
            ParentSpanId = ByteString.CopyFrom(s_rootSpan),
            Name = "SELECT cart",
            Kind = Span.Types.SpanKind.Client,
            StartTimeUnixNano = now - 4_000_000_000UL,
            EndTimeUnixNano = now - 3_000_000_000UL,
            Status = new OpenTelemetry.Proto.Trace.V1.Status { Code = OpenTelemetry.Proto.Trace.V1.Status.Types.StatusCode.Ok },
            Links =
            {
                new Span.Types.Link
                {
                    TraceId = ByteString.CopyFrom(s_traceB),
                    SpanId = ByteString.CopyFrom(s_linkedSpan),
                    TraceState = "linked=1",
                    Attributes = { Kv("error.type", Str("retry")) }
                }
            },
            Attributes = { Kv("code.function.name", Str("Query")) }
        };
        return new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    Resource = ServiceResource(),
                    SchemaUrl = "https://opentelemetry.io/schemas/1.45.0",
                    ScopeSpans = { new ScopeSpans { Scope = Scope(), Spans = { root, child } } }
                }
            }
        };
    }

    private static ExportLogsServiceRequest Logs(ulong now)
    {
        var exception = new ProtoLogRecord
        {
            TimeUnixNano = now - 3_000_000_000UL,
            ObservedTimeUnixNano = now - 2_900_000_000UL,
            SeverityNumber = SeverityNumber.Error,
            SeverityText = "ERROR",
            EventName = "exception",
            TraceId = ByteString.CopyFrom(s_traceA),
            SpanId = ByteString.CopyFrom(s_rootSpan),
            Body = Str("checkout failed: upstream timed out"),
            Attributes =
            {
                Kv("exception.type", Str("System.TimeoutException")),
                Kv("exception.message", Str("password=hunter2 timed out")),
                Kv("session.id", Str("session-42"))
            }
        };
        var structured = new ProtoLogRecord
        {
            TimeUnixNano = now - 2_000_000_000UL,
            SeverityNumber = SeverityNumber.Info,
            EventName = "claude_code.api_request",
            Body = new AnyValue
            {
                KvlistValue = new KeyValueList
                {
                    Values = { Kv("message", Str("api request")), Kv("cost", new AnyValue { DoubleValue = 0.0033 }) }
                }
            }
        };
        structured.Attributes.AddRange(RichAttributes());
        var untimed = new ProtoLogRecord { SeverityNumber = SeverityNumber.Warn, Body = Str("no timestamp at all") };
        return new ExportLogsServiceRequest
        {
            ResourceLogs =
            {
                new ResourceLogs
                {
                    Resource = ServiceResource(),
                    ScopeLogs = { new ScopeLogs { Scope = Scope(), LogRecords = { exception, structured, untimed } } }
                }
            }
        };
    }

    private static ExportMetricsServiceRequest Metrics(ulong now)
    {
        var exemplar = new Exemplar
        {
            TimeUnixNano = now - 1_500_000_000UL,
            AsDouble = 12.5,
            TraceId = ByteString.CopyFrom(s_traceA),
            SpanId = ByteString.CopyFrom(s_rootSpan),
            FilteredAttributes = { Kv("http.request.method", Str("GET")) }
        };
        var point = new NumberDataPoint
        {
            StartTimeUnixNano = now - 10_000_000_000UL,
            TimeUnixNano = now - 1_000_000_000UL,
            AsInt = 42,
            Attributes = { Kv("http.request.method", Str("GET")) },
            Exemplars = { exemplar }
        };
        var metrics = new ScopeMetrics
        {
            Scope = Scope(),
            Metrics =
            {
                new Metric
                {
                    Name = "claude_code.cost.usage",
                    Unit = "USD",
                    Description = "Cost",
                    Sum = new Sum
                    {
                        AggregationTemporality = AggregationTemporality.Delta,
                        IsMonotonic = true,
                        DataPoints = { point }
                    },
                    Metadata = { Kv("gen_ai.agent.name", Str("fuzzer")) }
                },
                new Metric
                {
                    Name = "process.memory.usage",
                    Unit = "By",
                    Gauge = new Gauge
                    {
                        DataPoints =
                        {
                            new NumberDataPoint { TimeUnixNano = now - 1_000_000_000UL, AsDouble = 1024.75 }
                        }
                    }
                },
                new Metric
                {
                    Name = "http.server.request.duration",
                    Unit = "s",
                    Histogram = new Histogram
                    {
                        AggregationTemporality = AggregationTemporality.Cumulative,
                        DataPoints =
                        {
                            new HistogramDataPoint
                            {
                                StartTimeUnixNano = now - 10_000_000_000UL,
                                TimeUnixNano = now - 1_000_000_000UL,
                                Count = 6,
                                Sum = 3.5,
                                Min = 0.01,
                                Max = 2,
                                ExplicitBounds = { 0.1, 0.5, 1 },
                                BucketCounts = { 1, 2, 2, 1 },
                                Exemplars = { exemplar.Clone() }
                            }
                        }
                    }
                },
                new Metric
                {
                    Name = "rpc.server.duration",
                    Unit = "ms",
                    ExponentialHistogram = new ExponentialHistogram
                    {
                        AggregationTemporality = AggregationTemporality.Delta,
                        DataPoints =
                        {
                            new ExponentialHistogramDataPoint
                            {
                                TimeUnixNano = now - 1_000_000_000UL,
                                Count = 5,
                                Sum = 40,
                                Scale = 3,
                                ZeroCount = 1,
                                ZeroThreshold = 0.001,
                                Positive = new ExponentialHistogramDataPoint.Types.Buckets { Offset = -2, BucketCounts = { 1, 2, 1 } },
                                Negative = new ExponentialHistogramDataPoint.Types.Buckets { Offset = 0 }
                            }
                        }
                    }
                },
                new Metric
                {
                    Name = "legacy.latency",
                    Summary = new Summary
                    {
                        DataPoints =
                        {
                            new SummaryDataPoint
                            {
                                TimeUnixNano = now - 1_000_000_000UL,
                                Count = 3,
                                Sum = 9,
                                QuantileValues =
                                {
                                    new SummaryDataPoint.Types.ValueAtQuantile { Quantile = 0.5, Value = 2 },
                                    new SummaryDataPoint.Types.ValueAtQuantile { Quantile = 0.99, Value = 5 }
                                }
                            }
                        }
                    }
                }
            }
        };
        return new ExportMetricsServiceRequest
        {
            ResourceMetrics = { new ResourceMetrics { Resource = ServiceResource(), ScopeMetrics = { metrics } } }
        };
    }
}
