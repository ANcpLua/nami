using Google.Protobuf;
using Microsoft.Extensions.Options;
using Nami.ApiKeys;
using Nami.Collector.Storage;
using Nami.Grpc;
using ExportLogsServiceResponse = OpenTelemetry.Proto.Collector.Logs.V1.ExportLogsServiceResponse;
using ExportMetricsServiceResponse = OpenTelemetry.Proto.Collector.Metrics.V1.ExportMetricsServiceResponse;
using ExportTraceServiceResponse = OpenTelemetry.Proto.Collector.Trace.V1.ExportTraceServiceResponse;

namespace Nami.Hosting;

internal static class CollectorEndpointExtensions
{
    public static void MapNamiCollectorEndpoints(this WebApplication app)
    {
        app.MapGrpcService<TraceServiceImpl>();
        app.MapGrpcService<LogsServiceImpl>();
        app.MapGrpcService<MetricsServiceImpl>();

        var otlp = app.MapGroup("/v1");
        otlp.MapPost("/traces", IngestOtlpTracesAsync);
        otlp.MapPost("/logs", IngestOtlpLogsAsync);
        otlp.MapPost("/metrics", IngestOtlpMetricsAsync);
    }

    private static Task<IResult> IngestOtlpTracesAsync(
        HttpContext context,
        INamiStore store,
        IOptions<IngestLimits> limits,
        CancellationToken ct)
    {
        return IngestOtlpAsync(
            context,
            "traces",
            limits.Value,
            OtlpPayloadParser.ParseTraceRequestAsync,
            static request => request.ResourceSpans.Count is 0,
            new ExportTraceServiceResponse(),
            request => OtlpConverter.ConvertTraceRequest(request, limits.Value),
            async (batch, token) =>
            {
                var spans = IngestionStorageMapper.ToSpanStorageRows(batch,
                    AuthenticatedProject.ForHttpIngest(context));
                if (spans.Count > 0) await store.EnqueueAsync(new SpanBatch(spans), token);
            },
            OtlpExportAck.Create,
            ct);
    }

    private static Task<IResult> IngestOtlpLogsAsync(
        HttpContext context,
        INamiStore store,
        TimeProvider timeProvider,
        IOptions<IngestLimits> limits,
        CancellationToken ct)
    {
        return IngestOtlpAsync(
            context,
            "logs",
            limits.Value,
            OtlpPayloadParser.ParseLogsRequestAsync,
            static request => request.ResourceLogs.Count is 0,
            new ExportLogsServiceResponse(),
            request => OtlpConverter.ConvertLogs(request, timeProvider, limits.Value),
            async (batch, token) =>
            {
                var logs = IngestionStorageMapper.ToLogStorageRows(batch, AuthenticatedProject.ForHttpIngest(context));
                if (logs.Count > 0) await store.InsertLogsAsync(logs, token);
            },
            OtlpExportAck.Create,
            ct);
    }

    private static Task<IResult> IngestOtlpMetricsAsync(
        HttpContext context,
        INamiStore store,
        IOptions<IngestLimits> limits,
        CancellationToken ct)
    {
        return IngestOtlpAsync(
            context,
            "metrics",
            limits.Value,
            OtlpPayloadParser.ParseMetricsRequestAsync,
            static request => request.ResourceMetrics.Count is 0,
            new ExportMetricsServiceResponse(),
            request => OtlpConverter.ConvertMetrics(request, limits.Value),
            async (batch, token) =>
            {
                var write = MetricStorageMapper.ToStorageRows(batch, AuthenticatedProject.ForHttpIngest(context));
                if (write.Observations.Count > 0)
                    await store.InsertMetricsAsync(write.Series, write.Observations, token);
            },
            OtlpExportAck.Create,
            ct);
    }

