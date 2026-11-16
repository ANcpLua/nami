using System.Net;
using System.Text;
using Google.Protobuf;
using Nami.Collector.Ingestion;
using Nami.Tests.Infrastructure;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace Nami.IntegrationTests;

/// <summary>
///     Ledger N55. What the converter says when it refuses a record or a request is logged by nami and returned to
///     the sender (the partial-success text, the body of a 400). It names the field that was wrong and never the
///     value that was sent: an entity key or a metric name is the sender's text, and it has not passed the
///     credential stage.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ReceivedValuesInMessagesTests(TestContext context)
{
    private const string Received = "zqe.received-ZQE900X000ZZZZZZ";
    private const ulong Time = 1_794_745_800_000_000_000;

    [TestMethod]
    [DataRow("unknown id key", DisplayName = "an entity id key that names no resource attribute")]
    [DataRow("unknown description key", DisplayName = "an entity description key that names no resource attribute")]
    [DataRow("duplicate id key", DisplayName = "an entity id key sent twice")]
    public void ARefusedResourceEntity_IsNamedByItsField_NotByTheKeyThatWasSent(string shape)
    {
        var entity = new EntityRef { Type = "service" };
        switch (shape)
        {
            case "unknown id key":
                entity.IdKeys.Add(Received);
                break;
            case "unknown description key":
                entity.IdKeys.Add("service.name");
                entity.DescriptionKeys.Add(Received);
                break;
            default:
                entity.IdKeys.Add(Received);
                entity.IdKeys.Add(Received);
                break;
        }

        var message = MessageOf(() => OtlpConverter.ConvertTraceRequest(Traces(entity)).RejectionMessage);

        Assert.Contains("OTLP resource entity", message);
        Assert.DoesNotContain("ZQE900X000ZZZZZZ", message);
        Assert.DoesNotContain("zqe.received", message);
    }

    [TestMethod]
    [DataRow("no time", DisplayName = "a data point without a time")]
    [DataRow("bucket counts", DisplayName = "a histogram with as many counts as bounds")]
    [DataRow("bounds", DisplayName = "a histogram whose bounds do not ascend")]
    public void ARefusedMetric_IsNotNamedByTheNameThatWasSent(string shape)
    {
        var metric = new Metric { Name = Received };
        switch (shape)
        {
            case "no time":
                metric.Gauge = new Gauge { DataPoints = { new NumberDataPoint { AsInt = 1 } } };
                break;
            case "bucket counts":
                metric.Histogram = new Histogram
                {
                    AggregationTemporality = AggregationTemporality.Cumulative,
                    DataPoints = { new HistogramDataPoint { TimeUnixNano = Time, Count = 1, ExplicitBounds = { 1 }, BucketCounts = { 1 } } }
                };
                break;
            default:
                metric.Histogram = new Histogram
                {
                    AggregationTemporality = AggregationTemporality.Cumulative,
                    DataPoints = { new HistogramDataPoint { TimeUnixNano = Time, Count = 1, ExplicitBounds = { 2, 1 }, BucketCounts = { 1, 0, 0 } } }
                };
                break;
        }

        var request = new ExportMetricsServiceRequest
        {
            ResourceMetrics = { new ResourceMetrics { Resource = Service(), ScopeMetrics = { new ScopeMetrics { Metrics = { metric } } } } }
        };

        var message = MessageOf(() => OtlpConverter.ConvertMetrics(request).RejectionMessage);

        Assert.IsFalse(string.IsNullOrEmpty(message), "The metric was not refused, so the test proves nothing.");
        Assert.DoesNotContain("ZQE900X000ZZZZZZ", message);
    }

    /// <summary>The same through the server: the answer to the sender holds no key that was sent.</summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task TheAnswerToARefusedResourceEntity_HoldsNoKeyThatWasSent()
    {
        var directory = context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");
        await using var host = await CollectorHost.StartAsync(Path.Combine(directory, "entity.duckdb"), context.CancellationToken);

        using var response = await host.PostProtobufAsync(Signals.Traces,
            Traces(new EntityRef { Type = "service", IdKeys = { Received } }).ToByteArray(), context.CancellationToken);
        var body = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync(context.CancellationToken));

        Assert.IsTrue(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest, response.StatusCode.ToString());
        Assert.Contains("resource entity", body, "The entity was not refused, so the test proves nothing.");
        Assert.DoesNotContain("ZQE900X000ZZZZZZ", body);
    }

    // The converter refuses a record in the batch's rejection message and a request by an exception; both texts
    // reach the log and the sender.
    private static string MessageOf(Func<string?> convert)
    {
        try
        {
            return convert() ?? "";
        }
        catch (InvalidDataException refused)
        {
            return refused.Message;
        }
    }

    private static Resource Service() => new()
    {
        Attributes = { new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "checkout" } } }
    };

    private static ExportTraceServiceRequest Traces(EntityRef entity)
    {
        var resource = Service();
        resource.EntityRefs.Add(entity);
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(Enumerable.Repeat((byte)1, 16).ToArray()),
            SpanId = ByteString.CopyFrom(Enumerable.Repeat((byte)1, 8).ToArray()),
            Name = "span", StartTimeUnixNano = Time, EndTimeUnixNano = Time + 1
        };
        return new ExportTraceServiceRequest
        {
            ResourceSpans = { new ResourceSpans { Resource = resource, ScopeSpans = { new ScopeSpans { Spans = { span } } } } }
        };
    }
}
