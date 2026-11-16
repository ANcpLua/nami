using System.Buffers;
using System.Text;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Nami.Collector.Ingestion;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace Nami.IntegrationTests;

/// <summary>
///     Ledger N78 and N81. Bytes that are not UTF-8 in a string are replaced by U+FFFD on the way in (OTLP 1.11.1),
///     and replacing costs one copy: the replaced bytes are written where they belong, over JSON into the
///     normalized document and over protobuf into the one rewritten payload the parser then reads. Before, the
///     protobuf path parsed, failed, built a string and a byte array for every replaced value, copied the payload
///     and parsed again; the JSON path built a token of three times the bytes and copied it into a buffer that
///     doubled (evaluator 15: X7 +125 to +139 MiB, X10 +190 MiB, X11 +318 to +337 MiB for 16 MiB requests).
///     The cost is counted in allocated bytes on the test's own thread.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class Utf8ReplacementCostTests(TestContext context)
{
    private const int Large = 4 * 1024 * 1024;
    private static readonly IngestLimits s_limits = new();

    /// <summary>
    ///     What is replaced is what Encoding.UTF8 replaces, in a key, a name and a value nested in a list, next to
    ///     strings and bytes that stay as they were sent.
    /// </summary>
    [TestMethod]
    [DataRow(false, DisplayName = "OTLP/HTTP")]
    [DataRow(true, DisplayName = "OTLP/gRPC")]
    public async Task Protobuf_InvalidSequences_BecomeWhatEncodingUtf8MakesOfThem_AtEveryLevel(bool grpc)
    {
        // A lone continuation byte, sequences cut short after one, two and three bytes, an overlong form, an encoded
        // surrogate, a code point above U+10FFFF and a byte that starts nothing, between valid text of each length.
        byte[] invalid =
        [
            (byte)'a', 0x80, (byte)'b', 0xC3, (byte)'c', 0xE2, 0x82, (byte)'d', 0xF0, 0x9F, 0x98, (byte)'e', 0xC0, 0x80,
            0xED, 0xA0, 0x80, 0xF4, 0x90, 0x80, 0x80, 0xFF, 0xC3, 0xA9, 0xE2, 0x82, 0xAC, 0xF0, 0x9F, 0x98, 0x80, 0xE2
        ];
        var expected = Encoding.UTF8.GetString(invalid);
        Assert.Contains('�', expected);
        var placeholder = new string('@', invalid.Length);
        var span = new Span { Name = placeholder, TraceId = ByteString.CopyFrom(invalid) };
        span.Attributes.Add(new KeyValue { Key = "valid é\U0001F600", Value = new AnyValue { StringValue = "kept €" } });
        span.Attributes.Add(new KeyValue
        {
            Key = placeholder,
            Value = new AnyValue
            {
                KvlistValue = new KeyValueList
                {
                    Values =
                    {
                        new KeyValue { Key = "bytes", Value = new AnyValue { BytesValue = ByteString.CopyFrom(invalid) } },
                        new KeyValue
                        {
                            Key = "list",
                            Value = new AnyValue { ArrayValue = new ArrayValue { Values = { new AnyValue { StringValue = placeholder } } } }
                        }
                    }
                }
            }
        });
        var resource = new Resource { Attributes = { new KeyValue { Key = placeholder, Value = new AnyValue { StringValue = "r" } } } };
        var payload = Swap(new ExportTraceServiceRequest
        {
            ResourceSpans = { new ResourceSpans { Resource = resource, ScopeSpans = { new ScopeSpans { Spans = { span } } } } }
        }.ToByteArray(), Encoding.ASCII.GetBytes(placeholder), invalid);

        var parsed = await ParseAsync(payload, grpc ? Transport.Grpc : Transport.HttpProtobuf);

        var read = parsed.ResourceSpans[0].ScopeSpans[0].Spans[0];
        Assert.AreEqual(expected, parsed.ResourceSpans[0].Resource.Attributes[0].Key);
        Assert.AreEqual(expected, read.Name);
        Assert.AreEqual("valid é\U0001F600", read.Attributes[0].Key);
        Assert.AreEqual("kept €", read.Attributes[0].Value.StringValue);
        Assert.AreEqual(expected, read.Attributes[1].Key);
        var members = read.Attributes[1].Value.KvlistValue.Values;
        CollectionAssert.AreEqual(invalid, members[0].Value.BytesValue.ToByteArray(), "A bytes value is no string.");
        Assert.AreEqual(expected, members[1].Value.ArrayValue.Values[0].StringValue);
        CollectionAssert.AreEqual(invalid, read.TraceId.ToByteArray());
    }

    /// <summary>
    ///     One string value of 4 MiB. Every byte invalid: the replaced text is three times the request and the
    ///     string the parser builds twice. Only the last byte invalid: one more copy of the request than a valid
    ///     one costs. Reading the request into its buffer costs three of its length over HTTP, where the buffer grows
    ///     with the bytes that arrive, and one over gRPC.
    /// </summary>
    [TestMethod]
    [DataRow(Transport.HttpProtobuf, true, 8.5, DisplayName = "every byte invalid, OTLP/HTTP protobuf: 3 read + 3 replaced + 2 string")]
    [DataRow(Transport.Grpc, true, 6.5, DisplayName = "every byte invalid, OTLP/gRPC: 1 read + 3 replaced + 2 string")]
    [DataRow(Transport.HttpJson, true, 10.5, DisplayName = "every byte invalid, OTLP/HTTP JSON: 3 read + 3 normalized + 4 builder and string")]
    [DataRow(Transport.HttpProtobuf, false, 6.5, DisplayName = "the last byte invalid, OTLP/HTTP protobuf: 3 read + 1 replaced + 2 string")]
    [DataRow(Transport.Grpc, false, 4.5, DisplayName = "the last byte invalid, OTLP/gRPC: 1 read + 1 replaced + 2 string")]
    [DataRow(Transport.HttpJson, false, 8.5, DisplayName = "the last byte invalid, OTLP/HTTP JSON: 3 read + 1 normalized + 4 builder and string")]
    public async Task OneLargeStringOfInvalidUtf8_CostsOneCopyMore(Transport transport, bool everyByte, double timesTheRequest)
    {
        var payload = LargeString(transport);
        var at = payload.AsSpan().IndexOf("xxxxxxxx"u8);
        if (everyByte)
            payload.AsSpan(at, Large).Fill(0x80);
        else
            payload[at + Large - 1] = 0xFF;

        var before = GC.GetAllocatedBytesForCurrentThread();
        var parsed = await ParseAsync(payload, transport);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        context.WriteLine($"allocated {allocated} bytes for a request of {payload.Length}: {(double)allocated / payload.Length:F1} times");
        var value = parsed.ResourceSpans[0].ScopeSpans[0].Spans[0].Attributes[0].Value.StringValue;
        Assert.AreEqual(everyByte ? new string('�', Large) : new string('x', Large - 1) + "�", value);
        Assert.IsLessThan(timesTheRequest, (double)allocated / payload.Length);
    }

    /// <summary>A valid protobuf request is checked on its bytes and not copied: the read and the string.</summary>
    [TestMethod]
    [DataRow(Transport.HttpProtobuf, 5.5, DisplayName = "OTLP/HTTP protobuf: 3 read + 2 string")]
    [DataRow(Transport.Grpc, 3.5, DisplayName = "OTLP/gRPC: 1 read + 2 string")]
    public async Task Protobuf_AValidRequest_IsNotCopied(Transport transport, double timesTheRequest)
    {
        var payload = LargeString(transport);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var parsed = await ParseAsync(payload, transport);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        context.WriteLine($"allocated {allocated} bytes for a request of {payload.Length}: {(double)allocated / payload.Length:F1} times");
        Assert.AreEqual(Large, parsed.ResourceSpans[0].ScopeSpans[0].Spans[0].Attributes[0].Value.StringValue.Length);
        Assert.IsLessThan(timesTheRequest, (double)allocated / payload.Length);
    }

    public enum Transport
    {
        HttpProtobuf,
        Grpc,
        HttpJson
    }

    private static byte[] LargeString(Transport transport)
    {
        var request = new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    ScopeSpans =
                    {
                        new ScopeSpans
                        {
                            Spans =
                            {
                                new Span
                                {
                                    Name = "span",
                                    Attributes = { new KeyValue { Key = "k", Value = new AnyValue { StringValue = new string('x', Large) } } }
                                }
                            }
                        }
                    }
                }
            }
        };
        return transport is Transport.HttpJson
            ? Encoding.UTF8.GetBytes(JsonFormatter.Default.Format(request))
            : request.ToByteArray();
    }

    private Task<ExportTraceServiceRequest> ParseAsync(byte[] payload, Transport transport)
    {
        if (transport is Transport.Grpc)
        {
            // The gRPC path of the parser is synchronous; run where the test counts its allocations.
            var task = new Task<ExportTraceServiceRequest>(() => OtlpPayloadParser.ParseProtobuf(
                ExportTraceServiceRequest.Parser, ExportTraceServiceRequest.Descriptor, new ReadOnlySequence<byte>(payload), s_limits));
            task.RunSynchronously();
            return task;
        }

        var request = new DefaultHttpContext().Request;
        request.Body = new MemoryStream(payload, false);
        request.ContentLength = payload.Length;
        return OtlpPayloadParser.ParseTraceRequestAsync(request,
            transport is Transport.HttpJson ? OtlpPayloadEncoding.Json : OtlpPayloadEncoding.Protobuf, s_limits,
            context.CancellationToken);
    }

    private static byte[] Swap(byte[] payload, byte[] placeholder, byte[] with)
    {
        var swapped = 0;
        for (var at = payload.AsSpan().IndexOf(placeholder); at >= 0;)
        {
            with.CopyTo(payload, at);
            swapped++;
            var next = payload.AsSpan(at + with.Length).IndexOf(placeholder);
            at = next < 0 ? -1 : at + with.Length + next;
        }

        Assert.AreEqual(4, swapped, "The resource key, the span name, the attribute key and the nested value.");
        return payload;
    }
}
