using System.Buffers;
using System.Net;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Nami.Collector.Ingestion;
using Nami.Tests.Infrastructure;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Nami.IntegrationTests;

/// <summary>
///     E4f. What the pre-scan counts has to be what the parser builds. Evaluator 14 found requests where it was
///     not: after an unknown group or a tag written longer than it needs to be the scan stopped counting and
///     Google.Protobuf read on; unknown fields were kept as objects; packed scalars and JSON scalars were counted
///     by no bound. Each is refused now or costs a few copies of its bytes. The cost is counted in allocated bytes
///     on the test's own thread, over the OTLP/HTTP path and the OTLP/gRPC path of the parser.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class PreScanBypassTests(TestContext context)
{
    private const int Hidden = 100_000;
    private static readonly IngestLimits s_limits = new();

    // 100,000 empty ResourceSpans, twice the message bound: refused when they are counted.
    private static byte[] EmptyResourceSpans => Repeat([0x0A, 0x00], Hidden);

    [TestMethod]
    [DataRow("group", false, DisplayName = "an unknown empty group first, HTTP")]
    [DataRow("group", true, DisplayName = "an unknown empty group first, gRPC")]
    [DataRow("five-byte tag", false, DisplayName = "every tag in five bytes with bit 32 set, HTTP")]
    [DataRow("five-byte tag", true, DisplayName = "every tag in five bytes with bit 32 set, gRPC")]
    [DataRow("six-byte tag", false, DisplayName = "a six-byte tag first, HTTP")]
    [DataRow("six-byte tag", true, DisplayName = "a six-byte tag first, gRPC")]
    public async Task MessagesBehindBytesNoExporterWrites_AreNotParsed(string shape, bool grpc)
    {
        byte[] payload = shape switch
        {
            // Field 15 as a group: start (wire type 3), end (wire type 4).
            "group" => [0x7B, 0x7C, .. EmptyResourceSpans],
            // 0x0A with four continuation bytes, the last carrying bit 32, which a 32-bit tag drops.
            "five-byte tag" => Repeat([0x8A, 0x80, 0x80, 0x80, 0x10, 0x00], Hidden),
            _ => [0x8A, 0x80, 0x80, 0x80, 0x80, 0x01, 0x00, .. EmptyResourceSpans]
        };

        // Once, unmeasured: the first parse of a process builds the generated protobuf descriptors (some 316 KB),
        // which is no cost of this request (ledger N90: 3 of 150 runs failed when it fell on a measured row).
        await Assert.ThrowsExactlyAsync<OtlpMalformedPayloadException>(() => ParseTracesAsync(payload, grpc));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var refused = await Assert.ThrowsExactlyAsync<OtlpMalformedPayloadException>(() => ParseTracesAsync(payload, grpc));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Contains("not protobuf as an OTLP exporter writes it", refused.Message);
        Assert.IsLessThan(3L * payload.Length + 65536, allocated, "Refused from the bytes: no object per hidden message.");
    }

    [TestMethod]
    [DataRow("length-delimited", false, DisplayName = "unknown one-byte fields, HTTP")]
    [DataRow("length-delimited", true, DisplayName = "unknown one-byte fields, gRPC")]
    [DataRow("varint", false, DisplayName = "unknown varint fields, HTTP")]
    [DataRow("varint", true, DisplayName = "unknown varint fields, gRPC")]
    public async Task FieldsOtlpDoesNotDefine_AreReadPastAndNotKept(string shape, bool grpc)
    {
        // Field 2 of the request, which only defines field 1: 1 MiB of them.
        var payload = shape == "varint" ? Repeat([0x10, 0x01], 524_288) : Repeat([0x12, 0x01, 0x00], 349_525);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var parsed = await ParseTracesAsync(payload, grpc);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(0, parsed.CalculateSize(), "Nothing of an unknown field is kept.");
        Assert.IsLessThan(3L * payload.Length + 65536, allocated, "A few copies of the bytes, not an object per field.");
    }

    /// <summary>
    ///     One exponential histogram point whose packed bucket counts are one byte each on the wire and eight bytes
    ///     each once parsed: inside the message and record bounds, bounded by the element count.
    /// </summary>
    [TestMethod]
    [DataRow(false, DisplayName = "HTTP")]
    [DataRow(true, DisplayName = "gRPC")]
    public async Task PackedScalars_AreCountedAsElements_AndRefusedOverTheBound(bool grpc)
    {
        // The name of the metric is the one element besides the counts.
        var atTheBound = HistogramPoint(s_limits.MaxRequestElements - 1);
        var parsed = await ParseMetricsAsync(atTheBound, grpc);
        Assert.HasCount(s_limits.MaxRequestElements - 1, parsed.ResourceMetrics[0].ScopeMetrics[0].Metrics[0]
            .ExponentialHistogram.DataPoints[0].Positive.BucketCounts);

        var over = HistogramPoint(2 * s_limits.MaxRequestElements);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var refused = await Assert.ThrowsExactlyAsync<OtlpPayloadTooLargeException>(() => ParseMetricsAsync(over, grpc));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual("OTLP request holds more than 1000000 scalar elements.", refused.Message);
        Assert.IsLessThan(3L * over.Length + 65536, allocated, "Refused from the bytes: no array of the counts.");
    }

    /// <summary>The JSON reader builds a token for every scalar, also in a member OTLP does not define.</summary>
    [TestMethod]
    public async Task Json_AnUnknownMemberOfMoreScalarsThanTheBound_IsRefused()
    {
        var json = Encoding.UTF8.GetBytes("{\"unknown\":[" +
                                          string.Join(',', Enumerable.Repeat("1", s_limits.MaxRequestElements + 1)) + "]}");

        var before = GC.GetAllocatedBytesForCurrentThread();
        var refused = await Assert.ThrowsExactlyAsync<OtlpPayloadTooLargeException>(() => ParseJsonAsync(json));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual("OTLP request holds more than 1000000 scalar elements.", refused.Message);
        Assert.IsLessThan(3L * json.Length + 65536, allocated, "Refused from the bytes: no token per number.");
    }

    /// <summary>
    ///     One string attribute of 4 MiB over JSON: a string is one object and is left to the converter to cut, so
    ///     what counts is how many copies of it the JSON path makes on the way. It made fifteen; and of a character
    ///     the default JSON encoder escapes (every one outside ASCII, and &lt; &gt; &amp; ' +) six bytes for each.
    /// </summary>
    [TestMethod]
    [DataRow("s", DisplayName = "ASCII letters")]
    [DataRow("<", DisplayName = "a character HTML escapes")]
    [DataRow("\u00e4", DisplayName = "two-byte characters")]
    [DataRow("\ud83c\udf0a", DisplayName = "four-byte characters")]
    public async Task Json_OneLargeStringAttribute_CostsAFewCopiesOfItself(string character)
    {
        var text = string.Concat(Enumerable.Repeat(character, 4 * 1024 * 1024 / Encoding.UTF8.GetByteCount(character)));
        var json = Encoding.UTF8.GetBytes(
            "{\"resourceSpans\":[{\"scopeSpans\":[{\"spans\":[{\"name\":\"span\",\"attributes\":[{\"key\":\"k\",\"value\":{\"stringValue\":\"" +
            text + "\"}}]}]}]}]}");

        var before = GC.GetAllocatedBytesForCurrentThread();
        var parsed = await ParseJsonAsync(json);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(text, parsed.ResourceSpans[0].ScopeSpans[0].Spans[0].Attributes[0].Value.StringValue);
        Assert.IsLessThan(11L * json.Length, allocated);
    }

    /// <summary>
    ///     A member name is a string to the JSON reader like any other: a million and one members of an object
    ///     OTLP does not define, each an empty array, hold nothing else that is counted.
    /// </summary>
    [TestMethod]
    public async Task Json_MoreMemberNamesThanTheBound_AreRefused()
    {
        var json = Encoding.UTF8.GetBytes("{\"unknown\":{" +
                                          string.Join(',', Enumerable.Repeat("\"a\":[]", s_limits.MaxRequestElements)) + "}}");

        var before = GC.GetAllocatedBytesForCurrentThread();
        var refused = await Assert.ThrowsExactlyAsync<OtlpPayloadTooLargeException>(() => ParseJsonAsync(json));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual("OTLP request holds more than 1000000 scalar elements.", refused.Message);
        Assert.IsLessThan(3L * json.Length + 65536, allocated, "Refused from the bytes: no string per name.");
    }

    /// <summary>
    ///     A field that is not repeated may still be sent any number of times: the parser builds a string for each
    ///     occurrence and keeps the last. One span whose name is sent a million and one times, and one attribute
    ///     value whose string is.
    /// </summary>
    [TestMethod]
    [DataRow("span name", false, DisplayName = "a span name, HTTP")]
    [DataRow("span name", true, DisplayName = "a span name, gRPC")]
    [DataRow("string value", false, DisplayName = "the string of an attribute value, HTTP")]
    [DataRow("string value", true, DisplayName = "the string of an attribute value, gRPC")]
    [DataRow("nested key", false, DisplayName = "the key of a member in a key-value list, HTTP")]
    [DataRow("nested key", true, DisplayName = "the key of a member in a key-value list, gRPC")]
    [DataRow("nested string", false, DisplayName = "the string of a value in an array, HTTP")]
    [DataRow("nested string", true, DisplayName = "the string of a value in an array, gRPC")]
    [DataRow("nested bytes", false, DisplayName = "the bytes of a value in an array, HTTP")]
    [DataRow("nested bytes", true, DisplayName = "the bytes of a value in an array, gRPC")]
    public async Task AStringFieldSentMoreOftenThanTheBound_IsRefused(string shape, bool grpc)
    {
        // Span.name = 5; KeyValue { key = 1, value = 2 }; AnyValue { string_value = 1, array_value = 5 { values = 1 },
        // kvlist_value = 6 { values = 1 }, bytes_value = 7 }.
        // A structure over the cap of one value is replaced unread, so the keys come in twenty values under it:
        // each a key-value list of one member whose key is sent 60,000 times. The same below the top level of a
        // value (evaluator 15, X3c): twenty arrays of one value whose string, or whose bytes, is sent 60,000 times.
        var span = shape switch
        {
            "span name" => Repeat([0x2A, 0x01, (byte)'n'], s_limits.MaxRequestElements + 1),
            "string value" => Field(9, Field(2, Repeat([0x0A, 0x01, (byte)'n'], s_limits.MaxRequestElements + 1))),
            "nested string" => Repeat(Field(9, Field(2, Field(5, Field(1, Repeat([0x0A, 0x01, (byte)'n'], 60_000))))), 20),
            "nested bytes" => Repeat(Field(9, Field(2, Field(5, Field(1, Repeat([0x3A, 0x01, (byte)'n'], 60_000))))), 20),
            _ => Repeat(Field(9, Field(2, Field(6, Field(1, Repeat([0x0A, 0x01, (byte)'n'], 60_000))))), 20)
        };
        // Export { resource_spans = 1 { scope_spans = 2 { spans = 2 } } }.
        var payload = Field(1, Field(2, Field(2, span)));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var refused = await Assert.ThrowsExactlyAsync<OtlpPayloadTooLargeException>(() => ParseTracesAsync(payload, grpc));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual("OTLP request holds more than 1000000 scalar elements.", refused.Message);
        Assert.IsLessThan(3L * payload.Length + 65536, allocated, "Refused from the bytes: no string per occurrence.");
    }

    /// <summary>
    ///     A structure over the value cap is replaced by the sentinel and never built, so the strings the scan
    ///     counted inside it before it knew are given back: they are no elements of the request. Here two arrays,
    ///     each over the cap by its count of values (250, the cap allows 204) and each with 150 strings in front,
    ///     in a request that may hold 200 elements. Counted without the give-back the first array leaves 50 and
    ///     the second is refused.
    /// </summary>
    [TestMethod]
    [DataRow(false, DisplayName = "OTLP/HTTP")]
    [DataRow(true, DisplayName = "OTLP/gRPC")]
    public async Task AStructureReplacedByTheSentinel_GivesBackTheElementsCountedInsideIt(bool grpc)
    {
        var limits = new IngestLimits { MaxValueUtf8Bytes = 1024, MaxRequestElements = 200 };
        Assert.AreEqual(204, OtlpValueBounds.MaxNestedValues(limits));
        // ArrayValue.values = 1: 150 values that hold a string of one byte, then 100 empty ones; 950 bytes.
        byte[] items = [.. Repeat([0x0A, 0x03, 0x0A, 0x01, (byte)'n'], 150), .. Repeat([0x0A, 0x00], 100)];
        var attribute = Field(9, [.. Field(1, "k"u8.ToArray()), .. Field(2, Field(5, items))]);
        Assert.IsLessThan(limits.MaxValueUtf8Bytes, attribute.Length, "Over the cap by its count, not by its bytes.");
        var payload = Field(1, Field(2, Field(2, [.. attribute, .. attribute])));

        var parsed = grpc
            ? OtlpPayloadParser.ParseProtobuf(ExportTraceServiceRequest.Parser, ExportTraceServiceRequest.Descriptor,
                new ReadOnlySequence<byte>(payload), limits)
            : await OtlpPayloadParser.ParseTraceRequestAsync(Request(payload), OtlpPayloadEncoding.Protobuf, limits,
                context.CancellationToken);

        var attributes = parsed.ResourceSpans[0].ScopeSpans[0].Spans[0].Attributes;
        Assert.HasCount(2, attributes);
        foreach (var replaced in attributes)
            Assert.AreEqual(OtlpValueBounds.OversizedValueSentinel, replaced.Value.StringValue);
    }

    /// <summary>
    ///     The answers of the server, not of the parser: protobuf no exporter writes is the client's error and is
    ///     not sent again (400, INVALID_ARGUMENT); a request over the element bound is too large (413,
    ///     RESOURCE_EXHAUSTED).
    /// </summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow("group", false, DisplayName = "a group, OTLP/HTTP")]
    [DataRow("group", true, DisplayName = "a group, OTLP/gRPC")]
    [DataRow("six-byte tag", false, DisplayName = "a six-byte tag, OTLP/HTTP")]
    [DataRow("six-byte tag", true, DisplayName = "a six-byte tag, OTLP/gRPC")]
    [DataRow("elements", false, DisplayName = "over the element bound, OTLP/HTTP")]
    [DataRow("elements", true, DisplayName = "over the element bound, OTLP/gRPC")]
    public async Task TheServerAnswers_MalformedProtobufAsTheClientsError_AndTooManyElementsAsTooLarge(string shape, bool grpc)
    {
        var directory = context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");
        await using var host = await CollectorHost.StartAsync(
            Path.Combine(directory, "answers-" + shape.Replace(' ', '-') + (grpc ? "-grpc" : "-http") + ".duckdb"),
            context.CancellationToken);
        var signal = shape == "elements" ? Signals.Metrics : Signals.Traces;
        byte[] payload = shape switch
        {
            "group" => [0x7B, 0x7C, .. EmptyResourceSpans],
            "six-byte tag" => [0x8A, 0x80, 0x80, 0x80, 0x80, 0x01, 0x00, .. EmptyResourceSpans],
            _ => HistogramPoint(2 * s_limits.MaxRequestElements)
        };
        var tooLarge = shape == "elements";
        const string message = "OTLP request holds more than 1000000 scalar elements.";

        if (grpc)
        {
            var refused = await Assert.ThrowsExactlyAsync<RpcException>(() =>
                host.ExportGrpcAsync(signal, payload, context.CancellationToken));
            Assert.AreEqual(tooLarge ? StatusCode.ResourceExhausted : StatusCode.InvalidArgument,
                refused.StatusCode, refused.Status.Detail);
            if (tooLarge) Assert.AreEqual(message, refused.Status.Detail);
            return;
        }

        using var response = await host.PostProtobufAsync(signal, payload, context.CancellationToken);
        Assert.AreEqual(tooLarge ? HttpStatusCode.RequestEntityTooLarge : HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.IsNull(response.Headers.RetryAfter, "Neither answer asks for the request to be sent again.");
        var error = await Assert.That.HasOtlpErrorAsync(response, context.CancellationToken, OtlpReceiver.ProtobufMediaType);
        if (tooLarge) Assert.AreEqual(message, error.Message);
    }

    /// <summary>
    ///     Bytes that are not UTF-8 inside a JSON string: each becomes U+FFFD, three bytes for one, on the way
    ///     through a string. The request is not refused for it and costs a bounded number of copies.
    /// </summary>
    [TestMethod]
    public async Task Json_OneLargeStringOfInvalidUtf8_CostsABoundedNumberOfCopies()
    {
        const int Invalid = 4 * 1024 * 1024;
        var head = "{\"resourceSpans\":[{\"scopeSpans\":[{\"spans\":[{\"name\":\"span\",\"attributes\":[{\"key\":\"k\",\"value\":{\"stringValue\":\""u8;
        var tail = "\"}}]}]}]}]}"u8;
        var json = new byte[head.Length + Invalid + tail.Length];
        head.CopyTo(json);
        json.AsSpan(head.Length, Invalid).Fill(0xFF);
        tail.CopyTo(json.AsSpan(head.Length + Invalid));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var parsed = await ParseJsonAsync(json);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(new string('\uFFFD', Invalid), parsed.ResourceSpans[0].ScopeSpans[0].Spans[0].Attributes[0].Value.StringValue);
        Assert.IsLessThan(15L * json.Length / 2, allocated, "Read (3), the parser's builder and string (2 each); normalized while read.");
    }

    private Task<ExportTraceServiceRequest> ParseTracesAsync(byte[] payload, bool grpc) => grpc
        ? OnThisThread(() => OtlpPayloadParser.ParseProtobuf(ExportTraceServiceRequest.Parser,
            ExportTraceServiceRequest.Descriptor, new ReadOnlySequence<byte>(payload), s_limits))
        : OtlpPayloadParser.ParseTraceRequestAsync(Request(payload), OtlpPayloadEncoding.Protobuf, s_limits,
            context.CancellationToken);

    private Task<ExportMetricsServiceRequest> ParseMetricsAsync(byte[] payload, bool grpc) => grpc
        ? OnThisThread(() => OtlpPayloadParser.ParseProtobuf(ExportMetricsServiceRequest.Parser,
            ExportMetricsServiceRequest.Descriptor, new ReadOnlySequence<byte>(payload), s_limits))
        : OtlpPayloadParser.ParseMetricsRequestAsync(Request(payload), OtlpPayloadEncoding.Protobuf, s_limits,
            context.CancellationToken);

    private Task<ExportTraceServiceRequest> ParseJsonAsync(byte[] json) =>
        OtlpPayloadParser.ParseTraceRequestAsync(Request(json), OtlpPayloadEncoding.Json, s_limits, context.CancellationToken);

    // The gRPC path of the parser is synchronous; run where the test counts its allocations.
    private static Task<T> OnThisThread<T>(Func<T> parse)
    {
        var task = new Task<T>(parse);
        task.RunSynchronously();
        return task;
    }

    private static HttpRequest Request(byte[] body)
    {
        var request = new DefaultHttpContext().Request;
        request.Body = new MemoryStream(body, false);
        request.ContentLength = body.Length;
        return request;
    }

    private static byte[] Repeat(byte[] unit, int count)
    {
        var bytes = new byte[unit.Length * count];
        for (var i = 0; i < count; i++) unit.CopyTo(bytes, i * unit.Length);
        return bytes;
    }

    // Export { resource_metrics = 1 { scope_metrics = 2 { metrics = 2 { name = 1, exponential_histogram = 10 {
    // data_points = 1 { positive = 8 { bucket_counts = 2, packed, one byte each } } } } } } }.
    private static byte[] HistogramPoint(int bucketCounts) =>
        Field(1, Field(2, Field(2, [.. Field(1, "m"u8.ToArray()), .. Field(10, Field(1, Field(8, Field(2, new byte[bucketCounts]))))])));

    private static byte[] Field(int number, byte[] content)
    {
        List<byte> field = [(byte)((number << 3) | 2)];
        for (var rest = (uint)content.Length;; rest >>= 7)
        {
            if (rest < 0x80)
            {
                field.Add((byte)rest);
                break;
            }

            field.Add((byte)(rest | 0x80));
        }

        field.AddRange(content);
        return [.. field];
    }
}
