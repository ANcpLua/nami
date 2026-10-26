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
    public OtlpUnsupportedMediaTypeException() : this(null, null) { }
    public OtlpUnsupportedMediaTypeException(string? contentType) : this(contentType, null) { }
}

internal sealed class OtlpUnsupportedContentEncodingException(string? contentEncoding, Exception? innerException)
    : NotSupportedException($"Unsupported OTLP Content-Encoding '{contentEncoding ?? "<missing>"}'.", innerException)
{
    public OtlpUnsupportedContentEncodingException() : this(null, null) { }
    public OtlpUnsupportedContentEncodingException(string? contentEncoding) : this(contentEncoding, null) { }
}

/// <summary>
///     The OTLP/HTTP body exceeded the decompressed size bound. OTLP 1.11 (request size limits)
///     requires 413 Content Too Large, which the endpoint handlers answer for this exception.
/// </summary>
internal sealed class OtlpPayloadTooLargeException(string? message, Exception? innerException)
    : IOException(message, innerException)
{
    public OtlpPayloadTooLargeException() : this(OtlpPayloadParser.PayloadTooLargeMessage, null) { }
    public OtlpPayloadTooLargeException(string? message) : this(message, null) { }
}
