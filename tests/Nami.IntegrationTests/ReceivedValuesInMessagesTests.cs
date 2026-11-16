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
    ///         setting can lower it (ledger N124). Below Warning those categories repeat what a sender chose: the
    ///         request log the Content-Type and the path, routing the path, and Kestrel a request line it could
    ///         not read; the gRPC server, under <c>Grpc</c>, a content type and a grpc-encoding it does not read
    ///         and the service a path names. nami holds both families at Warning in code, so no setting writes
    ///         any of it.
    ///     </para>
    /// </summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow("content type", DisplayName = "a Content-Type nami does not read")]
    [DataRow("content encoding", DisplayName = "a Content-Encoding nami does not read")]
    [DataRow("number", DisplayName = "an integer out of range, which the parser repeats in its message")]
    [DataRow("request line", DisplayName = "a request line that is no HTTP, which Kestrel repeats when it says why")]
    [DataRow("path", DisplayName = "a path nami does not serve, which routing repeats")]
    [DataRow("grpc content type", DisplayName = "gRPC: a content type that is no gRPC, which the gRPC server repeats")]
    [DataRow("grpc service", DisplayName = "gRPC: a service nami does not have, which the gRPC server repeats")]
    [DataRow("grpc encoding", DisplayName = "gRPC: a grpc-encoding nami does not read, which the gRPC server repeats")]
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
                ["Logging:LogLevel:Microsoft.AspNetCore.Routing"] = "Trace",
                ["Logging:LogLevel:Microsoft.AspNetCore.Server.Kestrel"] = "Trace",
                ["Logging:LogLevel:Microsoft.AspNetCore.Server.Kestrel.BadRequests"] = "Debug",
                ["Logging:LogLevel:Microsoft.*.Hosting.Diagnostics"] = "Trace",
                ["Logging:LogLevel:Grpc"] = "Trace",
                ["Logging:LogLevel:Grpc.AspNetCore.Server"] = "Trace",
                ["Logging:LogLevel:Grpc.AspNetCore.Server.ServerCallHandler"] = "Information",
                [$"Logging:{typeof(Captured).FullName}:LogLevel:Default"] = "Trace",
                [$"Logging:{typeof(Captured).FullName}:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics"] = "Trace",
                [$"Logging:{typeof(Captured).FullName}:LogLevel:Microsoft.AspNetCore.Server.Kestrel"] = "Trace"
            }, logs: logs);
        int status;
        string body;
        if (shape == "request line")
        {
            // No method, no target and no version: Kestrel refuses the line and, when it logs why, repeats it.
            body = await SendRawAsync(host.HttpEndpoint, "zqm-received-ZQM900X000ZZZZZZ\r\n\r\n", context.CancellationToken);
            status = body.StartsWith("HTTP/1.1 400 ", StringComparison.Ordinal) ? 400 : 0;
        }
        else if (shape.StartsWith("grpc ", StringComparison.Ordinal))
        {
            status = await SendGrpcAsync(host.GrpcEndpoint, shape, context.CancellationToken);
            body = "";
        }
        else
        {
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(host.HttpEndpoint, shape == "path" ? "v1/zqp-received-ZQP900X000ZZZZZZ" : "v1/traces"));
            var json = shape == "number"
                ? "{\"resourceSpans\":[{\"scopeSpans\":[{\"spans\":[{\"name\":\"span\",\"attributes\":[{\"key\":\"k\",\"value\":{\"intValue\":900900900900900900900900}}]}]}]}]}"
                : "{}";
            request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
            request.Content.Headers.TryAddWithoutValidation("Content-Type",
                shape == "content type" ? "application/zqc-received-ZQC900X000ZZZZZZ" : "application/json");
            if (shape == "content encoding") request.Content.Headers.TryAddWithoutValidation("Content-Encoding", "zqe-received-ZQE900X000ZZZZZZ");

            using var response = await client.SendAsync(request, context.CancellationToken);
            body = await response.Content.ReadAsStringAsync(context.CancellationToken);
            status = (int)response.StatusCode;
        }

        Assert.AreEqual(shape switch
        {
            "number" or "request line" => 400,
            "path" => 404,
            "grpc service" or "grpc encoding" => 200,
            _ => 415
        }, status, body);
        var logged = string.Join('\n', logs.Lines);
        if (shape is "content type" or "content encoding" or "number")
        {
            // nami refuses these itself and says so.
            Assert.Contains("Failed to process OTLP traces payload: ", logged, "The refusal was not logged, so the test proves nothing.");
            Assert.Contains("   at Nami.", logged, "The stack frames are logged.");
            Assert.Contains(shape switch
            {
                "content type" => "OtlpUnsupportedMediaTypeException",
                "content encoding" => "OtlpUnsupportedContentEncodingException",
                _ => "InvalidProtocolBufferException"
            }, logged);
        }

        Assert.IsTrue(logs.Lines.Any(static line => BelowWarning(line) && !Held(line)),
            "The lowered levels did not reach the log, so the test proves nothing.");
        foreach (var received in s_received)
        {
            Assert.DoesNotContain(received, logged);
            Assert.DoesNotContain(received, body);
        }

        Assert.IsEmpty(logs.Lines.Where(static line => BelowWarning(line) && Held(line)).ToArray(),
            "ASP.NET Core's own log and its gRPC server's are held at Warning.");
    }

    /// <summary>
    ///     The two families are held, and nothing else is. One broad rule, <c>*</c> at Trace, lowers every category:
    ///     the lines below Warning of the categories outside the two families are written as the rule says, and
    ///     those of the two families are not. A hold that raised the rule itself wrote nothing below Warning at all.
    /// </summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task ABroadRuleAtTrace_LowersEveryCategory_ButTheHeldFamilies()
    {
        var directory = context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");
        using var logs = new Captured();
        await using var host = await CollectorHost.StartAsync(Path.Combine(directory, "star.duckdb"), context.CancellationToken,
            settings: new Dictionary<string, string?> { ["Logging:LogLevel:*"] = "Trace" }, logs: logs);
        using var client = new HttpClient();
        using var content = new ByteArrayContent("{}"u8.ToArray());
        content.Headers.TryAddWithoutValidation("Content-Type", "application/json");

        using var response = await client.PostAsync(new Uri(host.HttpEndpoint, "v1/zqp-received-ZQP900X000ZZZZZZ"), content, context.CancellationToken);
        var grpc = await SendGrpcAsync(host.GrpcEndpoint, "grpc service", context.CancellationToken);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual(200, grpc);
        var lowered = logs.Lines.Where(static line => BelowWarning(line)).ToArray();
        context.WriteLine(string.Join('\n', lowered.Take(20)));
        Assert.IsNotEmpty(lowered.Where(static line => line.StartsWith("[Debug]", StringComparison.Ordinal) && !Held(line)).ToArray(),
            "The rule lowers the categories that are not held: their Debug lines are written.");
        Assert.IsEmpty(lowered.Where(static line => Held(line)).ToArray(), "The two families stay at Warning.");
        var logged = string.Join('\n', logs.Lines);
        foreach (var received in s_received) Assert.DoesNotContain(received, logged);
    }

    private static readonly string[] s_received =
    [
        "ZQC900X000ZZZZZZ", "ZQE900X000ZZZZZZ", "ZQM900X000ZZZZZZ", "ZQP900X000ZZZZZZ", "ZQG900X000ZZZZZZ", "ZQS900X000ZZZZZZ",
        "ZQN900X000ZZZZZZ", "900900900"
    ];

    // A line of the two families nami holds at Warning.
    private static bool Held(string line) =>
        line.Contains("] Microsoft.AspNetCore", StringComparison.Ordinal) || line.Contains("] Grpc.", StringComparison.Ordinal);

    // One gRPC call over HTTP/2 without TLS, built by hand so that it can be wrong; the HTTP status of the answer.
    private static async Task<int> SendGrpcAsync(Uri endpoint, string shape, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, shape == "grpc service"
            ? "zqs.received.ZQS900X000ZZZZZZ/Export"
            : "opentelemetry.proto.collector.trace.v1.TraceService/Export"))
        {
            Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        // One message: the flag (1 says compressed), its length, and an export of one empty ResourceSpans.
        request.Content = new ByteArrayContent([(byte)(shape == "grpc encoding" ? 1 : 0), 0, 0, 0, 2, 0x0A, 0x00]);
        request.Content.Headers.TryAddWithoutValidation("Content-Type",
            shape == "grpc content type" ? "application/zqg-received-ZQG900X000ZZZZZZ" : "application/grpc");
        request.Headers.TryAddWithoutValidation("TE", "trailers");
        if (shape == "grpc encoding") request.Headers.TryAddWithoutValidation("grpc-encoding", "zqn-received-ZQN900X000ZZZZZZ");

        using var response = await client.SendAsync(request, cancellationToken);
        _ = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return (int)response.StatusCode;
    }

    private static bool BelowWarning(string line) =>
        !line.StartsWith("[Warning]", StringComparison.Ordinal) && !line.StartsWith("[Error]", StringComparison.Ordinal) &&
        !line.StartsWith("[Critical]", StringComparison.Ordinal);

    // Bytes as they are, where an HTTP client would refuse to send them; the answer until the server closes.
    private static async Task<string> SendRawAsync(Uri endpoint, string text, CancellationToken cancellationToken)
    {
        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken);
        await using var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(text), cancellationToken);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        return await reader.ReadToEndAsync(cancellationToken);
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
