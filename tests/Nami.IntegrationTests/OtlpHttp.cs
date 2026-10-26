using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;

namespace Nami.IntegrationTests;

/// <summary>
///     Posts raw OTLP/HTTP protobuf payloads to a receiver route, optionally gzip-encoded or without a
///     Content-Length.
/// </summary>
internal static class OtlpHttp
{
    /// <summary>
    ///     Posts <paramref name="payload" /> as protobuf to the <paramref name="signal" /> route of
    ///     <paramref name="endpoint" />.
    /// </summary>
    internal static async Task<HttpResponseMessage> PostAsync(Uri endpoint, byte[] payload,
        CancellationToken cancellationToken,
        string signal = Signals.Traces, string? encoding = null)
    {
        using var content = Content(payload, encoding);
        return await TelemetryLifecycle.Http.PostAsync(new Uri(endpoint, "v1/" + signal), content, cancellationToken);
    }

    /// <summary>A protobuf body declaring <paramref name="encoding" />; a chunked body declares no Content-Length.</summary>
    internal static HttpContent Content(byte[] payload, string? encoding = null, bool chunked = false)
    {
        HttpContent content = chunked ? new UnknownLengthContent(payload) : new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue(OtlpReceiver.ProtobufMediaType);
        if (encoding is not null) content.Headers.ContentEncoding.Add(encoding);
        return content;
    }

    internal static byte[] Compress(byte[] payload)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, true)) gzip.Write(payload);
        return buffer.ToArray();
    }

    private sealed class UnknownLengthContent(byte[] payload) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return stream.WriteAsync(payload).AsTask();
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context,
            CancellationToken cancellationToken)
        {
            return stream.WriteAsync(payload, cancellationToken).AsTask();
        }
    }
}
