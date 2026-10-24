using System;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nami.Collector.Ingestion;
using Nami.Collector.Storage;
using Nami.SemanticConventions;
using Nami.Tests.Infrastructure;
using OpenTelemetry.Proto.Common.V1;

namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public sealed class IngestStoreTests(TestContext context)
{
    [TestMethod]
    public async Task Catalog_EmbeddedConstants_MatchRegistrySnapshot()
    {
        using var stream =
            typeof(RegistryInfo).Assembly.GetManifestResourceStream("Nami.SemanticConventions.RegistryInventory.json");
        Assert.IsNotNull(stream);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: context.CancellationToken);
        Assert.AreEqual(RegistryInfo.CoreCommit, json.RootElement.GetProperty("core_commit").GetString());
        Assert.AreEqual(RegistryInfo.GenAiCommit, json.RootElement.GetProperty("genai_commit").GetString());
    }

    [DependsOn(nameof(Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
    [TestMethod]
    public async Task Watermark_BackdatedRecord_DoesNotRegress()
    {
        var clock = new FakeTimeProvider(TestData.Now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        await TestData.SeedTraces(store, OtlpData.Traces(TestData.Nanos(TestData.Now.AddSeconds(-1))),
            context.CancellationToken);
        var before = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
        await TestData.SeedTraces(store, OtlpData.Traces(TestData.Nanos(TestData.Now.AddDays(-2)), offset: 1),
            context.CancellationToken);
        var after = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
        Assert.AreEqual(before.Signals[0].WatermarkUnixNano, after.Signals[0].WatermarkUnixNano);
        Assert.HasCount(2, after.Spans);
    }

    [DependsOn(nameof(Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
    [TestMethod]
    public async Task LastSeen_FutureTimestamp_UsesReceiptTime()
    {
        var clock = new FakeTimeProvider(TestData.Now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        await TestData.SeedLogs(store, OtlpData.Logs(TestData.Nanos(TestData.Now.AddYears(3))), clock,
            context.CancellationToken);
        var snapshot = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
        Assert.AreEqual(TestData.Nanos(TestData.Now), Assert.ContainsSingle(snapshot.Signals).LastSeenUnixNano);
        Assert.AreEqual(TestData.Nanos(TestData.Now.AddYears(3)), Assert.ContainsSingle(snapshot.Logs).TimeUnixNano);
    }

    [DependsOn(nameof(Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
    [TestMethod]
    public async Task Ingest_DeprecatedKey_RewrittenToSuccessor()
    {
        var clock = new FakeTimeProvider(TestData.Now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        await TestData.SeedTraces(store,
            OtlpData.Traces(TestData.Nanos(TestData.Now.AddSeconds(-1)),
                attributes: new JsonArray(OtlpData.Attribute("http.method", "POST"))), context.CancellationToken);
        var snapshot = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
        using var attrs = JsonDocument.Parse(Assert.ContainsSingle(snapshot.Spans).AttributesJson!);
        Assert.AreEqual("POST", attrs.RootElement.GetProperty("http.request.method").GetString());
        Assert.IsFalse(attrs.RootElement.TryGetProperty("http.method", out _));
        var discovery = Assert.ContainsSingle(snapshot.AttributeDiscoveries
            .Where(static d => d.OriginalKey == "http.method").ToArray());
#pragma warning disable MSTESTEXP
        using var assertions = Assert.Scope();
#pragma warning restore MSTESTEXP
        Assert.AreEqual("http.request.method", discovery.CanonicalKey);
        Assert.AreEqual("http", discovery.Namespace);
        Assert.AreEqual("default", discovery.ProjectId);
    }

    [DependsOn(nameof(Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
    [TestMethod]
    public async Task Discovery_RenamedAndDroppedKeys_SurvivesRestart()
    {
        var clock = new FakeTimeProvider(TestData.Now);
        var path = Path.Combine(
            context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),
            "discovery.duckdb");
        await using (var store = new DuckDbStore(path, timeProvider: clock))
        {
            await TestData.SeedTraces(store,
                OtlpData.Traces(TestData.Nanos(TestData.Now.AddSeconds(-1)),
                    attributes: new JsonArray(OtlpData.Attribute("http.method", "POST"),
                        OtlpData.Attribute("exception.escaped", "true"))), context.CancellationToken);
            _ = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
        }

        clock.Advance(TimeSpan.FromMinutes(1));
        await using (var store = new DuckDbStore(path, timeProvider: clock))
        {
            var snapshot = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
            var renamed = Assert.ContainsSingle(snapshot.AttributeDiscoveries
                .Where(static d => d.OriginalKey == "http.method").ToArray());
            var dropped = Assert.ContainsSingle(snapshot.AttributeDiscoveries
                .Where(static d => d.OriginalKey == "exception.escaped").ToArray());
            Assert.AreEqual("http.request.method", renamed.CanonicalKey);
            Assert.IsNull(dropped.CanonicalKey);
            Assert.AreEqual(TestData.Nanos(TestData.Now), renamed.FirstSeenUnixNano);
            using var attrs = JsonDocument.Parse(Assert.ContainsSingle(snapshot.Spans).AttributesJson!);
            Assert.IsFalse(attrs.RootElement.TryGetProperty("exception.escaped", out _));
        }
    }

    [DependsOn(nameof(Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
    [TestMethod]
    public async Task Ingest_DeprecatedTemplate_PreservesSuffixAndDiscovery()
    {
        var clock = new FakeTimeProvider(TestData.Now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        var metadata = new JsonObject
        {
            ["key"] = "rpc.grpc.request.metadata.x-request-id", ["value"] = new JsonObject
            {
                ["arrayValue"] = new JsonObject
                    { ["values"] = new JsonArray(new JsonObject { ["stringValue"] = "sample" }) }
            }
        };
        await TestData.SeedTraces(store,
            OtlpData.Traces(TestData.Nanos(TestData.Now.AddSeconds(-1)), attributes: new JsonArray(metadata)),
            context.CancellationToken);
        var snapshot = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
        var discovery = Assert.ContainsSingle(snapshot.AttributeDiscoveries
            .Where(static d => d.OriginalKey == "rpc.grpc.request.metadata.x-request-id").ToArray());
        Assert.AreEqual("rpc.request.metadata.x-request-id", discovery.CanonicalKey);
        using var attrs = JsonDocument.Parse(Assert.ContainsSingle(snapshot.Spans).AttributesJson!);
        Assert.AreEqual("sample", attrs.RootElement.GetProperty("rpc.request.metadata.x-request-id")[0].GetString());
        Assert.IsFalse(attrs.RootElement.TryGetProperty("rpc.grpc.request.metadata.x-request-id", out _));
    }

    [DependsOn(nameof(Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
    [TestMethod]
    public async Task Ingest_DevelopmentKey_StoredWithStabilityMark()
    {
        var clock = new FakeTimeProvider(TestData.Now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        var attrs = new JsonArray(new JsonObject
            { ["key"] = "gen_ai.usage.input_tokens", ["value"] = new JsonObject { ["intValue"] = "42" } });
        await TestData.SeedTraces(store,
            OtlpData.Traces(TestData.Nanos(TestData.Now.AddSeconds(-1)), attributes: attrs), context.CancellationToken);
        var snapshot = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
        var mark = Assert.ContainsSingle(snapshot.AttributeStabilities
            .Where(static a => a.AttributeKey == "gen_ai.usage.input_tokens").ToArray());
        Assert.AreEqual("development", mark.Stability);
        Assert.Contains("42", Assert.ContainsSingle(snapshot.Spans).AttributesJson!);
    }

    [DependsOn(nameof(Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
    [TestMethod]
    [DataRow(-1, DisplayName = "Below input limit")]
    [DataRow(0, DisplayName = "At input limit")]
    [DataRow(1, DisplayName = "Above input limit")]
    public void Sanitizer_Boundaries_HoldAtPlusMinusOne(int offset)
    {
        var text = new string('x', 128 * 1024 + offset);
        var actual = ExceptionTextSanitizer.Sanitize("exception.message", new AnyValue { StringValue = text });
        if (offset > 0) Assert.AreEqual("[SANITIZATION_FAILED]", actual);
        else
        {
            Assert.IsLessThanOrEqualTo(32 * 1024, Encoding.UTF8.GetByteCount(actual));
            Assert.IsTrue(actual.StartsWith(new string('x', 100), StringComparison.Ordinal));
        }
    }

    [DependsOn(nameof(Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
    [TestMethod]
    [DataRow(-1, DisplayName = "Below UTF8 output limit")]
    [DataRow(0, DisplayName = "At UTF8 output limit")]
    [DataRow(1, DisplayName = "Above UTF8 output limit")]
    public void Sanitizer_OutputUtf8Boundary_PreservesValidText(int offset)
    {
        var text = new string('x', 32 * 1024 + offset);
        var value = ExceptionTextSanitizer.Sanitize("exception.message", new AnyValue { StringValue = text });
        if (offset <= 0) Assert.AreEqual(text, value);
        else
        {
            Assert.AreEqual(32 * 1024, Encoding.UTF8.GetByteCount(value));
            Assert.IsTrue(value.EndsWith('…'));
        }

        var unicode =
            ExceptionTextSanitizer.Sanitize("exception.message", new AnyValue { StringValue = new string('ü', 20000) });
        Assert.IsLessThanOrEqualTo(32 * 1024, Encoding.UTF8.GetByteCount(unicode));
        Assert.IsFalse(unicode.Contains('\uFFFD'));
    }

    [DependsOn(nameof(Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
    [TestMethod]
    public void Sanitizer_InvalidUtf16_FailsClosed()
    {
        Assert.AreEqual("[SANITIZATION_FAILED]",
            ExceptionTextSanitizer.Sanitize("exception.stacktrace",
                new AnyValue { StringValue = "invalid" + '\ud800' }));
    }

    [DependsOn(nameof(Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
    [TestMethod]
    [DoNotParallelize]
    public async Task Resource_MissingServiceName_CountedNotDropped()
    {
        var missing = new long[1];
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, owner) =>
        {
            if (instrument.Name == Metrics.ResourceMissingServiceName.Name)
                owner.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref missing[0], value));
        listener.Start();
        var clock = new FakeTimeProvider(TestData.Now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        await TestData.SeedTraces(store, OtlpData.Traces(TestData.Nanos(TestData.Now.AddSeconds(-1)), service: null),
            context.CancellationToken);
        var snapshot = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
        Assert.HasCount(1, snapshot.Spans);
        Assert.AreEqual("unknown", snapshot.Spans[0].ServiceName);
        Assert.AreEqual(1L, Interlocked.Read(ref missing[0]));
    }

    [DependsOn(nameof(Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
    [TestMethod]
    public async Task Read_OtherProject_ReturnsNothing()
    {
        var clock = new FakeTimeProvider(TestData.Now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        await TestData.SeedTraces(store,
            OtlpData.Traces(TestData.Nanos(TestData.Now.AddSeconds(-1)), project: "tenant-a"),
            context.CancellationToken);
        var first = await store.ReadSnapshotAsync("tenant-a", ct: context.CancellationToken);
        var second = await store.ReadSnapshotAsync("tenant-b", ct: context.CancellationToken);
        Assert.HasCount(1, first.Spans);
        Assert.HasCount(0, second.Spans);
        await Assert.ThrowsExactlyAsync<SnapshotUnavailableException>(() =>
            store.ReadSnapshotAsync("tenant-b", first.Id, context.CancellationToken));
    }

    [DependsOn(nameof(Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
    [TestMethod]
    public async Task Snapshot_Expired_Refused()
    {
        var clock = new FakeTimeProvider(TestData.Now);
        await using var store =
            new DuckDbStore(":memory:", timeProvider: clock, snapshotLifetime: TimeSpan.FromMinutes(1));
        var snapshot = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(1));
        await Assert.ThrowsExactlyAsync<SnapshotUnavailableException>(() =>
            store.ReadSnapshotAsync("default", snapshot.Id, context.CancellationToken));
    }

    [DependsOn(nameof(Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
    [TestMethod]
    public async Task Retention_RemovesExpiredPreservesRecent()
    {
        var clock = new FakeTimeProvider(TestData.Now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        await TestData.SeedTraces(store, OtlpData.Traces(TestData.Nanos(TestData.Now.AddDays(-2)), offset: 0),
            context.CancellationToken);
        await TestData.SeedTraces(store, OtlpData.Traces(TestData.Nanos(TestData.Now.AddHours(-1)), offset: 1),
            context.CancellationToken);
        _ = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
        Assert.AreEqual(1,
            await store.DeleteExpiredSpansBatchAsync(TestData.Nanos(TestData.Now.AddDays(-1)), 100,
                context.CancellationToken));
        var after = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
        Assert.AreEqual(2.ToString("x32", CultureInfo.InvariantCulture), Assert.ContainsSingle(after.Spans).TraceId);
    }

    [DependsOn(nameof(Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
    [TestMethod]
    public void Store_HasNoDeletePathOutsideRetention()
    {
        var methods = typeof(INamiStore).GetMethods()
            .Where(static m => m.Name.Contains("Delete", StringComparison.Ordinal)).Select(static m => m.Name).Order()
            .ToArray();
        Assert.AreEqual(4, methods.Length);
        Assert.IsTrue(methods.All(static n => n.StartsWith("DeleteExpired", StringComparison.Ordinal)),
            string.Join(",", methods));
    }
}
