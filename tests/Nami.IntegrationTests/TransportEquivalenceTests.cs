using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

/// <summary>
///     One export, three transports, three stores: the stores must hold the same rows. Each transport gets its own
///     collector and store file with the same fixed clock, so the export keeps its identifiers and every receipt
///     time is equal; the whole files are then compared, table by table.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[DependsOn(typeof(IngestStoreTests), nameof(IngestStoreTests.Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
public sealed class TransportEquivalenceTests(TestContext context)
{
    private static readonly string[] s_transports =
        [CollectorHost.ProtobufTransport, CollectorHost.JsonTransport, CollectorHost.GrpcTransport];

    [TestMethod]
    [Timeout(90000, CooperativeCancellation = true)]
    [DataRow(Signals.Traces, "spans", 2, DisplayName = "Traces over three transports")]
    [DataRow(Signals.Logs, "logs", 2, DisplayName = "Logs over three transports")]
    [DataRow(Signals.Metrics, "metric_observations", 3, DisplayName = "Metrics over three transports")]
    public async Task Ingest_ThreeTransports_ProduceEquivalentRows(string signal, string table, int rows)
    {
        var directory = context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");
        var payload = Payload(signal);
        var stores = new Dictionary<string, SortedDictionary<string, string[]>>(StringComparer.Ordinal);
        foreach (var transport in s_transports)
        {
            var path = Path.Combine(directory, signal + "-" + transport + ".duckdb");
            await IngestAsync(path, transport, signal, payload);
            // Left out: the cache of answered reads, and the random identifier of a resource observation.
            stores[transport] = StoreDump.Read(path, "read_snapshots", "resource_observations.observation_id");
        }

        var reference = stores[CollectorHost.ProtobufTransport];
        Assert.HasCount(rows, reference[table], "The export did not reach the store: " + table);
        Assert.HasCount(1, reference["signal_watermarks"]);
        Assert.HasCount(1, reference["resource_observations"]);
        foreach (var transport in s_transports.Skip(1))
        {
            var other = stores[transport];
            Assert.AreSequenceEqual(reference.Keys.ToArray(), other.Keys.ToArray());
            foreach (var (name, expected) in reference)
            {
                Assert.AreSequenceEqual(expected, other[name],
                    $"Table {name} differs between {CollectorHost.ProtobufTransport} and {transport}.");
            }
        }
    }

    /// <summary>Starts a collector on <paramref name="path" />, sends the export and stops the collector again.</summary>
    private async Task IngestAsync(string path, string transport, string signal, string payload)
    {
        await using var host = await CollectorHost.StartAsync(path, context.CancellationToken,
            clock: new FakeTimeProvider(TestData.s_now));
        await host.SendAsync(transport, signal, payload, context.CancellationToken);
        // A read waits until the accepted export is committed.
        _ = await host.Store.ReadSnapshotAsync("default", ct: context.CancellationToken);
    }

