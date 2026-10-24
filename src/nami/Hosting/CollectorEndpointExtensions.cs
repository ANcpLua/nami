using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using Nami.Collector.ApiKeys;
using Nami.Collector.Grpc;
using Nami.Collector.Storage;
using ExportLogsServiceResponse = OpenTelemetry.Proto.Collector.Logs.V1.ExportLogsServiceResponse;
using ExportMetricsServiceResponse = OpenTelemetry.Proto.Collector.Metrics.V1.ExportMetricsServiceResponse;
using ExportTraceServiceResponse = OpenTelemetry.Proto.Collector.Trace.V1.ExportTraceServiceResponse;

namespace Nami.Collector.Hosting;

internal static partial class CollectorEndpointExtensions
{
    public static WebApplication MapNamiCollectorEndpoints(this WebApplication app)
    {
        app.MapGrpcService<TraceServiceImpl>();
        app.MapGrpcService<LogsServiceImpl>();
        app.MapGrpcService<MetricsServiceImpl>();

        var otlp = app.MapGroup("/v1");
        otlp.MapPost("/traces", IngestOtlpTracesAsync);
        otlp.MapPost("/logs", IngestOtlpLogsAsync);
        otlp.MapPost("/metrics", IngestOtlpMetricsAsync);

        return app;
    }

    internal static Task<IResult> IngestOtlpTracesAsync(
        HttpContext context,
        INamiStore store,
        CancellationToken ct) =>
        IngestOtlpAsync(
            context,
            "traces",
            OtlpPayloadParser.ParseTraceRequestAsync,
            static request => request.ResourceSpans.Count is 0,
            new ExportTraceServiceResponse(),
            OtlpConverter.ConvertTraceRequest,
            async (batch, token) =>
            {
                var spans = IngestionStorageMapper.ToSpanStorageRows(batch, AuthenticatedProject.ForHttpIngest(context));
                if (spans.Count > 0) await store.EnqueueAsync(new SpanBatch(spans), token);
            },
            OtlpExportAck.Create,
            OtlpTracesLog.FailedToProcessPayload,
            ct);

    internal static Task<IResult> IngestOtlpLogsAsync(
        HttpContext context,
        INamiStore store,
        TimeProvider timeProvider,
        CancellationToken ct) =>
        IngestOtlpAsync(
            context,
            "logs",
            OtlpPayloadParser.ParseLogsRequestAsync,
            static request => request.ResourceLogs.Count is 0,
            new ExportLogsServiceResponse(),
            request => OtlpConverter.ConvertLogs(request, timeProvider),
            async (batch, token) =>
            {
                var logs = IngestionStorageMapper.ToLogStorageRows(batch, AuthenticatedProject.ForHttpIngest(context));
                if (logs.Count > 0) await store.InsertLogsAsync(logs, token);
            },
            OtlpExportAck.Create,
            OtlpLogsLog.FailedToProcessPayload,
            ct);

    internal static Task<IResult> IngestOtlpMetricsAsync(
        HttpContext context,
        INamiStore store,
        CancellationToken ct) =>
        IngestOtlpAsync(
            context,
            "metrics",
            OtlpPayloadParser.ParseMetricsRequestAsync,
            static request => request.ResourceMetrics.Count is 0,
            new ExportMetricsServiceResponse(),
            OtlpConverter.ConvertMetrics,
            async (batch, token) =>
            {
                var write = MetricStorageMapper.ToStorageRows(batch, AuthenticatedProject.ForHttpIngest(context));
                if (write.Observations.Count > 0)
                    await store.InsertMetricsAsync(write.Series, write.Observations, token);
            },
            OtlpExportAck.Create,
            OtlpMetricsLog.FailedToProcessPayload,
            ct);

    /// <summary>
    /// One OTLP/HTTP export of any signal: an undecodable body is 413 over the size bound, 415 for a media type or
    /// content encoding OTLP does not define and 400 otherwise; a decoded export the converter or the storage mapper
    /// rejects is 400, and one the store cannot persist is a retryable 503.
    /// </summary>
    private static async Task<IResult> IngestOtlpAsync<TRequest, TBatch>(
        HttpContext context,
        string signal,
        Func<HttpRequest, OtlpPayloadEncoding, CancellationToken, Task<TRequest>> parse,
        Func<TRequest, bool> isEmpty,
        IMessage emptyResponse,
        Func<TRequest, TBatch> convert,
        Func<TBatch, CancellationToken, Task> persist,
        Func<TBatch, IMessage> acknowledge,
        Action<ILogger, Exception> logFailure,
        CancellationToken ct)
    {
        var encoding = OtlpPayloadEncoding.Json;
        TRequest request;
        try
        {
            encoding = OtlpPayloadParser.GetEncoding(context.Request.ContentType);
            request = await parse(context.Request, encoding, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OtlpPayloadTooLargeException ex)
        {
            // OTLP 1.11 (request size limits): a body over the decompressed bound is 413, never 400.
            return Failure(ex, StatusCodes.Status413PayloadTooLarge, OtlpPayloadParser.PayloadTooLargeMessage);
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
        catch (InvalidDataException ex)
        {
            return Failure(ex, StatusCodes.Status400BadRequest, $"The OTLP {signal} payload is invalid.");
        }
        catch (Exception ex)
        {
            return Failure(ex, StatusCodes.Status503ServiceUnavailable, $"The collector could not persist the OTLP {signal} payload.");
        }

        IResult Failure(Exception exception, int statusCode, string message)
        {
            var category = $"Otlp{char.ToUpperInvariant(signal[0])}{signal[1..]}Endpoint";
            logFailure(context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(category), exception);
            return OtlpHttpResult.Failure(statusCode, encoding, message);
        }
    }

}

internal static partial class OtlpLogsLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to process OTLP logs payload")]
    public static partial void FailedToProcessPayload(ILogger logger, Exception exception);
}

internal static partial class OtlpMetricsLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to process OTLP metrics payload")]
    public static partial void FailedToProcessPayload(ILogger logger, Exception exception);
}

internal static partial class OtlpTracesLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to process OTLP traces payload")]
    public static partial void FailedToProcessPayload(ILogger logger, Exception exception);
}
