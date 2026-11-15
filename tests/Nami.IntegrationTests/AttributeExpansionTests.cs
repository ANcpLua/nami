using Google.Protobuf;
using Nami.Collector.Ingestion;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace Nami.IntegrationTests;

/// <summary>
///     How far one attribute value may expand. OTLP bounds neither array length nor key-value list size: an empty
///     AnyValue costs two bytes on the wire and far more once decoded, converted, stored and rendered, so one 7.6 MiB
///     export of 4 million of them took the engine to 1.5 GB. These tests stay red until ingest bounds a value; they hold
///     for any bound below the sizes they send, so they fix no number.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class AttributeExpansionTests
{
    private const string InnocentTrace = "0af7651916cd43dd8448eb211c80319c";
    private const string WideTrace = "4bf92f3577b34da6a3ce929d0e0e4736";

    [TestMethod]
    [DataRow(false, 1_000_000, DisplayName = "An array of a million empty values")]
    [DataRow(true, 300_000, DisplayName = "A key-value list of 300,000 persisted keys")]
    public void Export_AttributeValueExpandingWithoutBound_RefusesThatSpanAndKeepsItsSibling(bool keyed, int count)
    {
        var value = new AnyValue();
        if (keyed)
        {
            value.KvlistValue = new KeyValueList();
            // A template key persists under any suffix, so every entry survives the registry filter.
            for (var i = 0; i < count; i++)
                value.KvlistValue.Values.Add(new KeyValue { Key = $"http.request.header.k{i}", Value = new AnyValue() });
        }
        else
        {
            value.ArrayValue = new ArrayValue();
            for (var i = 0; i < count; i++) value.ArrayValue.Values.Add(new AnyValue());
        }

        var bytes = Export(value).ToByteArray();
        Assert.IsTrue(bytes.Length < 16 * 1024 * 1024, "The export fits the OTLP/HTTP size bound.");

        var batch = OtlpConverter.ConvertTraceRequest(ExportTraceServiceRequest.Parser.ParseFrom(bytes));

        Assert.AreEqual(1L, batch.RejectedSpans, "The span whose attribute value expands without bound is refused.");
        Assert.AreEqual(InnocentTrace, Assert.ContainsSingle(batch.Spans).TraceId);
    }

    private static ExportTraceServiceRequest Export(AnyValue value)
    {
        var end = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000UL;
        var wide = NewSpan(WideTrace, end);
        wide.Attributes.Add(new KeyValue { Key = "gen_ai.input.messages", Value = value });
        return new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    Resource = new Resource
                    {
                        Attributes = { new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "checkout" } } }
                    },
                    ScopeSpans = { new ScopeSpans { Spans = { NewSpan(InnocentTrace, end), wide } } }
                }
            }
        };
    }

    private static Span NewSpan(string traceId, ulong end) => new()
    {
        TraceId = ByteString.CopyFrom(Convert.FromHexString(traceId)),
        SpanId = ByteString.CopyFrom(Convert.FromHexString("b7ad6b7169203331")),
        Name = "span",
        StartTimeUnixNano = end - 1_000_000_000UL,
        EndTimeUnixNano = end
    };
}
