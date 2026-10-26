using Google.Protobuf;
using JetBrains.Annotations;
using Nami.Tests.Evidence;
using Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Resource.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Trace.V1;
using ProtoLog = Nami.Tests.Wire.OpenTelemetry.Proto.Logs.V1.LogRecord;

namespace Nami.IntegrationTests.Receiver;

[TestClass]
public sealed class ProfilingFieldTests
{
    [UsedImplicitly] public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(Signals.Traces, 1, 0, 0)]
    [DataRow(Signals.Logs, 0, 1, 0)]
    [DataRow(Signals.Metrics, 0, 0, 1)]
    public async Task Receiver_ProfilingStringTableValueOnSignal_IsEmptyWithAWarningNotLeakedToTheNextExport(
        string signal, int expectedSpanCount, int expectedLogCount, int expectedMetricCount)
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        var resource = new Resource
        {
            Attributes =
            {
                OtlpEvidence.Pair("profile.value", new AnyValue { StringValueStrindex = 7 }),
                OtlpEvidence.Pair("nested", new AnyValue
                {
                    KvlistValue = new KeyValueList
                    {
                        Values =
                        {
                            OtlpEvidence.Pair("array", new AnyValue
                            {
                                ArrayValue = new ArrayValue { Values = { new AnyValue { StringValueStrindex = 8 } } }
                            })
                        }
                    }
                }),
                OtlpEvidence.Pair("unaffected", 42)
            }
        };
        var bytes = Request(signal, resource).ToByteArray();
        using var response = await OtlpHttp.PostAsync(receiver.Endpoint, bytes, TestContext.CancellationToken, signal);
        response.EnsureSuccessStatusCode();
        var snapshot = receiver.Snapshot();
        var receipt = Assert.ContainsSingle(snapshot.Receipts);
        Assert.AreEqual(1L, receipt.Accepted);
        Assert.AreEqual(0L, receipt.Rejected);
        Assert.IsNull(receipt.Error);
        Assert.HasCount(2, receipt.Warnings);
        Assert.Contains("StringValueStrindex 7", receipt.Warnings[0]);
        Assert.Contains("StringValueStrindex 8", receipt.Warnings[1]);
        Assert.AreSequenceEqual(bytes, Assert.ContainsSingle(snapshot.Requests).Payload.ToArray());
        Assert.HasCount(expectedSpanCount, snapshot.Spans);
        Assert.HasCount(expectedLogCount, snapshot.Logs);
        Assert.HasCount(expectedMetricCount, snapshot.Metrics);
        var observed = Assert.ContainsSingle(snapshot.Spans.Select(span => span.Resource)
            .Concat(snapshot.Logs.Select(log => log.Resource))
            .Concat(snapshot.Metrics.Select(metric => metric.Resource)));
        Assert.AreEqual(string.Empty, observed.Attributes["profile.value"]);
        Assert.AreEqual(42L, observed.Attributes["unaffected"]);
        var map = Assert.IsInstanceOfType<IReadOnlyDictionary<string, object?>>(observed.Attributes["nested"]);
        var array = Assert.IsInstanceOfType<IReadOnlyList<object?>>(map["array"]);
        Assert.AreEqual(string.Empty, Assert.ContainsSingle(array));
        var reply = await response.Content.ReadAsByteArrayAsync(TestContext.CancellationToken);
        var acknowledgement = Signals.PartialSuccess(signal, reply);
        Assert.IsNotNull(acknowledgement);
        var (rejected, warning) = acknowledgement.Value;
        Assert.AreEqual(0L, rejected);
        Assert.AreEqual(OtlpReceiver.ProtobufMediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("StringValueStrindex 7", warning);
        Assert.Contains("StringValueStrindex 8", warning);
        using var nextExportWithItsOwnDecoder =
            await OtlpHttp.PostAsync(receiver.Endpoint, [], TestContext.CancellationToken, signal);
        nextExportWithItsOwnDecoder.EnsureSuccessStatusCode();
        Assert.IsEmpty(receiver.Snapshot().Receipts[1].Warnings);
        Assert.HasCount(1, snapshot.Receipts);
    }

    [TestMethod]
    public async Task Receiver_UnknownProfileKeys_DoNotReplaceLiteralAttributes()
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        var resource = new Resource
        {
            Attributes =
            {
                new KeyValue { KeyStrindex = 3, Value = new AnyValue { StringValue = "unknown key" } },
                new KeyValue { Key = "known", KeyStrindex = 4, Value = new AnyValue { StringValue = "literal" } }
            }
        };
        using var response = await OtlpHttp.PostAsync(receiver.Endpoint, Request(Signals.Logs, resource).ToByteArray(),
            TestContext.CancellationToken, Signals.Logs);
        response.EnsureSuccessStatusCode();
        var snapshot = receiver.Snapshot();
        var log = Assert.ContainsSingle(snapshot.Logs);
        Assert.HasCount(1, log.Resource.Attributes);
        Assert.AreEqual("literal", log.Resource.Attributes["known"]);
        Assert.AreEqual(42L, log.Body);
        var receipt = Assert.ContainsSingle(snapshot.Receipts);
        Assert.AreEqual(0L, receipt.Rejected);
        Assert.HasCount(2, receipt.Warnings);
        Assert.Contains("KeyStrindex 3", receipt.Warnings[0]);
        Assert.Contains("KeyStrindex 4", receipt.Warnings[1]);
    }

    [TestMethod]
    public async Task Receiver_ProfilingKeyOnResourceSharedByTwoLogs_IsWarnedOncePerWireField()
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        var resource = new Resource
            { Attributes = { new KeyValue { KeyStrindex = 3, Value = new AnyValue { StringValue = "unknown key" } } } };
        var twoLogsOfOneResource = OtlpEvidence.Logs(resource,
            new ProtoLog { Body = new AnyValue { IntValue = 1 } },
            new ProtoLog { Body = new AnyValue { IntValue = 2 } });
        using var response = await OtlpHttp.PostAsync(receiver.Endpoint, twoLogsOfOneResource.ToByteArray(),
            TestContext.CancellationToken, Signals.Logs);
        response.EnsureSuccessStatusCode();
        Assert.HasCount(1, Assert.ContainsSingle(receiver.Snapshot().Receipts).Warnings);
    }

    private static IMessage Request(string signal, Resource resource)
    {
        return signal switch
        {
            Signals.Traces => OtlpEvidence.Traces(resource, new Span
            {
                TraceId = OtlpEvidence.Id(OtlpEvidence.TraceId), SpanId = OtlpEvidence.Id(OtlpEvidence.SpanId),
                Name = "preserved"
            }),
            Signals.Logs => OtlpEvidence.Logs(resource, new ProtoLog { Body = new AnyValue { IntValue = 42 } }),
            Signals.Metrics => OtlpEvidence.Metrics(resource, MetricFixture.Of(MetricShapes.Gauge)),
            _ => throw new ArgumentOutOfRangeException(nameof(signal))
        };
    }
}