    /// <summary>
    ///     One OTLP/HTTP export of any signal: an undecodable body is 413 over a size or message bound, 415 for a media type or
    ///     content encoding OTLP does not define and 400 otherwise; a decoded export the converter or the storage mapper
    ///     rejects is 400, and one the store cannot persist is a retryable 503.
    /// </summary>
    private static async Task<IResult> IngestOtlpAsync<TRequest, TBatch>(
        HttpContext context,
        string signal,
        IngestLimits limits,
        Func<HttpRequest, OtlpPayloadEncoding, IngestLimits, CancellationToken, Task<TRequest>> parse,
        Func<TRequest, bool> isEmpty,
        IMessage emptyResponse,
        Func<TRequest, TBatch> convert,
        Func<TBatch, CancellationToken, Task> persist,
        Func<TBatch, IMessage> acknowledge,
        CancellationToken ct)
    {
        var encoding = OtlpPayloadEncoding.Json;
        TRequest request;
        try
        {
            encoding = OtlpPayloadParser.GetEncoding(context.Request.ContentType);
            request = await parse(context.Request, encoding, limits, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OtlpPayloadTooLargeException ex)
        {
            // OTLP 1.11 (request size limits): a body over the wire or the decompressed bound is 413, never 400.
            // A request of too many messages carries its own status (OtlpPayloadTooLargeException).
            return Failure(ex, ex.HttpStatus, ex.Message);
        }
        catch (Exception ex) when (ex is OtlpUnsupportedMediaTypeException or OtlpUnsupportedContentEncodingException)
        {
            return Failure(
                ex,
                StatusCodes.Status415UnsupportedMediaType,
                "Content-Type must be application/x-protobuf or application/json; Content-Encoding must be gzip, identity, or absent.");
        }
        catch (Exception ex)
        {
            return Failure(ex, StatusCodes.Status400BadRequest, $"The OTLP {signal} payload could not be decoded.");
        }

        try
        {
            if (isEmpty(request))
                return OtlpHttpResult.Success(encoding, emptyResponse);

            var batch = convert(request);
            await persist(batch, ct);
            return OtlpHttpResult.Success(encoding, acknowledge(batch));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            // A record storage cannot encode as JSON fails the same way on every retry: never a retryable 503.
            return Failure(ex, StatusCodes.Status400BadRequest, $"The OTLP {signal} payload is invalid.");
        }
        catch (Exception ex)
        {
            return Failure(ex, StatusCodes.Status503ServiceUnavailable,
                $"The collector could not persist the OTLP {signal} payload.");
        }

        IResult Failure(Exception exception, int statusCode, string message)
        {
            var category = $"Otlp{char.ToUpperInvariant(signal[0])}{signal[1..]}Endpoint";
            OtlpFailureLog.FailedToProcessPayload(
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(category), signal,
                OtlpFailureLog.Describe(exception));
            return OtlpHttpResult.Failure(statusCode, encoding, message);
        }
    }
}

internal static partial class OtlpFailureLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to process OTLP {Signal} payload: {Failure}")]
    public static partial void FailedToProcessPayload(ILogger logger, string signal, string failure);

    /// <summary>
    ///     What nami's log says of a failed export: the type of the exception and its stack frames, and the same for
    ///     every exception inside it, each member of an <see cref="AggregateException" /> included. Frames name
    ///     code. The message is left out: it, and those of the libraries below, can repeat what was sent
    ///     (Google.Protobuf: "Value out of range: " and the number), and nami's log is nami's own. So never
    ///     <see cref="Exception.ToString" /> and never <see cref="Exception.Message" />.
    /// </summary>
    internal static string Describe(Exception exception)
    {
        var text = new System.Text.StringBuilder();
        // Depth first, in the order the exceptions are held: an exception, then what is inside it.
        var inside = new Stack<Exception>();
        inside.Push(exception);
        while (inside.TryPop(out var current))
        {
            if (text.Length > 0) text.Append(Environment.NewLine).Append(" ---> ");
            text.Append(current.GetType().FullName ?? current.GetType().Name);
            if (current.StackTrace is { Length: > 0 } frames) text.Append(Environment.NewLine).Append(frames);

            // InnerException is only the first of the exceptions an AggregateException holds.
            if (current is AggregateException several)
                for (var i = several.InnerExceptions.Count - 1; i >= 0; i--) inside.Push(several.InnerExceptions[i]);
            else if (current.InnerException is { } inner)
                inside.Push(inner);
        }

        return text.ToString();
    }
}
