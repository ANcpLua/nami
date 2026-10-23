using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using OpenTelemetry;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Trace.V1;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OtlpLogRecord = OpenTelemetry.Proto.Logs.V1.LogRecord;
using OtlpMetric = OpenTelemetry.Proto.Metrics.V1.Metric;
using OtlpResourceLogs = OpenTelemetry.Proto.Logs.V1.ResourceLogs;
using OtlpResourceMetrics = OpenTelemetry.Proto.Metrics.V1.ResourceMetrics;
using OtlpResource = OpenTelemetry.Proto.Resource.V1.Resource;
using OtlpScopeLogs = OpenTelemetry.Proto.Logs.V1.ScopeLogs;
using OtlpScopeMetrics = OpenTelemetry.Proto.Metrics.V1.ScopeMetrics;
using OtlpSpan = OpenTelemetry.Proto.Trace.V1.Span;
using GrpcStatusCode = Grpc.Core.StatusCode;

return await CollectorSmoke.RunAsync(args).ConfigureAwait(false);

internal static class CollectorSmoke
{
    private const string JsonTraceId = "0af7651916cd43dd8448eb211c80319c";
    private const string JsonSpanId = "b7ad6b7169203331";
    private const string JsonService = "aot-smoke-http-json";
    private const string JsonModel = "claude-fable-5";
    private const string ProtobufTraceId = "1af7651916cd43dd8448eb211c80319c";
    private const string ProtobufSpanId = "c7ad6b7169203331";
    private const string GrpcTraceId = "2af7651916cd43dd8448eb211c80319c";
    private const string GrpcSpanId = "d7ad6b7169203331";
    private const string GrpcLogService = "aot-smoke-grpc-logs";
    private const string GrpcLogEvent = "aot.smoke.grpc.log";
    private const string SummaryMetricName = "aot.smoke.summary";
    private const string InvalidMetricName = "aot.smoke.missing-time";
    private const string ProtobufContentType = "application/x-protobuf";
    private const string JsonContentType = "application/json";
    private const string StockSourceName = "Nami.CollectorAotSmoke.StockSdk";
    private static readonly TimeSpan s_requestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan s_readbackTimeout = TimeSpan.FromSeconds(15);

