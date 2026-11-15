using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Nami.Collector.Ingestion;
using Nami.Collector.Ingestion.Models;
using Nami.Collector.Storage;
using Nami.Grpc;
using Nami.Tools;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Nami.IntegrationTests.Fuzz;

/// <summary>A defect the oracle saw: the stage, a stable signature for deduplication, and the detail.</summary>
internal sealed record FuzzFinding(string Stage, string Signature, string Detail);

/// <summary>
///     Runs one OTLP/HTTP body through what the endpoint runs (decode, convert, map, persist, acknowledge) and then
///     every read tool over the stored snapshot, and judges each stage the way the endpoint answers it: a decode
///     failure is 400/413/415, an <see cref="InvalidDataException" /> after decoding is 400, and any other exception is
///     a retryable 503, which for a deterministic input is a defect. Accepted records must also hold every rule the
///     converter promises, and every tool must answer over what was stored.
/// </summary>
internal sealed partial class OtlpOracle(TimeProvider clock)
{
    private static readonly HashSet<Type> s_decodeFailures =
    [
        typeof(InvalidProtocolBufferException), typeof(InvalidJsonException), typeof(InvalidDataException),
        typeof(JsonException), typeof(FormatException), typeof(OtlpPayloadTooLargeException),
        typeof(OtlpUnsupportedMediaTypeException), typeof(OtlpUnsupportedContentEncodingException)
    ];

    internal async Task<List<FuzzFinding>> RunAsync(OtlpCase input, DuckDbStore store, CancellationToken ct)
    {
        var findings = new List<FuzzFinding>();
        IMessage request;
        try
        {
            request = await ParseAsync(input, ct);
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            if (!s_decodeFailures.Contains(error.GetType()))
                findings.Add(Finding("decode", error, "an undeclared decode exception (still answered 400)"));
            return findings;
        }

        if (input is { ContentEncoding: null, ContentType: "application/x-protobuf" })
            CompareGrpc(input, request, findings);
        var total = Count(request);
        if (total == 0) return findings;

        object batch;
        try
        {
            batch = input.Signal switch
            {
                FuzzSignal.Traces => OtlpConverter.ConvertTraceRequest((ExportTraceServiceRequest)request),
                FuzzSignal.Logs => OtlpConverter.ConvertLogs((ExportLogsServiceRequest)request, clock),
                _ => OtlpConverter.ConvertMetrics((ExportMetricsServiceRequest)request)
            };
        }
        catch (InvalidDataException)
        {
            return findings;
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            findings.Add(Finding("convert", error, "the converter threw; the endpoint answers a retryable 503"));
            return findings;
        }

        CheckBatch(batch, total, findings);
        try
        {
            switch (batch)
            {
                case TraceIngestionBatch traces:
                    OtlpExportAck.Create(traces);
                    break;
                case LogIngestionBatch logs:
                    OtlpExportAck.Create(logs);
                    break;
                case MetricIngestionBatch metrics:
                    OtlpExportAck.Create(metrics);
                    break;
            }
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            findings.Add(Finding("acknowledge", error, "the partial-success response could not be built"));
        }

        HashSet<string> projects;
        try
        {
            projects = await PersistAsync(batch, store, ct);
        }
        catch (InvalidDataException)
        {
            return findings;
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            findings.Add(Finding("persist", error, "mapping or storage threw; the endpoint answers a retryable 503"));
            return findings;
        }

        foreach (var project in projects.Order(StringComparer.Ordinal).Take(3))
            await ReadBackAsync(store, project, findings, ct);
        return findings;
    }

