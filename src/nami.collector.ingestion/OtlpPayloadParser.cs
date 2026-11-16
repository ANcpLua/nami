using System.IO.Compression;
using System.Net.Http.Headers;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Nami.Collector.Ingestion;

internal static class OtlpPayloadParser
{
    internal const string ProtobufContentType = "application/x-protobuf";
    internal const string JsonContentType = "application/json";

    // What a body is read into before it has grown; the buffer then follows the bytes that arrive.
    private const int ReadChunkBytes = 64 * 1024;

    private static readonly JsonParser s_otlpJsonParser =
        new(JsonParser.Settings.Default.WithIgnoreUnknownFields(true));

    public static OtlpPayloadEncoding GetEncoding(string? contentType)
    {
        return TryGetEncoding(contentType, out var encoding)
            ? encoding
            : throw new OtlpUnsupportedMediaTypeException(contentType);
    }

    /// <summary>The encoding a Content-Type names; false for a missing one and for one OTLP does not define.</summary>
    public static bool TryGetEncoding(string? contentType, out OtlpPayloadEncoding encoding)
    {
        encoding = OtlpPayloadEncoding.Json;
        if (!MediaTypeHeaderValue.TryParse(contentType, out var parsed) || parsed.MediaType is null)
            return false;

        if (string.Equals(parsed.MediaType, ProtobufContentType, StringComparison.OrdinalIgnoreCase))
        {
            encoding = OtlpPayloadEncoding.Protobuf;
            return true;
        }

        return string.Equals(parsed.MediaType, JsonContentType, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Reads and decodes one OTLP/HTTP export. The body is held to <paramref name="limits" />: more than
    ///     <see cref="IngestLimits.MaxRequestWireBytes" /> on the wire or
    ///     <see cref="IngestLimits.MaxRequestDecompressedBytes" /> decompressed is an
    ///     <see cref="OtlpPayloadTooLargeException" />, thrown as soon as the excess is known.
    /// </summary>
    public static Task<ExportTraceServiceRequest> ParseTraceRequestAsync(
        HttpRequest request,
        OtlpPayloadEncoding encoding,
        IngestLimits limits,
        CancellationToken ct = default) =>
        encoding is OtlpPayloadEncoding.Protobuf
            ? ParseProtobufAsync(request, ExportTraceServiceRequest.Parser, ExportTraceServiceRequest.Descriptor,
                limits, ct)
            : ParseJsonAsync<ExportTraceServiceRequest>(request, limits, ct);

    /// <inheritdoc cref="ParseTraceRequestAsync(HttpRequest, OtlpPayloadEncoding, IngestLimits, CancellationToken)" />
    public static Task<ExportLogsServiceRequest> ParseLogsRequestAsync(
        HttpRequest request,
        OtlpPayloadEncoding encoding,
        IngestLimits limits,
        CancellationToken ct = default) =>
        encoding is OtlpPayloadEncoding.Protobuf
            ? ParseProtobufAsync(request, ExportLogsServiceRequest.Parser, ExportLogsServiceRequest.Descriptor,
                limits, ct)
            : ParseJsonAsync<ExportLogsServiceRequest>(request, limits, ct);

    /// <inheritdoc cref="ParseTraceRequestAsync(HttpRequest, OtlpPayloadEncoding, IngestLimits, CancellationToken)" />
    public static Task<ExportMetricsServiceRequest> ParseMetricsRequestAsync(
        HttpRequest request,
        OtlpPayloadEncoding encoding,
        IngestLimits limits,
        CancellationToken ct = default) =>
        encoding is OtlpPayloadEncoding.Protobuf
            ? ParseProtobufAsync(request, ExportMetricsServiceRequest.Parser, ExportMetricsServiceRequest.Descriptor,
                limits, ct)
            : ParseJsonAsync<ExportMetricsServiceRequest>(request, limits, ct);

    /// <summary>The same with the default bounds, for a caller that has no configuration.</summary>
    public static Task<ExportTraceServiceRequest> ParseTraceRequestAsync(
        HttpRequest request,
        OtlpPayloadEncoding encoding,
        CancellationToken ct = default) =>
        ParseTraceRequestAsync(request, encoding, IngestLimits.Default, ct);

    /// <inheritdoc cref="ParseTraceRequestAsync(HttpRequest, OtlpPayloadEncoding, CancellationToken)" />
    public static Task<ExportLogsServiceRequest> ParseLogsRequestAsync(
        HttpRequest request,
        OtlpPayloadEncoding encoding,
        CancellationToken ct = default) =>
        ParseLogsRequestAsync(request, encoding, IngestLimits.Default, ct);

    /// <inheritdoc cref="ParseTraceRequestAsync(HttpRequest, OtlpPayloadEncoding, CancellationToken)" />
    public static Task<ExportMetricsServiceRequest> ParseMetricsRequestAsync(
        HttpRequest request,
        OtlpPayloadEncoding encoding,
        CancellationToken ct = default) =>
        ParseMetricsRequestAsync(request, encoding, IngestLimits.Default, ct);

    /// <summary>
    ///     The value bounds first, on the bytes (<see cref="OtlpValueBounds" />), so that a structure over the cap
    ///     is never built, and the counts of messages and records, so that a request of too many is refused with
    ///     an <see cref="OtlpPayloadTooLargeException" /> before it is parsed. Then the strict parse; when it fails and the payload holds an invalid UTF-8 sequence in
    ///     a <c>string</c> field, the sequence is replaced by U+FFFD (OTLP 1.11.1) and the payload is parsed again.
    ///     A failure that no replacement explains stands.
    /// </summary>
    private static T ParseProtobuf<T>(MessageParser<T> parser, MessageDescriptor descriptor, Span<byte> payload,
        IngestLimits limits)
        where T : IMessage<T>
    {
        var bounded = OtlpValueBounds.BoundProtobuf(payload, descriptor, limits, out var counted);
        RefuseTooMany(counted, limits);
        CountReplaced(bounded);
        // Unknown fields are not kept: Google.Protobuf would hold every one of them as an object, and the bounds
        // count what nami reads. The converter reads known fields only.
        var known = parser.WithDiscardUnknownFields(true);
        try
        {
            return known.ParseFrom(payload);
        }
        catch (InvalidProtocolBufferException) when (OtlpUtf8Replacement.TryReplaceInvalidUtf8(payload, descriptor,
                                                         out var replaced))
        {
            return known.ParseFrom(replaced);
        }
    }

    /// <summary>
    ///     A gRPC message. Its bytes are copied once: the value bounds are applied in place, and the buffers a
    ///     message arrives in are the server's.
    /// </summary>
    internal static T ParseProtobuf<T>(MessageParser<T> parser, MessageDescriptor descriptor,
        in ReadOnlySequence<byte> payload, IngestLimits limits)
        where T : IMessage<T> =>
        ParseProtobuf(parser, descriptor, payload.ToArray().AsSpan(), limits);

    /// <summary>The same with the default bounds, for a caller that has no configuration.</summary>
    internal static T ParseProtobuf<T>(MessageParser<T> parser, MessageDescriptor descriptor,
        in ReadOnlySequence<byte> payload)
        where T : IMessage<T> =>
        ParseProtobuf(parser, descriptor, payload, IngestLimits.Default);

    private static async Task<T> ParseProtobufAsync<T>(
        HttpRequest request,
        MessageParser<T> parser,
        MessageDescriptor descriptor,
        IngestLimits limits,
        CancellationToken ct)
        where T : IMessage<T>
    {
        await using var payload = await ReadBoundedPayloadAsync(request, limits, ct).ConfigureAwait(false);
        return ParseProtobuf(parser, descriptor, payload.GetBuffer().AsSpan(0, (int)payload.Length), limits);
    }

    private static async Task<T> ParseJsonAsync<T>(HttpRequest request, IngestLimits limits, CancellationToken ct)
        where T : IMessage<T>, new()
    {
        await using var payload = await ReadBoundedPayloadAsync(request, limits, ct).ConfigureAwait(false);

        // The value bounds first, on the bytes, so that a structure over the cap is never built.
        var utf8Json = payload.GetBuffer().AsSpan(0, (int)payload.Length);
        var bounded = OtlpValueBounds.BoundJson(utf8Json, limits, out var counted);
        RefuseTooMany(counted, limits);
        CountReplaced(bounded);

        // The OTLP spec mandates hex for trace and span ids in JSON, but protojson decodes
        // bytes fields as base64 — rewrite the id fields (validating them) before parsing.
        // Read from the normalized bytes: one more copy of a 16 MiB body as a string is 32 MiB.
        var json = OtlpJsonIdNormalizer.NormalizeIdsToProtoJsonUtf8(utf8Json);
        using var reader = new StreamReader(new MemoryStream(json.Array!, json.Offset, json.Count, false), Encoding.UTF8,
            false);
        return s_otlpJsonParser.Parse<T>(reader);
    }

    // The parser builds one object per message and the store one row per record, so a request of more than either
    // bound is refused before it is parsed, whatever its size in bytes.
    private static void RefuseTooMany(OtlpRequestCounts counted, IngestLimits limits)
    {
        if (counted.Messages > limits.MaxRequestMessages)
            throw OtlpPayloadTooLargeException.TooManyMessages(limits.MaxRequestMessages);
        if (counted.Records > limits.MaxRequestRecords)
            throw OtlpPayloadTooLargeException.TooManyRecords(limits.MaxRequestRecords);
    }

    // A value nested deeper than the bound was replaced by an empty value before the parse. Its attribute stays, so
    // this is no dropped attribute: it is counted in nami's own telemetry, with the reason "depth". A refused request
    // counts nothing. The converter counts the same way where it empties such a value itself; it cannot see the ones
    // replaced here.
    private static void CountReplaced(OtlpValueBoundsResult bounded)
    {
        if (bounded.TooDeep > 0) NamiCollectorMetrics.ValueReplaced(bounded.TooDeep, "depth");
    }

    /// <summary>
    ///     The request body, decompressed, in one buffer. Three things keep it bounded. A declared Content-Length
    ///     over the wire bound is refused before a byte is read. The bytes that arrive are counted, on the wire and
    ///     after decompression, and the read stops with the first byte over either bound, so a body without a
    ///     Content-Length and a small gzip body that inflates without end cost no more than the bound. And the
    ///     buffer grows with the bytes that have arrived, never to a length the sender only declared (engine audit
    ///     A8: 24 idle connections that each declared 16 MiB took the memory valid exports needed).
    /// </summary>
    private static async Task<MemoryStream> ReadBoundedPayloadAsync(HttpRequest request, IngestLimits limits,
        CancellationToken ct)
    {
        var contentEncoding = request.Headers.ContentEncoding.ToString().Trim();
        var isIdentity = contentEncoding.Length is 0 ||
                         string.Equals(contentEncoding, "identity", StringComparison.OrdinalIgnoreCase);
        if (!isIdentity && !string.Equals(contentEncoding, "gzip", StringComparison.OrdinalIgnoreCase))
            throw new OtlpUnsupportedContentEncodingException(contentEncoding);

        var wireLimit = limits.MaxRequestWireBytes;
        var decompressedLimit = limits.MaxRequestDecompressedBytes;
        if (request.ContentLength > wireLimit)
            throw OtlpPayloadTooLargeException.OnTheWire(wireLimit);

        if (isIdentity)
        {
            // An uncompressed body is its own decompressed form, so it is held to the smaller bound.
            if (request.ContentLength > decompressedLimit)
                throw OtlpPayloadTooLargeException.Decompressed(decompressedLimit);
            return await ReadBoundedStreamAsync(request.Body, Math.Min(wireLimit, decompressedLimit),
                request.ContentLength, wireLimit <= decompressedLimit, ct).ConfigureAwait(false);
        }

        await using var wire = new OtlpWireLimitStream(request.Body, wireLimit);
        await using var gzip = new GZipStream(wire, CompressionMode.Decompress, true);
        return await ReadBoundedStreamAsync(gzip, decompressedLimit, null, false, ct).ConfigureAwait(false);
    }

    private static async Task<MemoryStream> ReadBoundedStreamAsync(
        Stream source,
        int limit,
        long? declaredLength,
        bool limitIsOnTheWire,
        CancellationToken ct)
    {
        var payload = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(ReadChunkBytes);
        // The buffer never needs more than the bound, and no more than the sender declared.
        var ceiling = (int)Math.Min(limit, declaredLength ?? limit);
        long totalBytes = 0;

        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                if (read is 0)
                    break;

                totalBytes += read;
                if (totalBytes > limit)
                    throw TooLarge();

                // Grow by doubling, as far as the bytes that have arrived require and no further than the ceiling.
                if (totalBytes > payload.Capacity)
                {
                    payload.Capacity = (int)Math.Max(totalBytes,
                        Math.Min(Math.Max(payload.Capacity * 2L, ReadChunkBytes), ceiling));
                }

                payload.Write(buffer, 0, read);
            }

            payload.Position = 0;
            return payload;
        }
        catch (BadHttpRequestException refused) when (refused.StatusCode is StatusCodes.Status413PayloadTooLarge)
        {
            // Kestrel's own request-body bound, where it sits at or below nami's: the same refusal.
            await payload.DisposeAsync().ConfigureAwait(false);
            throw new OtlpPayloadTooLargeException(TooLarge().Message, refused);
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

        OtlpPayloadTooLargeException TooLarge() => limitIsOnTheWire
            ? OtlpPayloadTooLargeException.OnTheWire(limit)
            : OtlpPayloadTooLargeException.Decompressed(limit);
    }
}
