using System.Buffers;
using System.Text;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Nami.Collector.Ingestion;
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
        var atTheBound = HistogramPoint(s_limits.MaxRequestElements);
        var parsed = await ParseMetricsAsync(atTheBound, grpc);
        Assert.HasCount(s_limits.MaxRequestElements, parsed.ResourceMetrics[0].ScopeMetrics[0].Metrics[0]
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
    ///     what counts is how many copies of it the JSON path makes on the way. It made fifteen.
    /// </summary>
    [TestMethod]
    public async Task Json_OneLargeStringAttribute_CostsAFewCopiesOfItself()
    {
        var text = new string('s', 4 * 1024 * 1024);
        var json = Encoding.UTF8.GetBytes(
            "{\"resourceSpans\":[{\"scopeSpans\":[{\"spans\":[{\"name\":\"span\",\"attributes\":[{\"key\":\"k\",\"value\":{\"stringValue\":\"" +
            text + "\"}}]}]}]}]}");

        var before = GC.GetAllocatedBytesForCurrentThread();
        var parsed = await ParseJsonAsync(json);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(text, parsed.ResourceSpans[0].ScopeSpans[0].Spans[0].Attributes[0].Value.StringValue);
        Assert.IsLessThan(11L * json.Length, allocated);
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
