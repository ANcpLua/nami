using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Nami.Collector.Ingestion;

internal static class OtlpPayloadParser
{
    private const int MaxPayloadBytes = 16 * 1024 * 1024;
    internal const string ProtobufContentType = "application/x-protobuf";
    internal const string JsonContentType = "application/json";

    /// <summary>
    ///     The OTLP status message for a body over <see cref="MaxPayloadBytes" />; OTLP 1.11 (request
    ///     size limits) makes that a 413 Content Too Large, which the endpoint handlers answer.
    /// </summary>
    internal const string PayloadTooLargeMessage = "OTLP payload exceeds the 16 MiB wire or decompressed limit.";

    private static readonly JsonParser s_otlpJsonParser =
        new(JsonParser.Settings.Default.WithIgnoreUnknownFields(true));

    public static OtlpPayloadEncoding GetEncoding(string? contentType)
    {
        if (!MediaTypeHeaderValue.TryParse(contentType, out var parsed) || parsed.MediaType is null)
            throw new OtlpUnsupportedMediaTypeException(contentType);

        if (string.Equals(parsed.MediaType, ProtobufContentType, StringComparison.OrdinalIgnoreCase))
            return OtlpPayloadEncoding.Protobuf;

        if (string.Equals(parsed.MediaType, JsonContentType, StringComparison.OrdinalIgnoreCase))
            return OtlpPayloadEncoding.Json;

        throw new OtlpUnsupportedMediaTypeException(contentType);
    }

    public static Task<ExportTraceServiceRequest> ParseTraceRequestAsync(
        HttpRequest request,
        OtlpPayloadEncoding encoding,
        CancellationToken ct = default) =>
        encoding is OtlpPayloadEncoding.Protobuf
            ? ParseProtobufAsync(request, ExportTraceServiceRequest.Parser, ExportTraceServiceRequest.Descriptor, ct)
            : ParseJsonAsync<ExportTraceServiceRequest>(request, ct);

    public static Task<ExportLogsServiceRequest> ParseLogsRequestAsync(
        HttpRequest request,
        OtlpPayloadEncoding encoding,
        CancellationToken ct = default) =>
        encoding is OtlpPayloadEncoding.Protobuf
            ? ParseProtobufAsync(request, ExportLogsServiceRequest.Parser, ExportLogsServiceRequest.Descriptor, ct)
            : ParseJsonAsync<ExportLogsServiceRequest>(request, ct);

    public static Task<ExportMetricsServiceRequest> ParseMetricsRequestAsync(
        HttpRequest request,
        OtlpPayloadEncoding encoding,
        CancellationToken ct = default) =>
        encoding is OtlpPayloadEncoding.Protobuf
            ? ParseProtobufAsync(request, ExportMetricsServiceRequest.Parser, ExportMetricsServiceRequest.Descriptor,
                ct)
            : ParseJsonAsync<ExportMetricsServiceRequest>(request, ct);

    /// <summary>
    ///     The strict parse first; when it fails and the payload holds an invalid UTF-8 sequence in a
    ///     <c>string</c> field, the sequence is replaced by U+FFFD (OTLP 1.11.1) and the payload is
    ///     parsed again. A failure that no replacement explains stands.
    /// </summary>
    private static T ParseProtobuf<T>(MessageParser<T> parser, MessageDescriptor descriptor, ReadOnlySpan<byte> payload)
        where T : IMessage<T>
    {
        try
        {
            return parser.ParseFrom(payload);
        }
        catch (InvalidProtocolBufferException) when (OtlpUtf8Replacement.TryReplaceInvalidUtf8(payload, descriptor,
                                                         out var replaced))
        {
            return parser.ParseFrom(replaced);
        }
    }

    /// <inheritdoc cref="ParseProtobuf{T}(MessageParser{T}, MessageDescriptor, ReadOnlySpan{byte})" />
    internal static T ParseProtobuf<T>(MessageParser<T> parser, MessageDescriptor descriptor,
        in ReadOnlySequence<byte> payload)
        where T : IMessage<T>
    {
        try
        {
            return parser.ParseFrom(payload);
        }
        catch (InvalidProtocolBufferException) when (OtlpUtf8Replacement.TryReplaceInvalidUtf8(payload.ToArray(),
                                                         descriptor, out var replaced))
        {
            return parser.ParseFrom(replaced);
        }
    }

    private static async Task<T> ParseProtobufAsync<T>(
        HttpRequest request,
        MessageParser<T> parser,
        MessageDescriptor descriptor,
        CancellationToken ct)
        where T : IMessage<T>
    {
        await using var payload = await ReadBoundedPayloadAsync(request, ct).ConfigureAwait(false);
        return ParseProtobuf(parser, descriptor, payload.GetBuffer().AsSpan(0, (int)payload.Length));
    }

    private static async Task<T> ParseJsonAsync<T>(HttpRequest request, CancellationToken ct)
        where T : IMessage<T>, new()
    {
        await using var payload = await ReadBoundedPayloadAsync(request, ct).ConfigureAwait(false);

        // The OTLP spec mandates hex for trace and span ids in JSON, but protojson decodes
        // bytes fields as base64 — rewrite the id fields (validating them) before parsing.
        var json = OtlpJsonIdNormalizer.NormalizeIdsToProtoJson(payload.GetBuffer().AsSpan(0, (int)payload.Length));
        return s_otlpJsonParser.Parse<T>(json);
    }

    private static async Task<MemoryStream> ReadBoundedPayloadAsync(HttpRequest request, CancellationToken ct)
    {
        request.EnableBuffering();
        var contentEncoding = request.Headers.ContentEncoding.ToString().Trim();
        var isIdentity = contentEncoding.Length is 0 ||
                         string.Equals(contentEncoding, "identity", StringComparison.OrdinalIgnoreCase);
        if (!isIdentity && !string.Equals(contentEncoding, "gzip", StringComparison.OrdinalIgnoreCase))
            throw new OtlpUnsupportedContentEncodingException(contentEncoding);

        if (request.ContentLength is > MaxPayloadBytes)
            throw new OtlpPayloadTooLargeException();

        try
        {
            if (isIdentity)
            {
                var capacity = request.ContentLength is > 0 and <= int.MaxValue
                    ? (int)request.ContentLength.Value
                    : 0;
                return await ReadBoundedStreamAsync(request.Body, capacity, ct).ConfigureAwait(false);
            }

            await using var wire = new OtlpWireLimitStream(request.Body, MaxPayloadBytes);
            await using var gzip = new GZipStream(wire, CompressionMode.Decompress, true);
            return await ReadBoundedStreamAsync(gzip, 0, ct).ConfigureAwait(false);
        }
        finally
        {
            if (request.Body.CanSeek)
                request.Body.Position = 0;
        }
    }

    private static async Task<MemoryStream> ReadBoundedStreamAsync(
        Stream source,
        int capacity,
        CancellationToken ct)
    {
        var payload = new MemoryStream(capacity);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        long totalBytes = 0;

        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                if (read is 0)
                    break;

                totalBytes += read;
                if (totalBytes > MaxPayloadBytes)
                    throw new OtlpPayloadTooLargeException();

                payload.Write(buffer, 0, read);
            }

            payload.Position = 0;
            return payload;
        }
        catch
        {
            await payload.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
