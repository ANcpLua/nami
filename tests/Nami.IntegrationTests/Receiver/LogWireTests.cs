using Google.Protobuf;
using JetBrains.Annotations;
using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Logs.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Logs.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Resource.V1;
using static Nami.Tests.Evidence.OtlpEvidence;
using ProtoLog = Nami.Tests.Wire.OpenTelemetry.Proto.Logs.V1.LogRecord;

namespace Nami.IntegrationTests.Receiver;

[TestClass]
public sealed class LogWireTests
{
    private const string TextBody = "text";
    private const string IntegerBody = "integer";
    private const string DoubleBody = "double";
    private const string BooleanBody = "boolean";
    private const string BytesBody = "bytes";
    private const string ArrayBody = "array";
    private const string MapBody = "map";
    private const string EmptyBody = "empty";

    [UsedImplicitly] public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(TextBody)]
    [DataRow(IntegerBody)]
    [DataRow(DoubleBody)]
    [DataRow(BooleanBody)]
    [DataRow(BytesBody)]
    [DataRow(ArrayBody)]
    [DataRow(MapBody)]
    [DataRow(EmptyBody)]
    public async Task OtlpReceiverLogs_EveryBodyTypeAndMetadata_SurviveTheWire(string bodyKind)
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        var log = new ProtoLog
        {
            EventName = "OrderFailed", TimeUnixNano = 101, ObservedTimeUnixNano = 102,
            SeverityNumber = SeverityNumber.Error2,
            SeverityText = "ERROR-DETAIL", Flags = 0x80000001, DroppedAttributesCount = 9,
            TraceId = Id(TraceId), SpanId = Id(SpanId),
            Body = Body(bodyKind),
            Attributes =
            {
                Pair("event.name", "attribute-event"),
                Pair("exception.message", "keep this failure"),
                Pair("exception.stacktrace", "exact stack")
            }
        };
        var request = new ExportLogsServiceRequest
        {
            ResourceLogs =
            {
                new ResourceLogs
                {
                    SchemaUrl = "log-resource-schema",
                    Resource = new Resource
                        { Attributes = { Pair("service.name", "log-service") } },
                    ScopeLogs =
                    {
                        new ScopeLogs
                        {
                            SchemaUrl = "log-scope-schema",
                            Scope = new InstrumentationScope { Name = "log-scope", Version = "2.1" },
                            LogRecords = { log }
                        }
                    }
                }
            }
        };
        using var response = await OtlpHttp.PostAsync(receiver.Endpoint, request.ToByteArray(),
            TestContext.CancellationToken, Signals.Logs);
        response.EnsureSuccessStatusCode();
        var observed = Assert.ContainsSingle(receiver.Snapshot().Logs);
        Assert.AreEqual("OrderFailed", observed.EventName);
        Assert.AreEqual("attribute-event", observed.Attributes["event.name"]);
        Assert.AreEqual(TraceId, observed.TraceId);
        Assert.AreEqual(SpanId, observed.SpanId);
        Assert.AreEqual(101UL, observed.TimeUnixNano);
        Assert.AreEqual(102UL, observed.ObservedTimeUnixNano);
        Assert.AreEqual(18, observed.SeverityNumber);
        Assert.AreEqual("ERROR-DETAIL", observed.SeverityText);
        Assert.AreEqual(0x80000001u, observed.Flags);
        Assert.AreEqual(9u, observed.DroppedAttributesCount);
        Assert.AreEqual("log-service", observed.Resource.Attributes["service.name"]);
        Assert.AreEqual("log-resource-schema", observed.Resource.SchemaUrl);
        Assert.AreEqual("log-scope", observed.Scope?.Name);
        Assert.AreEqual("2.1", observed.Scope?.Version);
        Assert.AreEqual("log-scope-schema", observed.Scope?.SchemaUrl);
        Assert.AreEqual("keep this failure", observed.Attributes["exception.message"]);
        Assert.AreEqual("exact stack", observed.Attributes["exception.stacktrace"]);
    }

    [TestMethod]
    [DataRow(TextBody, "message")]
    [DataRow(IntegerBody, -42L)]
    [DataRow(DoubleBody, 0.25)]
    [DataRow(BooleanBody, true)]
    public async Task OtlpReceiverLogs_ScalarBody_KeepsItsValueAndType(string kind, object expected)
    {
        var body = await ReadBodyAsync(kind);
        Assert.AreEqual(expected, body);
    }

    [TestMethod]
    public async Task OtlpReceiverLogs_BytesBody_KeepsEveryByte()
    {
        var body = await ReadBodyAsync(BytesBody);
        Assert.AreSequenceEqual(new byte[] { 0, 255 }, Assert.IsInstanceOfType<byte[]>(body));
    }

    [TestMethod]
    public async Task OtlpReceiverLogs_ArrayBody_KeepsOrderedTypedValues()
    {
        var body = await ReadBodyAsync(ArrayBody);
        Assert.AreSequenceEqual(["item", 7L], Assert.IsInstanceOfType<IReadOnlyList<object?>>(body));
    }

    [TestMethod]
    public async Task OtlpReceiverLogs_MapBody_KeepsItsBooleanEntry()
    {
        var body = await ReadBodyAsync(MapBody);
        var map = Assert.IsInstanceOfType<IReadOnlyDictionary<string, object?>>(body);
        Assert.IsFalse(Assert.IsInstanceOfType<bool>(map["key"]));
    }

    [TestMethod]
    public async Task OtlpReceiverLogs_EmptyBody_StaysNull()
    {
        var body = await ReadBodyAsync(EmptyBody);
        Assert.IsNull(body);
    }

    private async Task<object?> ReadBodyAsync(string kind)
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        var request = Logs(null, new ProtoLog { Body = Body(kind) });
        using var response = await OtlpHttp.PostAsync(receiver.Endpoint, request.ToByteArray(),
            TestContext.CancellationToken, Signals.Logs);
        response.EnsureSuccessStatusCode();
        return receiver.Snapshot().Logs.Single().Body;
    }

    private static AnyValue Body(string kind)
    {
        return kind switch
        {
            TextBody => new AnyValue { StringValue = "message" }, IntegerBody => new AnyValue { IntValue = -42 },
            DoubleBody => new AnyValue { DoubleValue = 0.25 }, BooleanBody => new AnyValue { BoolValue = true },
            BytesBody => new AnyValue { BytesValue = ByteString.CopyFrom([0, 255]) },
            ArrayBody => new AnyValue
            {
                ArrayValue = new ArrayValue
                    { Values = { new AnyValue { StringValue = "item" }, new AnyValue { IntValue = 7 } } }
            },
            MapBody => new AnyValue
                { KvlistValue = new KeyValueList { Values = { Pair("key", false) } } },
            EmptyBody => new AnyValue(), _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
