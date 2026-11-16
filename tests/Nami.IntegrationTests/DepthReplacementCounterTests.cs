using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text.Json.Nodes;
using Nami.Collector.Ingestion;
using Nami.Tests.Infrastructure;

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
        using var listener = new MeterListener();
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
}