    private static async Task<IMessage> ParseAsync(OtlpCase input, CancellationToken ct)
    {
        await using var body = new MemoryStream(input.Body, false);
        var context = new DefaultHttpContext();
        context.Request.Body = body;
        context.Request.ContentType = input.ContentType;
        context.Request.ContentLength = input.Body.Length;
        if (input.ContentEncoding is not null) context.Request.Headers.ContentEncoding = input.ContentEncoding;
        var encoding = OtlpPayloadParser.GetEncoding(input.ContentType);
        return input.Signal switch
        {
            FuzzSignal.Traces => await OtlpPayloadParser.ParseTraceRequestAsync(context.Request, encoding, ct),
            FuzzSignal.Logs => await OtlpPayloadParser.ParseLogsRequestAsync(context.Request, encoding, ct),
            _ => await OtlpPayloadParser.ParseMetricsRequestAsync(context.Request, encoding, ct)
        };
    }

    /// <summary>The gRPC marshaller decodes the same protobuf bytes; it must yield the message the HTTP parser yields.</summary>
    private static void CompareGrpc(OtlpCase input, IMessage request, List<FuzzFinding> findings)
    {
        IMessage grpc;
        try
        {
            var bytes = new System.Buffers.ReadOnlySequence<byte>(input.Body);
            grpc = input.Signal switch
            {
                FuzzSignal.Traces => OtlpPayloadParser.ParseProtobuf(ExportTraceServiceRequest.Parser,
                    ExportTraceServiceRequest.Descriptor, bytes),
                FuzzSignal.Logs => OtlpPayloadParser.ParseProtobuf(ExportLogsServiceRequest.Parser,
                    ExportLogsServiceRequest.Descriptor, bytes),
                _ => OtlpPayloadParser.ParseProtobuf(ExportMetricsServiceRequest.Parser,
                    ExportMetricsServiceRequest.Descriptor, bytes)
            };
        }
        catch (Exception error)
        {
            findings.Add(Finding("grpc decode", error, "the gRPC marshaller refused bytes the HTTP parser decoded"));
            return;
        }

        if (!grpc.Equals(request))
            findings.Add(new FuzzFinding("grpc decode", "grpc-differential", "the gRPC and HTTP decoders disagree"));
    }

    private static long Count(IMessage request) => request switch
    {
        ExportTraceServiceRequest traces => traces.ResourceSpans.Sum(static r => r.ScopeSpans.Sum(static s => (long)s.Spans.Count)),
        ExportLogsServiceRequest logs => logs.ResourceLogs.Sum(static r => r.ScopeLogs.Sum(static s => (long)s.LogRecords.Count)),
        ExportMetricsServiceRequest metrics => metrics.ResourceMetrics.Sum(static r => r.ScopeMetrics.Sum(static s =>
            s.Metrics.Sum(static m => m.DataCase switch
            {
                OpenTelemetry.Proto.Metrics.V1.Metric.DataOneofCase.Gauge => (long)m.Gauge.DataPoints.Count,
                OpenTelemetry.Proto.Metrics.V1.Metric.DataOneofCase.Sum => m.Sum.DataPoints.Count,
                OpenTelemetry.Proto.Metrics.V1.Metric.DataOneofCase.Histogram => m.Histogram.DataPoints.Count,
                OpenTelemetry.Proto.Metrics.V1.Metric.DataOneofCase.ExponentialHistogram => m.ExponentialHistogram.DataPoints.Count,
                OpenTelemetry.Proto.Metrics.V1.Metric.DataOneofCase.Summary => m.Summary.DataPoints.Count,
                _ => 0L
            }))),
        _ => 0
    };

