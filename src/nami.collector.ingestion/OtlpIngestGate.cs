using Microsoft.Extensions.Options;

namespace Nami.Collector.Ingestion;

/// <summary>
///     How many export requests nami reads, parses, converts and stores at once, in the whole process. nami
///     receiver policy: the bounds of one request (<see cref="IngestLimits" />) bound what one request costs, and
///     this bounds how many pay that at the same time: <see cref="IngestLimits.MaxConcurrentRequests" /> slots.
///     <para>
///         A request takes its slot before a byte of its body is read, so a request that waits holds no buffer.
///         It waits <see cref="IngestLimits.MaxRequestWaitSeconds" /> at most. Then it is turned away with an
///         answer an exporter retries, unlike a request over a size bound, which is refused for good: OTLP/HTTP
///         503 with Retry-After, OTLP/gRPC UNAVAILABLE.
///     </para>
/// </summary>
internal sealed class OtlpIngestGate(IOptions<IngestLimits> limits) : IDisposable
{
    /// <summary>What a request that found no slot in time is told, over both transports.</summary>
    internal const string BusyMessage = "The collector is ingesting as many requests as it takes at once; send the export again.";

    /// <summary>The seconds an exporter is asked to wait before it sends the export again.</summary>
    internal const int RetryAfterSeconds = 1;

    private readonly SemaphoreSlim _slots = new(limits.Value.MaxConcurrentRequests, limits.Value.MaxConcurrentRequests);
    private readonly TimeSpan _wait = TimeSpan.FromSeconds(limits.Value.MaxRequestWaitSeconds);

    public void Dispose() => _slots.Dispose();

    /// <summary>A slot, or false when none came free within the wait.</summary>
    internal Task<bool> EnterAsync(CancellationToken cancellationToken) => _slots.WaitAsync(_wait, cancellationToken);

    internal void Leave() => _slots.Release();

    /// <summary>The slots no request holds at this moment.</summary>
    internal int FreeSlots => _slots.CurrentCount;
}

/// <summary>
///     Puts every OTLP export, over HTTP and over gRPC, through the <see cref="OtlpIngestGate" /> before its body
///     is read. For gRPC that has to be middleware: the server reads and decodes the message before an interceptor
///     or the service sees the call.
/// </summary>
internal sealed class OtlpIngestGateMiddleware(RequestDelegate next, OtlpIngestGate gate)
{
    private const string GrpcContentType = "application/grpc";
    private const string GrpcStatusUnavailable = "14";

    public async Task InvokeAsync(HttpContext context)
    {
        var grpc = context.Request.ContentType?.StartsWith(GrpcContentType, StringComparison.OrdinalIgnoreCase) is true;
        if (!HttpMethods.IsPost(context.Request.Method) ||
            (!grpc && !OtlpConstants.IsOtlpPath(context.Request.Path.Value ?? "")))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (!await gate.EnterAsync(context.RequestAborted).ConfigureAwait(false))
        {
            await TurnAwayAsync(context, grpc).ConfigureAwait(false);
            return;
        }

        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            gate.Leave();
        }
    }

    // Backpressure, which an exporter retries. gRPC: a response of trailers only, UNAVAILABLE. HTTP: 503 with
    // Retry-After and an OTLP Status in the encoding of the request.
    private static Task TurnAwayAsync(HttpContext context, bool grpc)
    {
        if (grpc)
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = GrpcContentType;
            context.Response.Headers["grpc-status"] = GrpcStatusUnavailable;
            context.Response.Headers["grpc-message"] = Uri.EscapeDataString(OtlpIngestGate.BusyMessage);
            return Task.CompletedTask;
        }

        context.Response.Headers.RetryAfter = OtlpIngestGate.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        _ = OtlpPayloadParser.TryGetEncoding(context.Request.ContentType, out var encoding);
        return OtlpHttpResult.Failure(StatusCodes.Status503ServiceUnavailable, encoding, OtlpIngestGate.BusyMessage)
            .ExecuteAsync(context);
    }
}
