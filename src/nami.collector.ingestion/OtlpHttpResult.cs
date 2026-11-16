using Google.Protobuf;
using RpcStatus = Google.Rpc.Status;

namespace Nami.Collector.Ingestion;

internal enum OtlpPayloadEncoding
{
    Protobuf,
    Json
}

/// <summary>
///     Writes the official OTLP protobuf response envelope in the request's encoding.
/// </summary>
internal sealed class OtlpHttpResult : IResult
{
    private readonly OtlpPayloadEncoding _encoding;
    private readonly IMessage _message;
    private readonly int _statusCode;

    private OtlpHttpResult(int statusCode, OtlpPayloadEncoding encoding, IMessage message)
    {
        _statusCode = statusCode;
        _encoding = encoding;
        _message = message;
    }

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = _statusCode;

        if (_encoding is OtlpPayloadEncoding.Protobuf)
        {
            httpContext.Response.ContentType = OtlpPayloadParser.ProtobufContentType;
            var payload = _message.ToByteArray();
            httpContext.Response.ContentLength = payload.Length;
            await httpContext.Response.Body.WriteAsync(payload, httpContext.RequestAborted).ConfigureAwait(false);
            return;
        }

        httpContext.Response.ContentType = OtlpPayloadParser.JsonContentType;
        var json = JsonFormatter.Default.Format(_message);
        var jsonPayload = Encoding.UTF8.GetBytes(json);
        httpContext.Response.ContentLength = jsonPayload.Length;
        await httpContext.Response.Body.WriteAsync(jsonPayload, httpContext.RequestAborted).ConfigureAwait(false);
    }

    public static IResult Success(OtlpPayloadEncoding encoding, IMessage response) =>
        new OtlpHttpResult(StatusCodes.Status200OK, encoding, response);

    public static IResult Failure(int statusCode, OtlpPayloadEncoding encoding, string message) =>
        new OtlpHttpResult(statusCode, encoding, new RpcStatus { Message = message });
}

internal sealed class OtlpUnsupportedMediaTypeException(string? contentType, Exception? innerException)
    : NotSupportedException($"Unsupported OTLP Content-Type '{contentType ?? "<missing>"}'.", innerException)
{
    public OtlpUnsupportedMediaTypeException(string? contentType) : this(contentType, null) { }
}

internal sealed class OtlpUnsupportedContentEncodingException(string? contentEncoding, Exception? innerException)
    : NotSupportedException($"Unsupported OTLP Content-Encoding '{contentEncoding ?? "<missing>"}'.", innerException)
{
    public OtlpUnsupportedContentEncodingException(string? contentEncoding) : this(contentEncoding, null) { }
}

/// <summary>
///     An OTLP export exceeded a bound of <see cref="IngestLimits" />: the bytes of its body on the wire or
///     decompressed, or the number of messages or records it holds. The endpoint handlers answer it with
///     <see cref="HttpStatus" /> and its message as the OTLP status message; OTLP 1.11 (request size limits) makes a
///     body over the size limit a 413 Content Too Large, and no status other than 429, 502, 503 and 504 is retried.
/// </summary>
internal sealed class OtlpPayloadTooLargeException(string? message, Exception? innerException,
    int httpStatus = StatusCodes.Status413PayloadTooLarge)
    : IOException(message, innerException)
{
    /// <summary>
    ///     The HTTP status of a request of too many messages or records, in this one place. Over gRPC the answer
    ///     is RESOURCE_EXHAUSTED without RetryInfo.
    /// </summary>
    internal const int TooManyMessagesHttpStatus = StatusCodes.Status413PayloadTooLarge;

    /// <summary>The HTTP status the refusal is answered with.</summary>
    public int HttpStatus { get; } = httpStatus;

    /// <summary>The body holds more bytes than a request may hold on the wire.</summary>
    public static OtlpPayloadTooLargeException OnTheWire(int limit) =>
        new(string.Create(CultureInfo.InvariantCulture, $"OTLP payload exceeds the {limit}-byte wire limit."), null);

    /// <summary>The request holds more messages than a request may hold.</summary>
    public static OtlpPayloadTooLargeException TooManyMessages(int limit) =>
        new(string.Create(CultureInfo.InvariantCulture, $"OTLP request holds more than {limit} messages."), null,
            TooManyMessagesHttpStatus);

    /// <summary>The request holds more records (spans, log records, data points, exemplars) than a request may hold.</summary>
    public static OtlpPayloadTooLargeException TooManyRecords(int limit) =>
        new(string.Create(CultureInfo.InvariantCulture, $"OTLP request holds more than {limit} records."), null,
            TooManyMessagesHttpStatus);

    /// <summary>The body holds, once decompressed, more bytes than a request may hold.</summary>
    public static OtlpPayloadTooLargeException Decompressed(int limit) =>
        new(string.Create(CultureInfo.InvariantCulture, $"OTLP payload exceeds the {limit}-byte decompressed limit."),
            null);
}