    /// <summary>What the converter promises about every record it accepts, and that it accounts for every record.</summary>
    private static void CheckBatch(object batch, long total, List<FuzzFinding> findings)
    {
        switch (batch)
        {
            case TraceIngestionBatch traces:
                if (traces.Spans.Count + traces.RejectedSpans != total)
                    findings.Add(new FuzzFinding("count", "trace-count", $"{traces.Spans.Count} accepted + {traces.RejectedSpans} rejected != {total} sent"));
                if (traces.RejectedSpans > 0 && string.IsNullOrEmpty(traces.RejectionMessage))
                    findings.Add(new FuzzFinding("count", "trace-reason", "rejected spans without a reason"));
                foreach (var span in traces.Spans)
                {
                    Rule(Id(span.TraceId, 32), "span trace_id is not a nonzero 16-byte id", findings);
                    Rule(Id(span.SpanId, 16), "span span_id is not a nonzero 8-byte id", findings);
                    Rule(span.ParentSpanId is null || Id(span.ParentSpanId, 16), "span parent_span_id is malformed", findings);
                    Rule(span.EndTimeUnixNano >= span.StartTimeUnixNano && span.EndTimeUnixNano != 0, "span ends before it starts or has no times", findings);
                    Rule(span.Kind is null or >= 0 and <= 5, "span kind outside 0-5", findings);
                    Rule(span.StatusCode is null or >= 0 and <= 2, "span status outside 0-2", findings);
                    Rule(span.Links.All(static link => Id(link.TraceId, 32) && Id(link.SpanId, 16)), "span link id is malformed", findings);
                    Rule(!string.IsNullOrWhiteSpace(span.ServiceName), "span service name is empty", findings);
                }

                break;
            case LogIngestionBatch logs:
                if (logs.Logs.Count + logs.RejectedLogRecords != total)
                    findings.Add(new FuzzFinding("count", "log-count", $"{logs.Logs.Count} accepted + {logs.RejectedLogRecords} rejected != {total} sent"));
                foreach (var log in logs.Logs)
                {
                    Rule(log.TraceId is null || Id(log.TraceId, 32), "log trace_id is malformed", findings);
                    Rule(log.SpanId is null || Id(log.SpanId, 16), "log span_id is malformed", findings);
                    Rule(log.SeverityNumber is >= 0 and <= 24, "log severity outside 0-24", findings);
                }

                break;
            case MetricIngestionBatch metrics:
                if (metrics.Points.Count + metrics.RejectedDataPoints != total)
                    findings.Add(new FuzzFinding("count", "metric-count", $"{metrics.Points.Count} accepted + {metrics.RejectedDataPoints} rejected != {total} sent"));
                foreach (var point in metrics.Points)
                {
                    Rule(point.TimeUnixNano != 0, "metric point without time", findings);
                    Rule(!string.IsNullOrWhiteSpace(point.MetricName), "metric without a name", findings);
                    if (point.Data is MetricHistogramPointData histogram)
                    {
                        var bounds = histogram.ExplicitBounds;
                        Rule(bounds.All(double.IsFinite) && bounds.Zip(bounds.Skip(1)).All(static pair => pair.First < pair.Second),
                            "histogram bounds not finite and ascending", findings);
                        Rule(histogram.BucketCounts.Count is 0 || histogram.BucketCounts.Count == bounds.Count + 1,
                            "histogram bucket count does not match its bounds", findings);
                    }

                    Rule(point.Exemplars.All(static e => (e.TraceId is null || Id(e.TraceId, 32)) && (e.SpanId is null || Id(e.SpanId, 16))),
                        "exemplar id is malformed", findings);
                }

                break;
        }
    }

    private static async Task<HashSet<string>> PersistAsync(object batch, DuckDbStore store, CancellationToken ct)
    {
        switch (batch)
        {
            case TraceIngestionBatch traces:
                var spans = IngestionStorageMapper.ToSpanStorageRows(traces);
                if (spans.Count > 0) await store.EnqueueAsync(new SpanBatch(spans), ct);
                return [.. spans.Select(static row => row.ProjectId)];
            case LogIngestionBatch logs:
                var rows = IngestionStorageMapper.ToLogStorageRows(logs);
                if (rows.Count > 0) await store.InsertLogsAsync(rows, ct);
                return [.. rows.Select(static row => row.ProjectId)];
            case MetricIngestionBatch metrics:
                var write = MetricStorageMapper.ToStorageRows(metrics);
                if (write.Observations.Count > 0) await store.InsertMetricsAsync(write.Series, write.Observations, ct);
                return [.. write.Series.Select(static row => row.ProjectId)];
            default:
                return [];
        }
    }

