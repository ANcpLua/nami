using System.Net;
using System.Text;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
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
    [DataRow("no data", DisplayName = "a metric without a data point set")]
    public void ARefusedMetric_IsNotNamedByTheNameThatWasSent(string shape)
    {
        var metric = new Metric { Name = Received };
        switch (shape)
        {
            case "no time":
                metric.Gauge = new Gauge { DataPoints = { new NumberDataPoint { AsInt = 1 } } };
                break;
            case "no data":
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

    /// <summary>
    ///     nami's own log holds nothing a sender chose. A request refused for its Content-Type or its
    ///     Content-Encoding is logged without the header's value, and a payload a library refuses is logged by the
    ///     type of the failure: Google.Protobuf repeats a number that is out of range in its message.
    ///     <para>
    ///         The log is read whole, ASP.NET Core's categories included, with every level lowered as far as a
    ///         setting can lower it (ledger N124). Its request log names the Content-Type of every request at
    ///         Information; nami holds those categories at Warning in code, so no setting writes the header.
    ///     </para>
    /// </summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow("content type", DisplayName = "a Content-Type nami does not read")]
    [DataRow("content encoding", DisplayName = "a Content-Encoding nami does not read")]
    [DataRow("number", DisplayName = "an integer out of range, which the parser repeats in its message")]
    public async Task NamisLog_HoldsNoValueThatWasSent(string shape)
    {
        var directory = context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");
        using var logs = new Captured();
        await using var host = await CollectorHost.StartAsync(Path.Combine(directory, "log.duckdb"), context.CancellationToken,
            settings: new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = "Trace",
                ["Logging:LogLevel:Microsoft.AspNetCore"] = "Trace",
                ["Logging:LogLevel:Microsoft.AspNetCore.Hosting"] = "Trace",
                ["Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics"] = "Information",
                ["Logging:LogLevel:Microsoft.*.Hosting.Diagnostics"] = "Trace",
                [$"Logging:{typeof(Captured).FullName}:LogLevel:Default"] = "Trace",
                [$"Logging:{typeof(Captured).FullName}:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics"] = "Trace"
            }, logs: logs);
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(host.HttpEndpoint, "v1/traces"));
        var json = shape == "number"
            ? "{\"resourceSpans\":[{\"scopeSpans\":[{\"spans\":[{\"name\":\"span\",\"attributes\":[{\"key\":\"k\",\"value\":{\"intValue\":900900900900900900900900}}]}]}]}]}"
            : "{}";
        request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
        request.Content.Headers.TryAddWithoutValidation("Content-Type",
            shape == "content type" ? "application/zqc-received-ZQC900X000ZZZZZZ" : "application/json");
        if (shape == "content encoding") request.Content.Headers.TryAddWithoutValidation("Content-Encoding", "zqe-received-ZQE900X000ZZZZZZ");

        using var response = await client.SendAsync(request, context.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(context.CancellationToken);

        Assert.AreEqual(shape == "number" ? HttpStatusCode.BadRequest : HttpStatusCode.UnsupportedMediaType, response.StatusCode, body);
        var logged = string.Join('\n', logs.Lines);
        Assert.Contains("Failed to process OTLP traces payload: ", logged, "The refusal was not logged, so the test proves nothing.");
        Assert.Contains("   at Nami.", logged, "The stack frames are logged.");
        Assert.Contains("[Debug] Microsoft.AspNetCore.", logged, "The lowered levels did not reach the log, so the test proves nothing.");
        Assert.IsEmpty(logs.Lines.Where(static line => line.Contains("] Microsoft.AspNetCore.Hosting", StringComparison.Ordinal) &&
                                                       !line.StartsWith("[Warning]", StringComparison.Ordinal) &&
                                                       !line.StartsWith("[Error]", StringComparison.Ordinal) &&
                                                       !line.StartsWith("[Critical]", StringComparison.Ordinal)).ToArray(),
            "The request log is held at Warning.");
        Assert.Contains(shape switch
        {
            "content type" => "OtlpUnsupportedMediaTypeException",
            "content encoding" => "OtlpUnsupportedContentEncodingException",
            _ => "InvalidProtocolBufferException"
        }, logged);
        foreach (var received in new[] { "ZQC900X000ZZZZZZ", "ZQE900X000ZZZZZZ", "900900900" })
        {
            Assert.DoesNotContain(received, logged);
            Assert.DoesNotContain(received, body);
        }
    }

    /// <summary>
    ///     What is logged of a failure: the type and the stack frames of the exception and of each one inside it,
    ///     so that a defect in nami can be found from the log, and no message, because a message can hold what was
    ///     sent. An exception written with ToString() fails this row.
    /// </summary>
    [TestMethod]
    public void TheLoggedFailure_HasTheTypesAndFramesOfEveryException_AndNoMessage()
    {
        Exception thrown;
        try
        {
            try
            {
                throw new FormatException("inner holds " + Received);
            }
            catch (FormatException inner)
            {
                throw new InvalidDataException("outer holds " + Received, inner);
            }
        }
        catch (InvalidDataException outer)
        {
            thrown = outer;
        }

        var logged = Nami.Hosting.OtlpFailureLog.Describe(thrown);

        Assert.Contains("System.IO.InvalidDataException", logged);
        Assert.Contains(" ---> System.FormatException", logged);
        Assert.AreEqual(2, logged.Split(nameof(TheLoggedFailure_HasTheTypesAndFramesOfEveryException_AndNoMessage)).Length - 1,
            "The frames of both exceptions: " + logged);
        Assert.DoesNotContain("ZQE900X000ZZZZZZ", logged);
        Assert.DoesNotContain("holds", logged);
    }

    /// <summary>
    ///     Ledger N126. An <see cref="AggregateException" /> holds several exceptions, and InnerException is only the
    ///     first of them: each member is logged with its type and frames, and what is inside a member too. Still no
    ///     message.
    /// </summary>
    [TestMethod]
    public void TheLoggedFailure_HasEveryMemberOfAnAggregateException_AndNoMessage()
    {
        var thrown = new AggregateException("several hold " + Received,
            Thrown(FirstMember), Thrown(SecondMember), new AggregateException(Thrown(ThirdMember)));

        var logged = Nami.Hosting.OtlpFailureLog.Describe(thrown);

        context.WriteLine(logged);
        var types = logged.Split('\n').Where(static line => !line.StartsWith("   at ", StringComparison.Ordinal))
            .Select(static line => line.Trim()).ToArray();
        Assert.AreEqual(
            "System.AggregateException|---> System.FormatException|---> System.IO.InvalidDataException|" +
            "---> System.TimeoutException|---> System.AggregateException|---> System.ArgumentException",
            string.Join('|', types), "Every member, in the order held, and what is inside it.");
        Assert.Contains(nameof(FirstMember), logged, "The frames of every member.");
        Assert.Contains(nameof(SecondMember), logged);
        Assert.Contains(nameof(InsideSecond), logged);
        Assert.Contains(nameof(ThirdMember), logged);
        Assert.DoesNotContain("ZQE900X000ZZZZZZ", logged);
        Assert.DoesNotContain("hold", logged);

        static void FirstMember() => throw new FormatException("first holds " + Received);

        static void InsideSecond() => throw new TimeoutException("inside holds " + Received);

        static void SecondMember()
        {
            try
            {
                InsideSecond();
            }
            catch (TimeoutException inside)
            {
                throw new InvalidDataException("second holds " + Received, inside);
            }
        }

        static void ThirdMember() => throw new ArgumentException("third holds " + Received);
    }

    /// <summary>
    ///     Ledger N93. A failed tool call in nami's own telemetry: the span event and the log line hold the type
    ///     and the frames of the exception and of what is inside it, and no message. A message can repeat received
    ///     text, and nami's own telemetry is stored and read like any other.
    /// </summary>
    [TestMethod]
    public void AFailedToolCall_IsRecordedByTypeAndFrames_NeverByItsMessage()
    {
        using var logs = new Captured();
        using var activity = new System.Diagnostics.Activity("tools/call");
        activity.Start();
        var thrown = Thrown(Outer);

        Nami.Telemetry.NamiTelemetry.RecordException(thrown, logs.CreateLogger("nami"));

        var recorded = activity.Events.Single();
        var tags = recorded.Tags.ToDictionary(static tag => tag.Key, static tag => tag.Value?.ToString() ?? "");
        var logged = string.Join('\n', logs.Lines);
        context.WriteLine(string.Join('\n', tags.Select(static tag => tag.Key + " = " + tag.Value)));
        context.WriteLine(logged);
        Assert.AreEqual("exception", recorded.Name);
        Assert.AreEqual("System.InvalidOperationException", tags["exception.type"]);
        Assert.AreEqual("exception.stacktrace|exception.type", string.Join('|', tags.Keys.Order(StringComparer.Ordinal)), "No exception.message.");
        foreach (var text in new[] { tags["exception.stacktrace"], logged })
        {
            Assert.Contains("System.InvalidOperationException", text);
            Assert.Contains(" ---> System.FormatException", text);
            Assert.Contains(nameof(Outer), text, "The frames of the exception.");
            Assert.Contains(nameof(Inner), text, "The frames of the exception inside it.");
        }

        Assert.Contains("[Error] nami: Nami tool failed: ", logged);
        foreach (var text in tags.Values.Append(logged))
        {
            Assert.DoesNotContain("ZQE900X000ZZZZZZ", text);
            Assert.DoesNotContain("holds", text);
        }

        static void Inner() => throw new FormatException("inner holds " + Received);

        static void Outer()
        {
            try
            {
                Inner();
            }
            catch (FormatException inner)
            {
                throw new InvalidOperationException("outer holds " + Received, inner);
            }
        }
    }

    private static Exception Thrown(Action fails)
    {
        try
        {
            fails();
        }
        catch (Exception thrown)
        {
            return thrown;
        }

        throw new InvalidOperationException("Nothing was thrown.");
    }

    // Every line that reaches nami's log, under every category: the level, the category, the formatted message
    // and, when one is logged, the whole exception.
    private sealed class Captured : ILoggerProvider
    {
        internal System.Collections.Concurrent.ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Category(categoryName, Lines);

        public void Dispose()
        {
        }

        private sealed class Category(string name, System.Collections.Concurrent.ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                lines.Enqueue($"[{logLevel}] {name}: {formatter(state, exception)} {exception}");
        }
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
