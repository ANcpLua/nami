using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Nami.Collector.Ingestion;
using Nami.Collector.Storage;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using ProtoLogRecord = OpenTelemetry.Proto.Logs.V1.LogRecord;

namespace Nami.IntegrationTests;

/// <summary>
///     Storage records every attribute key it sees for the project's stability catalog, on the single writer that every
///     export and read waits behind. One statement per key made a 1.3 MB export of 20,000 keys hold that writer for
///     about 50 s (fuzz round seed 11, case signed-in/00005-logs.pb with 20,000 added attributes). Since the bounds
///     of 16 Nov 2026 a record keeps 128 attributes, so the many keys of this test sit on a resource, which is exempt.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class AttributeKeyCountTests(TestContext context)
{
    private const int Keys = 10_000;

    private static readonly DateTimeOffset s_now = DateTimeOffset.Parse("2026-11-16T00:00:00Z", CultureInfo.InvariantCulture);

    [TestMethod]
    public async Task Export_ResourceOfTenThousandKeys_StoredWithinSecondsWithEveryKeyCatalogued()
    {
        var log = new ProtoLogRecord
        {
            TimeUnixNano = (ulong)s_now.AddMinutes(-1).ToUnixTimeMilliseconds() * 1_000_000UL,
            Body = new AnyValue { StringValue = "wide" }
        };
        // A resource is exempt from the count of 128 attributes, and 10,000 integers fit its megabyte, so every key
        // reaches storage.
        var resource = new Resource
        {
            Attributes = { new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "checkout" } } }
        };
        for (var i = 0; i < Keys; i++)
            resource.Attributes.Add(new KeyValue { Key = $"http.request.header.k{i}", Value = new AnyValue { IntValue = i } });
        var request = new ExportLogsServiceRequest
        {
            ResourceLogs = { new ResourceLogs { Resource = resource, ScopeLogs = { new ScopeLogs { LogRecords = { log } } } } }
        };
        var clock = new FakeTimeProvider(s_now);
        var rows = IngestionStorageMapper.ToLogStorageRows(OtlpConverter.ConvertLogs(request, clock));
        Assert.IsTrue(Assert.ContainsSingle(rows).ResourceDroppedAttributesCount is null or 0, "no key of the resource is dropped");
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);

        var started = Stopwatch.GetTimestamp();
        await store.InsertLogsAsync(rows, context.CancellationToken);
        var elapsed = Stopwatch.GetElapsedTime(started);

        var snapshot = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
        Assert.IsTrue(snapshot.AttributeStabilities.Count >= Keys,
            $"{snapshot.AttributeStabilities.Count} of {Keys} keys catalogued.");
        Assert.IsTrue(elapsed < TimeSpan.FromSeconds(10), $"Storing one resource of {Keys} keys took {elapsed.TotalSeconds:F1} s.");
    }
}
