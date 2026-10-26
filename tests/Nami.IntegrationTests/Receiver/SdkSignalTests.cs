using System.Diagnostics.Metrics;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Nami.SemanticConventions;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SpanKind = Nami.Tests.SpanKind;

namespace Nami.IntegrationTests.Receiver;

[TestClass]
public sealed class SdkSignalTests
{
    private const string SchemaUrl = "https://opentelemetry.io/schemas/1.44.0";

    private static readonly Action<ILogger, int, Exception?> s_orderCreated =
        LoggerMessage.Define<int>(LogLevel.Information, new EventId(19, "OrderCreated"), "Created {OrderId}");

    [UsedImplicitly] public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(OtlpTransport.HttpProtobuf)]
    [DataRow(OtlpTransport.Grpc)]
    public async Task OfficialSdk_TraceNamedLogAndPartitionedCounter_AreExportedToTheReceiver(OtlpTransport transport)
    {
        var sourceName = "sdk-" + Guid.NewGuid().ToString("N");
        var result = await TelemetryCapture.TraceAsync(test =>
            {
                var resource = ResourceBuilder.CreateEmpty().AddAttributes(new Dictionary<string, object>
                    {
                        [ServiceAttributes.Name] = "sdk-service",
                        [test.Partition.ResourceAttribute] = test.Partition.Value
                    },
                    SchemaUrl);
                var receiver = ReceiverEndpoint(transport, test.Endpoint, test.GrpcEndpoint);
                using var source = new ActivitySource(sourceName, "2.0.1");
                using var traces = Sdk.CreateTracerProviderBuilder().AddSource(sourceName).SetResourceBuilder(resource)
                    .AddOtlpCapture(receiver, transport).Build();
                using var meter = new Meter(sourceName, "2.0.1");
                using var metrics = Sdk.CreateMeterProviderBuilder().AddMeter(sourceName).SetResourceBuilder(resource)
                    .AddOtlpCapture(receiver, transport).Build();
                using var logs = LoggerFactory.Create(b => b.AddOpenTelemetry(o =>
                {
                    o.Version = "2.0.1";
                    o.SchemaUrl = SchemaUrl;
                    o.SetResourceBuilder(resource).AddOtlpCapture(receiver, transport);
                }));
                using var activity = source.CreateActivity("sdk.operation", ActivityKind.Server)!.Start();
                activity.SetStatus(ActivityStatusCode.Ok).SetTag(HttpAttributes.RequestMethod, "POST");
                s_orderCreated(logs.CreateLogger(sourceName), 41, null);
                meter.CreateCounter<long>("orders.created", "{order}", "Created orders").Add(7);
                Assert.IsTrue(metrics.ForceFlush());
                activity.Stop();
                return Task.CompletedTask;
            }, t => t.Spans.Count == 1 && t.Logs.Count == 1 && t.Metrics.Count > 0,
            cancellationToken: TestContext.CancellationToken);
        Assert.IsGreaterThanOrEqualTo(3, result.Capture.Requests.Count);
        Assert.IsTrue(
            result.Capture.Requests.All(request => request.Transport == transport && !request.Payload.IsEmpty));
        Assert.IsTrue(result.Capture.Receipts.All(receipt =>
            receipt.Transport == transport && receipt is { Rejected: 0, Error: null }));
        var span = Assert.ContainsSingle(result.Spans);
        Assert.AreEqual(result.TraceId, span.TraceId);
        Assert.AreEqual("sdk.operation", span.Name);
        Assert.AreEqual(SpanKind.Server, span.Kind);
        Assert.AreEqual(ActivityStatusCode.Ok, span.Status);
        Assert.AreEqual(sourceName, span.Scope?.Name);
        Assert.AreEqual(SchemaUrl, span.Resource.SchemaUrl);
        var log = Assert.ContainsSingle(result.Logs);
        Assert.AreEqual(result.TraceId, log.TraceId);
        Assert.AreEqual(span.SpanId, log.SpanId);
        Assert.AreEqual("OrderCreated", log.EventName);
        Assert.AreEqual(9, log.SeverityNumber);
        Assert.AreEqual(sourceName, log.Scope?.Name);
        Assert.AreEqual("2.0.1", log.Scope?.Version);
        Assert.AreEqual(SchemaUrl, log.Scope?.SchemaUrl);
        Assert.AreEqual("sdk-service", log.Resource.Attributes["service.name"]);
        Assert.AreEqual(SchemaUrl, log.Resource.SchemaUrl);
        var metric = result.Metrics.First(m => m.Metric.Name == "orders.created");
        Assert.AreEqual(MetricCorrelation.ResourcePartition, metric.Correlation);
        Assert.AreEqual(sourceName, metric.Metric.Scope?.Name);
        Assert.AreEqual("{order}", metric.Metric.Unit);
        Assert.AreEqual(SchemaUrl, metric.Metric.Resource.SchemaUrl);
        var sum = Assert.IsInstanceOfType<SumData>(metric.Metric.Data);
        Assert.IsTrue(sum.IsMonotonic);
        Assert.AreEqual(7L, Assert.ContainsSingle(sum.Points).Value);
    }

    private static Uri ReceiverEndpoint(OtlpTransport transport, Uri http, Uri grpc)
    {
        return transport == OtlpTransport.Grpc ? grpc : http;
    }
}
