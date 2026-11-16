using Grpc.AspNetCore.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nami.Collector.Ingestion;
using Nami.Hosting;

namespace Nami.UnitTests.Hosting;

/// <summary>
///     The size bounds of ingest (observer's ruling, 16 Nov 2026): their defaults, and that the engine reads them
///     from its configuration under the keys <see cref="IngestLimits" /> names.
/// </summary>
[TestClass]
[TestCategory("Fast")]
public class IngestLimitsTests
{
    [TestMethod]
    public void Should_HoldTheObserversBounds_When_NothingIsConfigured()
    {
        var limits = new IngestLimits();

        Assert.AreEqual(256 * 1024, limits.MaxValueUtf8Bytes);
        Assert.AreEqual(128, limits.MaxAttributesPerRecord);
        Assert.AreEqual(1024 * 1024, limits.MaxRecordAttributeBytes);
        Assert.AreEqual(16, limits.MaxValueNesting);
        Assert.AreEqual(16 * 1024 * 1024, limits.MaxRequestWireBytes);
        Assert.AreEqual(16 * 1024 * 1024, limits.MaxRequestDecompressedBytes);
        Assert.AreEqual(50_000, limits.MaxRequestMessages);
        Assert.AreEqual(3_000, limits.MaxRequestRecords);
        AssertSameBounds(limits, IngestLimits.Default);
    }

    [TestMethod]
    public async Task Should_GiveTheEngineTheDefaults_When_ItsConfigurationNamesNoBound()
    {
        await using var app = BuildCollectorHost([]);

        AssertSameBounds(new IngestLimits(), app.Services.GetRequiredService<IOptions<IngestLimits>>().Value);
    }

    [TestMethod]
    public async Task Should_BindEveryBoundFromTheEnginesConfiguration_When_ItsKeyIsSet()
    {
        await using var app = BuildCollectorHost(new Dictionary<string, string?>
        {
            ["NAMI_INGEST_MAX_VALUE_BYTES"] = "1001",
            ["NAMI_INGEST_MAX_ATTRIBUTES"] = "1002",
            ["NAMI_INGEST_MAX_RECORD_BYTES"] = "1003",
            ["NAMI_INGEST_MAX_VALUE_NESTING"] = "4",
            ["NAMI_INGEST_MAX_REQUEST_BYTES"] = "1005",
            ["NAMI_INGEST_MAX_DECOMPRESSED_BYTES"] = "1006",
            ["NAMI_INGEST_MAX_REQUEST_MESSAGES"] = "1007",
            ["NAMI_INGEST_MAX_REQUEST_RECORDS"] = "1008"
        });

        var limits = app.Services.GetRequiredService<IOptions<IngestLimits>>().Value;

        Assert.AreEqual(1001, limits.MaxValueUtf8Bytes);
        Assert.AreEqual(1002, limits.MaxAttributesPerRecord);
        Assert.AreEqual(1003, limits.MaxRecordAttributeBytes);
        Assert.AreEqual(4, limits.MaxValueNesting);
        Assert.AreEqual(1005, limits.MaxRequestWireBytes);
        Assert.AreEqual(1006, limits.MaxRequestDecompressedBytes);
        Assert.AreEqual(1007, limits.MaxRequestMessages);
        Assert.AreEqual(1008, limits.MaxRequestRecords);
    }

    [TestMethod]
    [DataRow(IngestLimits.MaxValueUtf8BytesKey, "0")]
    [DataRow(IngestLimits.MaxAttributesPerRecordKey, "0")]
    [DataRow(IngestLimits.MaxRecordAttributeBytesKey, "-1")]
    [DataRow(IngestLimits.MaxValueNestingKey, "0")]
    [DataRow(IngestLimits.MaxValueNestingKey, "29")]
    [DataRow(IngestLimits.MaxRequestWireBytesKey, "0")]
    [DataRow(IngestLimits.MaxRequestDecompressedBytesKey, "1073741825")]
    [DataRow(IngestLimits.MaxRequestMessagesKey, "0")]
    [DataRow(IngestLimits.MaxRequestRecordsKey, "0")]
    public async Task Should_RefuseTheBoundAndNameItsKey_When_ItIsOutOfRange(string key, string value)
    {
        await using var app = BuildCollectorHost(new Dictionary<string, string?> { [key] = value });

        var refused = Assert.ThrowsExactly<OptionsValidationException>(() =>
            app.Services.GetRequiredService<IOptions<IngestLimits>>().Value);

        var failure = Assert.ContainsSingle(refused.Failures);
        Assert.Contains(key, failure);
        Assert.Contains($"'{value}'", failure);
    }

    [TestMethod]
    [DataRow(null, null, 16 * 1024 * 1024, DisplayName = "defaults")]
    [DataRow("1024", "4096", 1024, DisplayName = "wire bound smaller")]
    [DataRow("4096", "1024", 1024, DisplayName = "decompressed bound smaller")]
    public async Task Should_GiveGrpcTheSmallerRequestBound_When_TheEngineIsComposed(string? wire, string? decompressed,
        int expected)
    {
        await using var app = BuildCollectorHost(new Dictionary<string, string?>
        {
            [IngestLimits.MaxRequestWireBytesKey] = wire,
            [IngestLimits.MaxRequestDecompressedBytesKey] = decompressed
        });

        Assert.AreEqual(expected, app.Services.GetRequiredService<IOptions<GrpcServiceOptions>>().Value.MaxReceiveMessageSize);
    }

    [TestMethod]
    [DataRow(null, 30_000_000L, DisplayName = "Kestrel's default stays above the default bound")]
    [DataRow("67108864", 67108869L, DisplayName = "a bound above Kestrel's default raises it, gRPC prefix included")]
    public async Task Should_KeepKestrelsBodyBoundAtOrAboveTheRequestBound_When_TheEngineIsComposed(string? wire,
        long expected)
    {
        await using var app = BuildCollectorHost(new Dictionary<string, string?>
        {
            [IngestLimits.MaxRequestWireBytesKey] = wire
        }, true);

        Assert.AreEqual(expected,
            app.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits.MaxRequestBodySize);
    }

    private static void AssertSameBounds(IngestLimits expected, IngestLimits actual)
    {
        Assert.AreEqual(expected.MaxValueUtf8Bytes, actual.MaxValueUtf8Bytes);
        Assert.AreEqual(expected.MaxAttributesPerRecord, actual.MaxAttributesPerRecord);
        Assert.AreEqual(expected.MaxRecordAttributeBytes, actual.MaxRecordAttributeBytes);
        Assert.AreEqual(expected.MaxValueNesting, actual.MaxValueNesting);
        Assert.AreEqual(expected.MaxRequestWireBytes, actual.MaxRequestWireBytes);
        Assert.AreEqual(expected.MaxRequestDecompressedBytes, actual.MaxRequestDecompressedBytes);
        Assert.AreEqual(expected.MaxRequestMessages, actual.MaxRequestMessages);
        Assert.AreEqual(expected.MaxRequestRecords, actual.MaxRequestRecords);
    }

    private static WebApplication BuildCollectorHost(Dictionary<string, string?> settings, bool kestrel = false)
    {
        var builder = WebApplication.CreateSlimBuilder();
        _ = builder.Logging.ClearProviders();
        _ = builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddNamiCollectorCore(builder.Configuration);
        // Kestrel reads its options while the host is built, so only the test of those options configures it.
        if (kestrel) builder.Services.ConfigureNamiCollectorKestrel();
        return builder.Build();
    }
}