    /// <summary>Every tool over the stored snapshot, including one trace and one cursor page; none may throw.</summary>
    private static async Task ReadBackAsync(DuckDbStore store, string project, List<FuzzFinding> findings, CancellationToken ct)
    {
        Nami.Collector.Storage.TelemetrySnapshot snapshot;
        try
        {
            snapshot = await store.ReadSnapshotAsync(project, ct: ct);
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            findings.Add(Finding("snapshot", error, "the stored rows could not be read back"));
            return;
        }

        foreach (var span in snapshot.Spans)
        {
            Rule(Id(span.TraceId, 32) && Id(span.SpanId, 16), "stored span with a malformed id", findings);
            Rule(span.EndTimeUnixNano >= span.StartTimeUnixNano, "stored span ends before it starts", findings);
        }

        const string Since = "\"since\":\"1970-01-01T00:00:00Z\"";
        var probes = new List<(string Tool, string Input)>
        {
            ("sources_status", $"{{{Since}}}"),
            ("errors_recent", $"{{{Since},\"max_bytes\":262144}}"),
            ("traces_slowest", $"{{{Since},\"limit\":100,\"max_bytes\":262144}}"),
            ("changes_recent", $"{{{Since},\"max_bytes\":262144}}"),
            ("logs_search", $"{{{Since},\"query\":\"e\",\"max_bytes\":262144}}"),
            ("errors_recent", "{\"max_bytes\":1024}")
        };
        foreach (var traceId in snapshot.Spans.Select(static span => span.TraceId).Distinct(StringComparer.Ordinal).Take(2))
        {
            if (TraceIdPattern().IsMatch(traceId))
                probes.Add(("traces_get", $"{{{Since},\"trace_id\":\"{traceId}\",\"max_bytes\":4096}}"));
        }

        foreach (var (tool, text) in probes)
        {
            var input = text;
            for (var page = 0; page < 3 && input is not null; page++)
            {
                using var document = JsonDocument.Parse(input);
                ToolRunResult result;
                try
                {
                    result = ToolDispatcher.Run(tool, snapshot, document.RootElement);
                }
                catch (Exception error)
                {
                    findings.Add(Finding("tool " + tool, error, "the tool threw over stored data; the engine answers internal_error"));
                    break;
                }

                input = null;
                if (result.IsError)
                {
                    var code = result.Content.TryGetProperty("code", out var value) ? value.GetString() : null;
                    if (code is not ("invalid_argument" or "not_found" or "data_unavailable"))
                        findings.Add(new FuzzFinding("tool " + tool, "tool-error-code:" + code, result.Content.GetRawText()[..Math.Min(400, result.Content.GetRawText().Length)]));
                    if (Encoding.UTF8.GetByteCount(result.Content.GetRawText()) > 1024)
                        findings.Add(new FuzzFinding("tool " + tool, "tool-error-size", "a refusal exceeds 1024 bytes"));
                    break;
                }

                if (result.Content.TryGetProperty("next_cursor", out var cursor) && cursor.ValueKind == JsonValueKind.String)
                {
                    var next = JsonNode.Parse(text)!.AsObject();
                    next["cursor"] = cursor.GetString();
                    input = next.ToJsonString();
                }
            }
        }
    }

    private static bool Id(string? hex, int length) =>
        hex is not null && hex.Length == length && hex.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f') &&
        hex.Any(static c => c != '0');

    private static void Rule(bool holds, string rule, List<FuzzFinding> findings)
    {
        if (!holds && findings.All(finding => finding.Signature != "rule:" + rule))
            findings.Add(new FuzzFinding("invariant", "rule:" + rule, rule));
    }

    internal static FuzzFinding Finding(string stage, Exception error, string meaning)
    {
        var frame = error.StackTrace?.Split('\n').Select(static line => line.Trim())
            .FirstOrDefault(static line => line.Contains("Nami.", StringComparison.Ordinal)) ?? "";
        return new FuzzFinding(stage, $"{stage}:{error.GetType().Name}:{frame}",
            $"{meaning}: {error.GetType().FullName}: {error.Message}\n{error.StackTrace}");
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex TraceIdPattern();
}
