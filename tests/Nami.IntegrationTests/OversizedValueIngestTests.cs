using System.Text.Json.Nodes;
using Nami.Collector.Ingestion;
using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

/// <summary>
///     Engine audit A3 end to end, on the tree that holds both the bounded parse (<see cref="OtlpValueBounds" />)
///     and the converter that counts its sentinel: one span whose attribute is an array of a million empty values,
///     sent over each transport. The array is never built, the attribute is dropped and counted in the span's
///     dropped_attributes_count, and the attribute beside it is stored as sent.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class OversizedValueIngestTests(TestContext context)
{
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow(CollectorHost.ProtobufTransport, DisplayName = "OTLP/HTTP protobuf")]
    [DataRow(CollectorHost.JsonTransport, DisplayName = "OTLP/HTTP JSON")]
    [DataRow(CollectorHost.GrpcTransport, DisplayName = "OTLP/gRPC")]
    public async Task Export_AnAttributeOfAMillionEmptyValues_IsDroppedAndCounted_AndItsNeighbourIsKept(string transport)
    {
        var directory = context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");
        await using var host = await CollectorHost.StartAsync(Path.Combine(directory, "a3-" + transport + ".duckdb"),
            context.CancellationToken);
        var attributes = new JsonArray(
            new JsonObject { ["key"] = "acme.wide", ["value"] = new JsonObject { ["stringValue"] = "placeholder" } },
            new JsonObject { ["key"] = "acme.small", ["value"] = new JsonObject { ["stringValue"] = "fits" } });
        var wide = "{\"arrayValue\":{\"values\":[" + string.Join(',', Enumerable.Repeat("{}", 1_000_000)) + "]}}";
        var export = OtlpData.Traces(TestData.Nanos(DateTimeOffset.UtcNow.AddSeconds(-2)), attributes: attributes)
            .Replace("{\"stringValue\":\"placeholder\"}", wide, StringComparison.Ordinal);

        await host.SendAsync(transport, Signals.Traces, export, context.CancellationToken);

        var span = Assert.ContainsSingle((await host.Store.ReadSnapshotAsync("default", ct: context.CancellationToken)).Spans);
        Assert.DoesNotContain("acme.wide", span.AttributesJson!);
        Assert.Contains("\"acme.small\":\"fits\"", span.AttributesJson!);
        Assert.AreEqual(1U, span.DroppedAttributesCount);
        Assert.DoesNotContain("nami:value-over-the-cap", span.AttributesJson!);
    }

    /// <summary>
    ///     A sender that sends the sentinel itself loses what it put it in and nothing else: the attribute whose
    ///     value it is, and the array attribute that holds it, are dropped and counted like a structure over the
    ///     cap. The export is accepted and the attribute beside them is stored as sent.
    /// </summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task Export_TheSentinelSentAsAValue_CostsTheSenderThatValueOnly()
    {
        var directory = context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");
        await using var host = await CollectorHost.StartAsync(Path.Combine(directory, "sentinel.duckdb"),
            context.CancellationToken);
        var sentinel = new JsonObject { ["stringValue"] = OtlpValueBounds.OversizedValueSentinel };
        var attributes = new JsonArray(
            new JsonObject { ["key"] = "acme.forged", ["value"] = sentinel.DeepClone() },
            new JsonObject { ["key"] = "acme.list", ["value"] = new JsonObject
                { ["arrayValue"] = new JsonObject { ["values"] = new JsonArray(sentinel.DeepClone()) } } },
            new JsonObject { ["key"] = "acme.small", ["value"] = new JsonObject { ["stringValue"] = "fits" } });
        var export = OtlpData.Traces(TestData.Nanos(DateTimeOffset.UtcNow.AddSeconds(-2)), attributes: attributes);

        await host.SendAsync(CollectorHost.JsonTransport, Signals.Traces, export, context.CancellationToken);

        var span = Assert.ContainsSingle((await host.Store.ReadSnapshotAsync("default", ct: context.CancellationToken)).Spans);
        Assert.DoesNotContain("acme.forged", span.AttributesJson!);
        Assert.DoesNotContain("acme.list", span.AttributesJson!);
        Assert.DoesNotContain("nami:value-over-the-cap", span.AttributesJson!);
        Assert.Contains("\"acme.small\":\"fits\"", span.AttributesJson!);
        Assert.AreEqual(2U, span.DroppedAttributesCount);
    }
}