    private static readonly HttpClient s_http = new()
    {
        Timeout = s_requestTimeout
    };

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length is 0)
                throw new ArgumentException("Missing command.");

            switch (args[0])
            {
                case "wire" when args.Length is 4:
                    await VerifyWireAsync(ParseBaseUri(args[1]), ParseBaseUri(args[2]), ParseBaseUri(args[3]))
                        .ConfigureAwait(false);
                    break;
                case "persistence" when args.Length is 2:
                    await VerifyTraceReadbackAsync(ParseBaseUri(args[1]), JsonTraceId, JsonSpanId)
                        .ConfigureAwait(false);
                    break;
                case "grpc-auth" when args.Length is 2:
                    await VerifyGrpcAuthAsync(ParseBaseUri(args[1])).ConfigureAwait(false);
                    break;
                case "stock-sdk" when args.Length is 2:
                    await VerifyStockSdkAsync(ParseBaseUri(args[1])).ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentException(
                        "Usage: CollectorAotSmoke wire <api-base> <otlp-http-base> <grpc-base> | " +
                        "persistence <api-base> | grpc-auth <grpc-base> | stock-sdk <api-base>");
            }

            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[driver] FAIL: {ex}").ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task VerifyWireAsync(Uri apiBase, Uri otlpHttpBase, Uri grpcBase)
    {
        var http = s_http;

        var now = checked((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000UL);
        var jsonTrace = BuildTrace(
            JsonTraceId,
            JsonSpanId,
            JsonService,
            "aot-smoke-http-json-span",
            now,
            includeSessionAttributes: true);
        await ExportJsonTraceAsync(http, otlpHttpBase, jsonTrace).ConfigureAwait(false);
        await VerifyTraceReadbackAsync(http, apiBase, JsonTraceId, JsonSpanId).ConfigureAwait(false);
        await VerifyJsonValuesAsync(http, new Uri(apiBase, "api/v1/sessions"), JsonService, JsonModel)
            .ConfigureAwait(false);
        Console.WriteLine("[driver] HTTP JSON trace response and readback verified");

        var protobufTrace = BuildTrace(
            ProtobufTraceId,
            ProtobufSpanId,
            "aot-smoke-http-protobuf",
            "aot-smoke-http-protobuf-span",
            now + 2_000_000UL);
        await ExportProtobufTraceAsync(http, otlpHttpBase, protobufTrace).ConfigureAwait(false);
        await VerifyTraceReadbackAsync(http, apiBase, ProtobufTraceId, ProtobufSpanId).ConfigureAwait(false);
        Console.WriteLine("[driver] HTTP protobuf trace response encoding and readback verified");

        using var channel = GrpcChannel.ForAddress(grpcBase);
        var grpcTrace = BuildTrace(
            GrpcTraceId,
            GrpcSpanId,
            "aot-smoke-grpc-traces",
            "aot-smoke-grpc-trace-span",
            now + 4_000_000UL);
        var traceClient = new TraceService.TraceServiceClient(channel);
        var traceResponse = await traceClient.ExportAsync(
                grpcTrace,
                deadline: DateTime.UtcNow.Add(s_requestTimeout))
            .ResponseAsync.ConfigureAwait(false);
        AssertTraceSuccess(traceResponse);
        await VerifyTraceReadbackAsync(http, apiBase, GrpcTraceId, GrpcSpanId).ConfigureAwait(false);
        Console.WriteLine("[driver] gRPC trace export and readback verified");

        var logsClient = new LogsService.LogsServiceClient(channel);
        var logsResponse = await logsClient.ExportAsync(
                BuildLogs(now + 6_000_000UL),
                deadline: DateTime.UtcNow.Add(s_requestTimeout))
            .ResponseAsync.ConfigureAwait(false);
        AssertLogsSuccess(logsResponse);
        await VerifyJsonValuesAsync(
                http,
                new Uri(apiBase, $"api/v1/logs?service_name={Uri.EscapeDataString(GrpcLogService)}"),
                GrpcLogService,
                GrpcLogEvent,
                GrpcTraceId,
                GrpcSpanId)
            .ConfigureAwait(false);
        Console.WriteLine("[driver] gRPC log export and readback verified");

        var tracesBeforeMetrics = await GetPageItemCountAsync(http, new Uri(apiBase, "api/v1/traces"))
            .ConfigureAwait(false);
        var logsBeforeMetrics = await GetPageItemCountAsync(http, new Uri(apiBase, "api/v1/logs"))
            .ConfigureAwait(false);
        var metricsRequest = BuildMetrics(now + 8_000_000UL);

        using (var content = new ByteArrayContent(metricsRequest.ToByteArray()))
        {
            content.Headers.ContentType = new MediaTypeHeaderValue(ProtobufContentType);
            using var response = await http.PostAsync(new Uri(otlpHttpBase, "v1/metrics"), content)
                .ConfigureAwait(false);
            var payload = await ReadSuccessPayloadAsync(response, ProtobufContentType).ConfigureAwait(false);
            AssertMetricsStored(ExportMetricsServiceResponse.Parser.ParseFrom(payload));
        }

        var metricsClient = new MetricsService.MetricsServiceClient(channel);
        var grpcMetricsResponse = await metricsClient.ExportAsync(
                metricsRequest,
                deadline: DateTime.UtcNow.Add(s_requestTimeout))
            .ResponseAsync.ConfigureAwait(false);
        AssertMetricsStored(grpcMetricsResponse);
        await VerifyOriginalMetricPointsAsync(http, apiBase).ConfigureAwait(false);

        var tracesAfterMetrics = await GetPageItemCountAsync(http, new Uri(apiBase, "api/v1/traces"))
            .ConfigureAwait(false);
        var logsAfterMetrics = await GetPageItemCountAsync(http, new Uri(apiBase, "api/v1/logs"))
            .ConfigureAwait(false);
        AssertEqual(tracesBeforeMetrics, tracesAfterMetrics, "metrics export changed stored trace count");
        AssertEqual(logsBeforeMetrics, logsAfterMetrics, "metrics export changed stored log count");
        Console.WriteLine("[driver] HTTP and gRPC metrics: all five original shapes and invalid-time rejection verified");
    }

    private static async Task ExportJsonTraceAsync(
        HttpClient http,
        Uri otlpHttpBase,
        ExportTraceServiceRequest request)
    {
        var json = JsonNode.Parse(JsonFormatter.Default.Format(request))?.AsObject()
                   ?? throw new InvalidOperationException("Generated OTLP JSON was empty.");
        var span = json["resourceSpans"]?[0]?["scopeSpans"]?[0]?["spans"]?[0]
                   ?? throw new InvalidOperationException("Generated OTLP JSON did not contain the smoke span.");
        span["traceId"] = JsonTraceId;
        span["spanId"] = JsonSpanId;

        using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(json.ToJsonString()));
        content.Headers.ContentType = new MediaTypeHeaderValue(JsonContentType);
        using var response = await http.PostAsync(new Uri(otlpHttpBase, "v1/traces"), content)
            .ConfigureAwait(false);
        var payload = await ReadSuccessPayloadAsync(response, JsonContentType).ConfigureAwait(false);
        AssertTraceSuccess(JsonParser.Default.Parse<ExportTraceServiceResponse>(Encoding.UTF8.GetString(payload)));
    }

    private static async Task ExportProtobufTraceAsync(
        HttpClient http,
        Uri otlpHttpBase,
        ExportTraceServiceRequest request)
    {
        using var content = new ByteArrayContent(request.ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue(ProtobufContentType);
        using var response = await http.PostAsync(new Uri(otlpHttpBase, "v1/traces"), content)
            .ConfigureAwait(false);
        var payload = await ReadSuccessPayloadAsync(response, ProtobufContentType).ConfigureAwait(false);
        AssertTraceSuccess(ExportTraceServiceResponse.Parser.ParseFrom(payload));
    }

    private static async Task<byte[]> ReadSuccessPayloadAsync(HttpResponseMessage response, string mediaType)
    {
        var payload = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        if (response.StatusCode is not HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} returned " +
                $"{(int)response.StatusCode}: {Encoding.UTF8.GetString(payload)}");
        }

        AssertEqual(
            mediaType,
            response.Content.Headers.ContentType?.MediaType,
            $"{response.RequestMessage?.RequestUri} response Content-Type");
        return payload;
    }

    private static async Task VerifyTraceReadbackAsync(Uri apiBase, string traceId, string spanId)
    {
        await VerifyTraceReadbackAsync(s_http, apiBase, traceId, spanId).ConfigureAwait(false);
    }

    private static Task VerifyTraceReadbackAsync(HttpClient http, Uri apiBase, string traceId, string spanId) =>
        VerifyJsonValuesAsync(http, new Uri(apiBase, $"api/v1/traces/{traceId}"), traceId, spanId);

    private static async Task VerifyJsonValuesAsync(HttpClient http, Uri uri, params string[] expectedValues)
    {
        var deadline = Stopwatch.StartNew();
        string? lastPayload = null;
        HttpStatusCode? lastStatus = null;

        while (deadline.Elapsed < s_readbackTimeout)
        {
            using var response = await http.GetAsync(uri).ConfigureAwait(false);
            lastStatus = response.StatusCode;
            lastPayload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.OK)
            {
                using var document = JsonDocument.Parse(lastPayload);
                if (expectedValues.All(value => ContainsString(document.RootElement, value)))
                    return;
            }

            await Task.Delay(200).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"Readback {uri} did not contain [{string.Join(", ", expectedValues)}] within " +
            $"{s_readbackTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds. " +
            $"Last status: {lastStatus}; payload: {lastPayload}");
    }

    private static bool ContainsString(JsonElement element, string expected)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return string.Equals(element.GetString(), expected, StringComparison.Ordinal);
            case JsonValueKind.Array:
                return element.EnumerateArray().Any(item => ContainsString(item, expected));
            case JsonValueKind.Object:
                return element.EnumerateObject().Any(property => ContainsString(property.Value, expected));
            default:
                return false;
        }
    }

    private static async Task<int> GetPageItemCountAsync(HttpClient http, Uri uri)
    {
        using var response = await http.GetAsync(uri).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (response.StatusCode is not HttpStatusCode.OK)
            throw new InvalidOperationException($"GET {uri} returned {(int)response.StatusCode}: {payload}");

        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind is not JsonValueKind.Array)
            throw new InvalidOperationException($"GET {uri} did not return a page with an items array: {payload}");
        return items.GetArrayLength();
    }

    private static async Task VerifyGrpcAuthAsync(Uri grpcBase)
    {
        using var channel = GrpcChannel.ForAddress(grpcBase);
        var client = new TraceService.TraceServiceClient(channel);

        try
        {
            _ = await client.ExportAsync(
                    BuildTrace(
                        "3af7651916cd43dd8448eb211c80319c",
                        "e7ad6b7169203331",
                        "aot-smoke-grpc-auth",
                        "aot-smoke-grpc-auth-span",
                        checked((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000UL)),
                    deadline: DateTime.UtcNow.Add(s_requestTimeout))
                .ResponseAsync.ConfigureAwait(false);
        }
        catch (RpcException ex) when (ex.StatusCode is GrpcStatusCode.Unauthenticated)
        {
            Console.WriteLine("[driver] keyless gRPC export rejected with UNAUTHENTICATED");
            return;
        }

        throw new InvalidOperationException("Keyless gRPC export was not rejected with UNAUTHENTICATED.");
    }

    private static async Task VerifyStockSdkAsync(Uri apiBase)
    {
        var endpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("OTEL_EXPORTER_OTLP_ENDPOINT is not set.");

        var configured = Environment.GetEnvironmentVariables()
            .Keys
            .Cast<string>()
            .Where(variable => variable.StartsWith("OTEL_EXPORTER_OTLP_", StringComparison.Ordinal)
                               && !string.Equals(
                                   variable,
                                   "OTEL_EXPORTER_OTLP_ENDPOINT",
                                   StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (configured.Length > 0)
            throw new InvalidOperationException(
                $"Stock SDK probe must configure only OTEL_EXPORTER_OTLP_ENDPOINT; also set: {string.Join(", ", configured)}");

        var serviceName = $"aot-smoke-stock-sdk-{Guid.NewGuid():N}";
        using var source = new ActivitySource(StockSourceName);
        using var provider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateEmpty().AddService(serviceName))
            .AddSource(StockSourceName)
            .AddOtlpExporter()
            .Build();

        string traceId;
        string spanId;
        using (var activity = source.StartActivity("aot-smoke-stock-sdk-span", ActivityKind.Client)
                              ?? throw new InvalidOperationException("Stock OTel SDK did not sample the smoke activity."))
        {
            activity.SetTag("smoke.transport", "stock-default");
            traceId = activity.TraceId.ToHexString();
            spanId = activity.SpanId.ToHexString();
        }

        if (!provider.ForceFlush((int)s_requestTimeout.TotalMilliseconds))
            throw new InvalidOperationException("Stock OTel SDK exporter did not flush successfully.");

        await VerifyTraceReadbackAsync(s_http, apiBase, traceId, spanId).ConfigureAwait(false);
        Console.WriteLine($"[driver] stock OTel SDK default gRPC export read back as trace {traceId}");
    }

    private static ExportTraceServiceRequest BuildTrace(
        string traceId,
        string spanId,
        string serviceName,
        string spanName,
        ulong startTimeUnixNano,
        bool includeSessionAttributes = false)
    {
        var resource = new OtlpResource
        {
            Attributes =
            {
                StringAttribute("service.name", serviceName)
            }
        };
        var span = new OtlpSpan
        {
            TraceId = ByteString.CopyFrom(Convert.FromHexString(traceId)),
            SpanId = ByteString.CopyFrom(Convert.FromHexString(spanId)),
            Name = spanName,
            Kind = OtlpSpan.Types.SpanKind.Server,
            StartTimeUnixNano = startTimeUnixNano,
            EndTimeUnixNano = startTimeUnixNano + 1_000_000UL,
            Status = new OpenTelemetry.Proto.Trace.V1.Status
            {
                Code = OpenTelemetry.Proto.Trace.V1.Status.Types.StatusCode.Ok
            }
        };

        if (includeSessionAttributes)
        {
            resource.Attributes.Add(StringAttribute("session.id", "aot-smoke-session"));
            resource.Attributes.Add(StringAttribute("gen_ai.provider.name", "anthropic"));
            span.Attributes.Add(StringAttribute("gen_ai.request.model", JsonModel));
        }

        return new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    Resource = resource,
                    ScopeSpans =
                    {
                        new ScopeSpans
                        {
                            Scope = new InstrumentationScope { Name = "aot-smoke" },
                            Spans = { span }
                        }
                    }
                }
            }
        };
    }

    private static ExportLogsServiceRequest BuildLogs(ulong timeUnixNano) =>
        new()
        {
            ResourceLogs =
            {
                new OtlpResourceLogs
                {
                    Resource = new OtlpResource
                    {
                        Attributes = { StringAttribute("service.name", GrpcLogService) }
                    },
                    ScopeLogs =
                    {
                        new OtlpScopeLogs
                        {
                            Scope = new InstrumentationScope { Name = "aot-smoke" },
                            LogRecords =
                            {
                                new OtlpLogRecord
                                {
                                    TimeUnixNano = timeUnixNano,
                                    ObservedTimeUnixNano = timeUnixNano,
                                    SeverityNumber = SeverityNumber.Info,
                                    SeverityText = "INFO",
                                    EventName = GrpcLogEvent,
                                    Body = new AnyValue { StringValue = "aot smoke gRPC log body" },
                                    TraceId = ByteString.CopyFrom(Convert.FromHexString(GrpcTraceId)),
                                    SpanId = ByteString.CopyFrom(Convert.FromHexString(GrpcSpanId))
                                }
                            }
                        }
                    }
                }
            }
        };

    private static HistogramDataPoint HistogramPoint(ulong timeUnixNano)
    {
        var point = new HistogramDataPoint { TimeUnixNano = timeUnixNano, Count = 3, Sum = 6 };
        point.ExplicitBounds.AddRange([1.0, 5.0]);
        point.BucketCounts.AddRange([1UL, 1UL, 1UL]);
        return point;
    }

    private static ExportMetricsServiceRequest BuildMetrics(ulong timeUnixNano) =>
        new()
        {
            ResourceMetrics =
            {
                new OtlpResourceMetrics
                {
                    Resource = new OtlpResource
                    {
                        Attributes = { StringAttribute("service.name", "aot-smoke-metrics") }
                    },
                    ScopeMetrics =
                    {
                        new OtlpScopeMetrics
                        {
                            Scope = new InstrumentationScope { Name = "aot-smoke" },
                            Metrics =
                            {
                                new OtlpMetric
                                {
                                    Name = "aot.smoke.gauge",
                                    Gauge = new Gauge
                                    {
                                        DataPoints =
                                        {
                                            NumberPoint(timeUnixNano, 1),
                                            NumberPoint(timeUnixNano + 1, 2),
                                            NumberPoint(timeUnixNano + 2, 3)
                                        }
                                    }
                                },
                                new OtlpMetric
                                {
                                    Name = "aot.smoke.histogram",
                                    Histogram = new Histogram
                                    {
                                        AggregationTemporality = AggregationTemporality.Delta,
                                        DataPoints = { HistogramPoint(timeUnixNano) }
                                    }
                                },
                                new OtlpMetric
                                {
                                    Name = "aot.smoke.sum",
                                    Sum = new Sum
                                    {
                                        AggregationTemporality = AggregationTemporality.Cumulative,
                                        IsMonotonic = true,
                                        DataPoints = { NumberPoint(timeUnixNano, 9_007_199_254_740_993) }
                                    }
                                },
                                new OtlpMetric
                                {
                                    Name = "aot.smoke.exponential",
                                    ExponentialHistogram = new ExponentialHistogram
                                    {
                                        AggregationTemporality = AggregationTemporality.Delta,
                                        DataPoints = { new ExponentialHistogramDataPoint
                                        {
                                            TimeUnixNano = timeUnixNano, Count = 3, Sum = 4, Scale = 2, ZeroCount = 1,
                                            Positive = new ExponentialHistogramDataPoint.Types.Buckets { Offset = -2, BucketCounts = { 2UL } }
                                        } }
                                    }
                                },
                                new OtlpMetric
                                {
                                    Name = SummaryMetricName,
                                    Summary = new Summary
                                    {
                                        DataPoints = { new SummaryDataPoint
                                        {
                                            TimeUnixNano = timeUnixNano, Count = 7, Sum = 20,
                                            QuantileValues = { new SummaryDataPoint.Types.ValueAtQuantile { Quantile = 0.95, Value = 6 } }
                                        } }
                                    }
                                },
                                new OtlpMetric
                                {
                                    Name = InvalidMetricName,
                                    Gauge = new Gauge { DataPoints = { NumberPoint(0, 1) } }
                                }
                            }
                        }
                    }
                }
            }
        };

    private static NumberDataPoint NumberPoint(ulong timeUnixNano, long value) =>
        new()
        {
            TimeUnixNano = timeUnixNano,
            AsInt = value
        };

    private static KeyValue StringAttribute(string key, string value) =>
        new()
        {
            Key = key,
            Value = new AnyValue { StringValue = value }
        };

    private static void AssertTraceSuccess(ExportTraceServiceResponse response)
    {
        if (response.PartialSuccess is { RejectedSpans: > 0 } partial)
            throw new InvalidOperationException(
                $"Trace export rejected {partial.RejectedSpans} spans: {partial.ErrorMessage}");
    }

    private static void AssertLogsSuccess(ExportLogsServiceResponse response)
    {
        if (response.PartialSuccess is { RejectedLogRecords: > 0 } partial)
            throw new InvalidOperationException(
                $"Log export rejected {partial.RejectedLogRecords} records: {partial.ErrorMessage}");
    }

    // Valid summaries are retained as original observations. Only the zero-time point is malformed:
    // https://github.com/open-telemetry/opentelemetry-proto/blob/v1.11.1/opentelemetry/proto/metrics/v1/metrics.proto
    private static void AssertMetricsStored(ExportMetricsServiceResponse response)
    {
        if (response.PartialSuccess is null)
            throw new InvalidOperationException("Metrics response omitted partial_success.");
        AssertEqual(1, response.PartialSuccess.RejectedDataPoints, "rejected metric data-point count");
        if (!response.PartialSuccess.ErrorMessage.Contains(
                InvalidMetricName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Metrics partial_success did not name the declined instrument: {response.PartialSuccess.ErrorMessage}");
        }
    }

    private static async Task VerifyOriginalMetricPointsAsync(HttpClient http, Uri apiBase)
    {
        (string Name, string Type, int Count)[] expected =
        [
            ("aot.smoke.gauge", "number", 3),
            ("aot.smoke.sum", "number", 1),
            ("aot.smoke.histogram", "histogram", 1),
            ("aot.smoke.exponential", "exponential_histogram", 1),
            (SummaryMetricName, "summary", 1)
        ];
        foreach (var (name, type, count) in expected)
        {
            var seriesUri = new Uri(apiBase, $"api/v1/metrics/{name}/series");
            await VerifyJsonValuesAsync(http, seriesUri, name).ConfigureAwait(false);
            using var series = JsonDocument.Parse(await http.GetStringAsync(seriesUri).ConfigureAwait(false));
            var items = series.RootElement.GetProperty("items");
            AssertEqual(1, items.GetArrayLength(), $"{name} series count");
            var seriesId = items[0].GetProperty("series_id").GetString();
            var pointsUri = new Uri(apiBase, $"api/v1/metrics/{name}/series/{seriesId}/points");
            await VerifyJsonValuesAsync(http, pointsUri, type).ConfigureAwait(false);
            using var points = JsonDocument.Parse(await http.GetStringAsync(pointsUri).ConfigureAwait(false));
            var observations = points.RootElement.GetProperty("items");
            AssertEqual(false, points.RootElement.GetProperty("has_more").GetBoolean(), $"{name} complete page");
            AssertEqual(count, observations.GetArrayLength(), $"{name} original point count after identical HTTP/gRPC exports");
            foreach (var point in observations.EnumerateArray())
                AssertEqual(type, point.GetProperty("data").GetProperty("type").GetString(), $"{name} point shape");
            var data = observations[0].GetProperty("data");
            if (name is "aot.smoke.sum")
                AssertEqual("9007199254740993", data.GetProperty("value").GetProperty("value").GetString(), "exact int64");
            else if (name is "aot.smoke.histogram")
            {
                AssertEqual("3", data.GetProperty("count").GetString(), "histogram count");
                AssertEqual(3, data.GetProperty("bucket_counts").GetArrayLength(), "explicit bucket count");
                AssertEqual(6d, data.GetProperty("sum").GetDouble(), "histogram sum");
            }
            else if (name is "aot.smoke.exponential")
            {
                AssertEqual(2, data.GetProperty("scale").GetInt32(), "native exponential scale");
                AssertEqual(-2, data.GetProperty("positive").GetProperty("offset").GetInt32(), "native exponential offset");
                AssertEqual("2", data.GetProperty("positive").GetProperty("bucket_counts")[0].GetString(), "native bucket count");
                AssertEqual("1", data.GetProperty("zero_count").GetString(), "native zero count");
            }
            else if (name == SummaryMetricName)
            {
                AssertEqual("7", data.GetProperty("count").GetString(), "summary count");
                AssertEqual(20d, data.GetProperty("sum").GetDouble(), "summary sum");
                AssertEqual(0.95d, data.GetProperty("quantile_values")[0].GetProperty("quantile").GetDouble(), "summary quantile");
                AssertEqual(6d, data.GetProperty("quantile_values")[0].GetProperty("value").GetDouble(), "summary quantile value");
            }
        }
    }

    private static Uri ParseBaseUri(string value)
    {
        if (!Uri.TryCreate(value.EndsWith('/') ? value : value + '/', UriKind.Absolute,
                out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException($"'{value}' is not an absolute HTTP base URI.");
        }

        return uri;
    }

    private static void AssertEqual<T>(T expected, T actual, string assertion)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{assertion}: expected '{expected}', got '{actual}'.");
    }
}