    /// <summary>An export that uses every attribute value kind and the optional fields of its signal.</summary>
    private static string Payload(string signal)
    {
        var time = TestData.Nanos(TestData.s_now.AddSeconds(-2));
        var trace = 1.ToString("x32", CultureInfo.InvariantCulture);
        var span = 1.ToString("x16", CultureInfo.InvariantCulture);
        switch (signal)
        {
            case Signals.Traces:
                {
                    var payload = JsonNode.Parse(OtlpData.Traces(time, 2, attributes: Attributes(), error: true,
                        exceptionEvent: true))!;
                    payload["resourceSpans"]![0]!["resource"]!["attributes"]!.AsArray()
                        .Add((JsonNode)OtlpData.Attribute("service.version", "1.0.0"));
                    var spans = payload["resourceSpans"]![0]!["scopeSpans"]![0]!["spans"]!.AsArray();
                    spans[0]!["traceState"] = "vendor=value";
                    spans[0]!["flags"] = 1;
                    spans[0]!["droppedAttributesCount"] = 2;
                    spans[0]!["status"]!["message"] = "declined";
                    spans[0]!["links"] = new JsonArray(new JsonObject
                    {
                        ["traceId"] = 9.ToString("x32", CultureInfo.InvariantCulture),
                        ["spanId"] = 9.ToString("x16", CultureInfo.InvariantCulture),
                        ["attributes"] = new JsonArray(OtlpData.Attribute("http.request.method", "POST"))
                    });
                    // The second span is a child in the first span's trace.
                    spans[1]!["traceId"] = trace;
                    spans[1]!["parentSpanId"] = span;
                    return payload.ToJsonString();
                }
            case Signals.Logs:
                {
                    var payload = JsonNode.Parse(OtlpData.Logs(time, exception: true, trace: trace, span: span))!;
                    var records = payload["resourceLogs"]![0]!["scopeLogs"]![0]!["logRecords"]!.AsArray();
                    records[0]!["observedTimeUnixNano"] = (time + 1).ToString(CultureInfo.InvariantCulture);
                    records[0]!["flags"] = 1;
                    records[0]!["droppedAttributesCount"] = 1;
                    foreach (var attribute in Attributes()) records[0]!["attributes"]!.AsArray().Add(attribute!.DeepClone());
                    records.Add((JsonNode)new JsonObject
                    {
                        ["timeUnixNano"] = (time + 2).ToString(CultureInfo.InvariantCulture),
                        ["severityNumber"] = 9, ["severityText"] = "INFO",
                        ["body"] = new JsonObject
                        {
                            ["kvlistValue"] = new JsonObject
                            {
                                ["values"] = new JsonArray(OtlpData.Attribute("http.request.method", "GET"),
                                    Value("gen_ai.usage.input_tokens", "intValue", "3"))
                            }
                        }
                    });
                    return payload.ToJsonString();
                }
            default:
                {
                    var payload = JsonNode.Parse(OtlpData.Metrics(time))!;
                    var metrics = payload["resourceMetrics"]![0]!["scopeMetrics"]![0]!["metrics"]!.AsArray();
                    var start = (time - 1000000000UL).ToString(CultureInfo.InvariantCulture);
                    var at = time.ToString(CultureInfo.InvariantCulture);
                    metrics.Add((JsonNode)new JsonObject
                    {
                        ["name"] = "orders.processed", ["unit"] = "{order}", ["description"] = "Processed orders",
                        ["sum"] = new JsonObject
                        {
                            ["aggregationTemporality"] = 2, ["isMonotonic"] = true,
                            ["dataPoints"] = new JsonArray(new JsonObject
                            {
                                ["startTimeUnixNano"] = start, ["timeUnixNano"] = at, ["asDouble"] = 2.5,
                                ["attributes"] = new JsonArray(OtlpData.Attribute("http.request.method", "GET")),
                                ["exemplars"] = new JsonArray(new JsonObject
                                {
                                    ["timeUnixNano"] = at, ["asDouble"] = 2.5, ["traceId"] = trace, ["spanId"] = span
                                })
                            })
                        }
                    });
                    metrics.Add((JsonNode)new JsonObject
                    {
                        ["name"] = "http.server.request.duration", ["unit"] = "s",
                        ["histogram"] = new JsonObject
                        {
                            ["aggregationTemporality"] = 1,
                            ["dataPoints"] = new JsonArray(new JsonObject
                            {
                                ["startTimeUnixNano"] = start, ["timeUnixNano"] = at, ["count"] = "3", ["sum"] = 6.5,
                                ["min"] = 0.5, ["max"] = 4, ["bucketCounts"] = new JsonArray("1", "2"),
                                ["explicitBounds"] = new JsonArray(1)
                            })
                        }
                    });
                    return payload.ToJsonString();
                }
        }
    }

    private static JsonArray Attributes()
    {
        return new JsonArray(
            OtlpData.Attribute("http.request.method", "GET"),
            Value("http.response.status_code", "intValue", "503"),
            Value("gen_ai.request.temperature", "doubleValue", 0.25),
            Value("app.sampled", "boolValue", true),
            Value("app.payload", "bytesValue", "AQID"),
            // A deprecated key with an array value: ingest renames it and records the discovery.
            Value("rpc.grpc.request.metadata.x-request-id", "arrayValue",
                new JsonObject { ["values"] = new JsonArray(new JsonObject { ["stringValue"] = "sample" }) }),
            Value("app.context", "kvlistValue", new JsonObject
            {
                ["values"] = new JsonArray(OtlpData.Attribute("http.request.method", "GET"),
                    Value("gen_ai.usage.input_tokens", "intValue", "3"))
            }));
    }

    private static JsonObject Value(string key, string kind, JsonNode value)
    {
        return new JsonObject { ["key"] = key, ["value"] = new JsonObject { [kind] = value } };
    }
}
