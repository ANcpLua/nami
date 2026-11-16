using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Nami.Collector.Ingestion;
using Nami.SemanticConventions;
using Nami.Tests.Infrastructure;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;

namespace Nami.IntegrationTests;

/// <summary>
///     A value nested deeper than the bound becomes an empty value and its attribute stays, so the loss is no
///     dropped attribute. It is counted in nami's own telemetry instead, once per replaced value, with the reason
///     "depth", whichever transport the export came over. The bounded parse replaces the value before the converter
///     sees it, so the count comes from the parser.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[DoNotParallelize]
public sealed class DepthReplacementCounterTests(TestContext context)
{
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow(CollectorHost.ProtobufTransport, DisplayName = "OTLP/HTTP protobuf")]
    [DataRow(CollectorHost.JsonTransport, DisplayName = "OTLP/HTTP JSON")]
    [DataRow(CollectorHost.GrpcTransport, DisplayName = "OTLP/gRPC")]
    public async Task Export_AValueNestedPastTheBound_CountsOneReplacementWithTheReasonDepth(string transport)
    {
        var counted = new ConcurrentQueue<(long Count, string? Reason)>();
        using var listener = Listen(counted);
        var directory = context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");
        await using var host = await CollectorHost.StartAsync(Path.Combine(directory, "depth-" + transport + ".duckdb"),
            context.CancellationToken);
        // Seventeen arrays inside one another: one more than IngestLimits.MaxValueNesting.
        JsonNode value = new JsonObject { ["stringValue"] = "bottom" };
        for (var level = 0; level <= new IngestLimits().MaxValueNesting; level++)
            value = new JsonObject { ["arrayValue"] = new JsonObject { ["values"] = new JsonArray(value) } };
        var attributes = new JsonArray(new JsonObject { ["key"] = "acme.deep", ["value"] = value });
        var export = OtlpData.Traces(TestData.Nanos(DateTimeOffset.UtcNow.AddSeconds(-2)), attributes: attributes);

        await host.SendAsync(transport, Signals.Traces, export, context.CancellationToken);

        var span = Assert.ContainsSingle((await host.Store.ReadSnapshotAsync("default", ct: context.CancellationToken)).Spans);
        Assert.Contains("acme.deep", span.AttributesJson!);
        Assert.AreEqual(0U, span.DroppedAttributesCount, "The attribute stays: a replaced value is no dropped attribute.");
        Assert.AreSequenceEqual([(1L, (string?)"depth")], counted.ToArray());
    }

    /// <summary>
    ///     The bounded parse replaces the deep value on the bytes, before the parser reads them. When the parser
    ///     then refuses the request, nothing of it is stored and nothing is counted: the count is taken after the
    ///     parse. Over JSON the parser refuses a span kind that is an object; over protobuf packed bucket counts of
    ///     seven bytes, where each count takes eight.
    /// </summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow(false, DisplayName = "OTLP/HTTP JSON, a span kind that is an object")]
    [DataRow(true, DisplayName = "OTLP/HTTP protobuf, packed bucket counts cut short")]
    public async Task ARequestTheParserRefuses_CountsNoReplacement_ThoughItsDeepValueWasReplaced(bool protobuf)
    {
        var limits = new IngestLimits();
        var counted = new ConcurrentQueue<(long Count, string? Reason)>();
        using var listener = Listen(counted);
        var request = new DefaultHttpContext().Request;
        byte[] payload;
        if (protobuf)
        {
            var deep = new AnyValue { StringValue = "bottom" };
            for (var level = 0; level <= limits.MaxValueNesting; level++)
                deep = new AnyValue { ArrayValue = new ArrayValue { Values = { deep } } };
            // Export { resource_metrics = 1 { scope_metrics = 2 { metrics = 2 { name = 1, histogram = 9 { data_points = 1 {
            // attributes = 9, bucket_counts = 6 (packed fixed64) } } } } } }.
            var point = Field(1, [.. Field(9, new KeyValue { Key = "acme.deep", Value = deep }.ToByteArray()), .. Field(6, new byte[7])]);
            payload = Field(1, Field(2, Field(2, [.. Field(1, "m"u8.ToArray()), .. Field(9, point)])));
            Assert.AreEqual(1, OtlpValueBounds.BoundProtobuf(payload.ToArray(), ExportMetricsServiceRequest.Descriptor, limits).TooDeep);
            request.Body = new MemoryStream(payload, false);

            await Assert.ThrowsAsync<InvalidProtocolBufferException>(() => OtlpPayloadParser.ParseMetricsRequestAsync(
                request, OtlpPayloadEncoding.Protobuf, limits, context.CancellationToken));
        }
        else
        {
            JsonNode value = new JsonObject { ["stringValue"] = "bottom" };
            for (var level = 0; level <= limits.MaxValueNesting; level++)
                value = new JsonObject { ["arrayValue"] = new JsonObject { ["values"] = new JsonArray(value) } };
            var export = OtlpData.Traces(TestData.Nanos(DateTimeOffset.UtcNow.AddSeconds(-2)),
                attributes: new JsonArray(new JsonObject { ["key"] = "acme.deep", ["value"] = value }));
            Assert.Contains("\"kind\":2", export);
            payload = Encoding.UTF8.GetBytes(export.Replace("\"kind\":2", "\"kind\":{}", StringComparison.Ordinal));
            Assert.AreEqual(1, OtlpValueBounds.BoundJson(payload.ToArray(), limits).TooDeep);
            request.Body = new MemoryStream(payload, false);

            await Assert.ThrowsAsync<InvalidProtocolBufferException>(() => OtlpPayloadParser.ParseTraceRequestAsync(
                request, OtlpPayloadEncoding.Json, limits, context.CancellationToken));
        }

        Assert.IsEmpty(counted.ToArray(), "A refused request counts nothing.");
    }

    private static MeterListener Listen(ConcurrentQueue<(long Count, string? Reason)> counted)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = static (instrument, meters) =>
        {
            if (instrument.Meter.Name == CollectorSemanticAttributeCatalog.InstrumentationScope &&
                instrument.Name == CollectorSemanticAttributeCatalog.ValuesReplacedMetric)
                meters.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            string? reason = null;
            foreach (var tag in tags)
                if (tag.Key == CollectorSemanticAttributeCatalog.NamiValueReplacementReason) reason = tag.Value as string;
            counted.Enqueue((measurement, reason));
        });
        listener.Start();
        return listener;
    }

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
