using Google.Protobuf;
using JetBrains.Annotations;
using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Logs.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Logs.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Metrics.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Resource.V1;
using static Nami.Tests.Evidence.OtlpEvidence;
using LogRecord = Nami.Tests.Wire.OpenTelemetry.Proto.Logs.V1.LogRecord;

namespace Nami.IntegrationTests.Receiver;

[TestClass]
public sealed class NumberAndSessionTests
{
    [UsedImplicitly] public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task OtlpReceiverNumbers_DoublesIntegerExemplarsAndAbsentValues_KeepTheirDiscriminators()
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        var request = Metrics(null, new Metric
        {
            Name = "numeric-types", Gauge = new Gauge
            {
                DataPoints =
                {
                    new NumberDataPoint
                        { AsDouble = 0.125, Exemplars = { new Exemplar { AsInt = -42 }, new Exemplar() } },
                    new NumberDataPoint { Flags = 1 }
                }
            }
        });
        using var response = await OtlpHttp.PostAsync(receiver.Endpoint, request.ToByteArray(),
            TestContext.CancellationToken, "metrics");
        response.EnsureSuccessStatusCode();
        var metric = Assert.ContainsSingle(receiver.Snapshot().Metrics);
        Assert.AreEqual("numeric-types", metric.Name);
        var gauge = Assert.IsInstanceOfType<GaugeData>(metric.Data);
        Assert.HasCount(2, gauge.Points);
        Assert.AreEqual(0.125, Assert.IsInstanceOfType<double>(gauge.Points[0].Value));
        Assert.HasCount(2, gauge.Points[0].Exemplars);
        Assert.AreEqual(-42L, Assert.IsInstanceOfType<long>(gauge.Points[0].Exemplars[0].Value));
        Assert.IsNull(gauge.Points[0].Exemplars[0].TraceId);
        Assert.IsNull(gauge.Points[0].Exemplars[0].SpanId);
        Assert.IsNull(gauge.Points[0].Exemplars[1].Value);
        Assert.IsNull(gauge.Points[1].Value);
        Assert.AreEqual(1u, gauge.Points[1].Flags);
    }

    [TestMethod]
    public async Task ObservedSessions_Conflicting_AreExposedWithoutAliasingTheTraceId()
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        var spans = RichSpan();
        spans.ResourceSpans[0].Resource.Attributes
            .Add(Pair("session.id", "resource-session"));
        spans.ResourceSpans[0].ScopeSpans[0].Spans[0].Attributes
            .Add(Pair("session.id", "span-session"));
        using var first =
            await OtlpHttp.PostAsync(receiver.Endpoint, spans.ToByteArray(), TestContext.CancellationToken);
        first.EnsureSuccessStatusCode();
        var logs = Logs(new Resource { Attributes = { Pair("session.id", "log-resource-session") } },
            new InstrumentationScope { Name = "session-scope" },
            new LogRecord
            {
                TraceId = Id(TraceId), EventName = "SessionObserved",
                SeverityNumber = SeverityNumber.Info,
                Attributes = { Pair("session.id", "log-session") }
            });
        using var second = await OtlpHttp.PostAsync(receiver.Endpoint, logs.ToByteArray(),
            TestContext.CancellationToken, "logs");
        second.EnsureSuccessStatusCode();
        var trace = new TraceEvidence(TraceId, receiver.Snapshot());
        Assert.AreEqual("rich.operation", Assert.ContainsSingle(trace.Spans).Name);
        var log = Assert.ContainsSingle(trace.Logs);
        Assert.AreEqual("SessionObserved", log.EventName);
        Assert.AreEqual(trace.TraceId, log.TraceId);
        Assert.AreEqual("session-scope", log.Scope?.Name);
        Assert.AreEqual(9, log.SeverityNumber);
        Assert.AreSequenceEqual(["span-session", "resource-session", "log-session", "log-resource-session"],
            trace.SessionIds);
        Assert.IsNull(trace.SessionId);
    }

    [TestMethod]
    public async Task OtlpReceiverLogs_RejectedAttributes_ReportExactPartialSuccessAndKeepTheValidRecord()
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        var valid = new LogRecord
            { EventName = "Valid", SeverityNumber = SeverityNumber.Warn };
        var invalid = new LogRecord
        {
            EventName = "Invalid", Attributes = { Pair("duplicate", "first"), Pair("duplicate", "second") }
        };
        var request = Logs(null, new InstrumentationScope { Name = "partial-scope" }, valid, invalid);
        using var response = await OtlpHttp.PostAsync(receiver.Endpoint, request.ToByteArray(),
            TestContext.CancellationToken, "logs");
        response.EnsureSuccessStatusCode();
        var result =
            ExportLogsServiceResponse.Parser.ParseFrom(
                await response.Content.ReadAsByteArrayAsync(TestContext.CancellationToken));
        Assert.AreEqual(1L, result.PartialSuccess.RejectedLogRecords);
        Assert.AreEqual("Duplicate OTLP attribute: duplicate.", result.PartialSuccess.ErrorMessage);
        var log = Assert.ContainsSingle(receiver.Snapshot().Logs);
        Assert.AreEqual("Valid", log.EventName);
        Assert.AreEqual("partial-scope", log.Scope?.Name);
        Assert.AreEqual(13, log.SeverityNumber);
        var receipt = Assert.ContainsSingle(receiver.Snapshot().Receipts);
        Assert.AreEqual(1L, receipt.Accepted);
        Assert.AreEqual(1L, receipt.Rejected);
    }
}
