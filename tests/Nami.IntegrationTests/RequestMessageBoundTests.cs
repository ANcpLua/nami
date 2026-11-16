using System.Buffers;
using System.Collections;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Nami.Collector.Ingestion;
using Nami.Tests.Infrastructure;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace Nami.IntegrationTests;

/// <summary>
///     E4e. nami receiver policy: one export request holds at most <see cref="IngestLimits.MaxRequestMessages" />
///     protobuf messages, every type counted alike, and among them at most
///     <see cref="IngestLimits.MaxRequestRecords" /> records, the messages stored as rows of their own (spans, log
///     records, metric data points, exemplars). The parser builds one object per message and the store one row per
///     record, and an empty message is two bytes of the request, so the byte bounds alone do not bound what a
///     request costs: two million empty spans in 3.8 MiB took 465 MiB to parse, and ten thousand minimal spans in
///     510 KB took 294 MiB to store. A request over either bound is refused whole before it is parsed, with the
///     answer of a request over a byte bound. What a structure over the value cap held does not count: it is
///     dropped without being built.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class RequestMessageBoundTests(TestContext context)
{
    private static readonly IngestLimits s_limits = new();

    [TestMethod]
    [Timeout(180000, CooperativeCancellation = true)]
    [DataRow(CollectorHost.ProtobufTransport, false, DisplayName = "messages, OTLP/HTTP protobuf")]
    [DataRow(CollectorHost.JsonTransport, false, DisplayName = "messages, OTLP/HTTP JSON")]
    [DataRow(CollectorHost.GrpcTransport, false, DisplayName = "messages, OTLP/gRPC")]
    [DataRow(CollectorHost.ProtobufTransport, true, DisplayName = "records, OTLP/HTTP protobuf")]
    [DataRow(CollectorHost.JsonTransport, true, DisplayName = "records, OTLP/HTTP JSON")]
    [DataRow(CollectorHost.GrpcTransport, true, DisplayName = "records, OTLP/gRPC")]
    public async Task ARequestOfOneOverTheBound_IsRefusedWhole_AndOneOfExactlyTheBoundIsAccepted(string transport,
        bool records)
    {
        var directory = context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");
        await using var host = await CollectorHost.StartAsync(
            Path.Combine(directory, (records ? "records-" : "messages-") + transport + ".duckdb"), context.CancellationToken);
        var bound = records ? s_limits.MaxRequestRecords : s_limits.MaxRequestMessages;
        var atTheBound = records ? SpansOf(bound) : ExportOf(bound);
        var over = records ? SpansOf(bound + 1) : ExportOf(bound + 1);
        var refusal = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"OTLP request holds more than {bound} {(records ? "records" : "messages")}.");

        if (transport == CollectorHost.GrpcTransport)
        {
            var refused = await Assert.ThrowsExactlyAsync<RpcException>(() =>
                host.ExportGrpcAsync(Signals.Traces, CollectorHost.Encode(Signals.Traces, over), context.CancellationToken));
            Assert.AreEqual(StatusCode.ResourceExhausted, refused.StatusCode, refused.Status.Detail);
            Assert.AreEqual(refusal, refused.Status.Detail);
            Assert.IsNull(refused.Trailers.GetValue("grpc-status-details-bin"), "No RetryInfo: the request is not to be sent again.");
        }
        else
        {
            using var refused = transport == CollectorHost.JsonTransport
                ? await host.PostJsonAsync(Signals.Traces, over, context.CancellationToken)
                : await host.PostProtobufAsync(Signals.Traces, CollectorHost.Encode(Signals.Traces, over), context.CancellationToken);
            Assert.AreEqual((HttpStatusCode)OtlpPayloadTooLargeException.TooManyMessagesHttpStatus, refused.StatusCode);
            Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, refused.StatusCode);
            var error = await Assert.That.HasOtlpErrorAsync(refused, context.CancellationToken,
                transport == CollectorHost.JsonTransport ? "application/json" : OtlpReceiver.ProtobufMediaType);
            Assert.AreEqual(refusal, error.Message);
        }

        Assert.IsEmpty((await host.Store.ReadSnapshotAsync("default", ct: context.CancellationToken)).Spans,
            "Nothing of a refused request is stored.");

        await host.SendAsync(transport, Signals.Traces, atTheBound, context.CancellationToken);

        Assert.HasCount(records ? bound : 1,
            (await host.Store.ReadSnapshotAsync("default", ct: context.CancellationToken)).Spans);
    }

    /// <summary>
    ///     Every message is counted once and alike in both encodings: the counts of the scan are the objects
    ///     Google.Protobuf builds and the rows the store writes, taken here from the parsed request itself.
    /// </summary>
    [TestMethod]
    public void TheCounts_AreTheMessagesTheParserBuilds_AndTheRecordsAmongThem_OnTheWireAndInJson()
    {
        var json = ExportOf(1000);
        var export = ExportTraceServiceRequest.Parser.ParseJson(TestData.ProtobufJson(json));
        Assert.AreEqual(new OtlpRequestCounts(1000, 1), Count(export));
        _ = OtlpValueBounds.BoundProtobuf(export.ToByteArray(), ExportTraceServiceRequest.Descriptor, s_limits, out var onTheWire);
        Assert.AreEqual(new OtlpRequestCounts(1000, 1), onTheWire);
        _ = OtlpValueBounds.BoundJson(Encoding.UTF8.GetBytes(json), s_limits, out var inJson);
        Assert.AreEqual(new OtlpRequestCounts(1000, 1), inJson);

        // Values of every kind, nested: arrays, key-value lists, members without a value, empty values. An event and
        // a link are messages and no records: they are stored inside their span's row.
        var nested = new ExportTraceServiceRequest
        {
            ResourceSpans = { new ResourceSpans { ScopeSpans = { new ScopeSpans { Spans = { new Span(), new Span
            {
                Name = "span", Status = new Status(), Events = { new Span.Types.Event { Name = "event" } },
                Links = { new Span.Types.Link() },
                Attributes =
                {
                    new KeyValue { Key = "list", Value = new AnyValue { ArrayValue = new ArrayValue { Values =
                    {
                        new AnyValue(), new AnyValue { StringValue = "text" },
                        new AnyValue { KvlistValue = new KeyValueList { Values =
                        {
                            new KeyValue { Key = "bare" },
                            new KeyValue { Key = "inner", Value = new AnyValue { ArrayValue = new ArrayValue() } }
                        } } }
                    } } } },
                    new KeyValue { Key = "small", Value = new AnyValue { IntValue = 1 } }
                }
            } } } } } }
        };
        var expected = Count(nested);
        Assert.AreEqual(2, expected.Records);
        _ = OtlpValueBounds.BoundProtobuf(nested.ToByteArray(), ExportTraceServiceRequest.Descriptor, s_limits, out onTheWire);
        Assert.AreEqual(expected, onTheWire);
        _ = OtlpValueBounds.BoundJson(Encoding.UTF8.GetBytes(JsonFormatter.Default.Format(nested)), s_limits, out inJson);
        Assert.AreEqual(expected, inJson);

        // Every kind of record: log records, the four kinds of data point, exemplars.
        var logs = new ExportLogsServiceRequest
        {
            ResourceLogs = { new OpenTelemetry.Proto.Logs.V1.ResourceLogs { ScopeLogs = { new OpenTelemetry.Proto.Logs.V1.ScopeLogs
                { LogRecords = { new OpenTelemetry.Proto.Logs.V1.LogRecord(), new OpenTelemetry.Proto.Logs.V1.LogRecord { SeverityText = "INFO" } } } } } }
        };
        Assert.AreEqual(2, Count(logs).Records);
        _ = OtlpValueBounds.BoundProtobuf(logs.ToByteArray(), ExportLogsServiceRequest.Descriptor, s_limits, out onTheWire);
        Assert.AreEqual(Count(logs), onTheWire);
        _ = OtlpValueBounds.BoundJson(Encoding.UTF8.GetBytes(JsonFormatter.Default.Format(logs)), s_limits, out inJson);
        Assert.AreEqual(Count(logs), inJson);

        var exemplar = new OpenTelemetry.Proto.Metrics.V1.Exemplar { AsInt = 1 };
        var metrics = new ExportMetricsServiceRequest
        {
            ResourceMetrics = { new OpenTelemetry.Proto.Metrics.V1.ResourceMetrics { ScopeMetrics = { new OpenTelemetry.Proto.Metrics.V1.ScopeMetrics { Metrics =
            {
                new OpenTelemetry.Proto.Metrics.V1.Metric { Name = "gauge", Gauge = new OpenTelemetry.Proto.Metrics.V1.Gauge
                    { DataPoints = { new OpenTelemetry.Proto.Metrics.V1.NumberDataPoint { AsInt = 1, Exemplars = { exemplar } } } } },
                new OpenTelemetry.Proto.Metrics.V1.Metric { Name = "histogram", Histogram = new OpenTelemetry.Proto.Metrics.V1.Histogram
                    { DataPoints = { new OpenTelemetry.Proto.Metrics.V1.HistogramDataPoint { Count = 1 } } } },
                new OpenTelemetry.Proto.Metrics.V1.Metric { Name = "exponential", ExponentialHistogram = new OpenTelemetry.Proto.Metrics.V1.ExponentialHistogram
                    { DataPoints = { new OpenTelemetry.Proto.Metrics.V1.ExponentialHistogramDataPoint { Count = 1 } } } },
                new OpenTelemetry.Proto.Metrics.V1.Metric { Name = "summary", Summary = new OpenTelemetry.Proto.Metrics.V1.Summary
                    { DataPoints = { new OpenTelemetry.Proto.Metrics.V1.SummaryDataPoint { Count = 1 } } } }
            } } } } }
        };
        Assert.AreEqual(5, Count(metrics).Records);
        _ = OtlpValueBounds.BoundProtobuf(metrics.ToByteArray(), ExportMetricsServiceRequest.Descriptor, s_limits, out onTheWire);
        Assert.AreEqual(Count(metrics), onTheWire);
        _ = OtlpValueBounds.BoundJson(Encoding.UTF8.GetBytes(JsonFormatter.Default.Format(metrics)), s_limits, out inJson);
        Assert.AreEqual(Count(metrics), inJson);
    }

    /// <summary>
    ///     Engine audit A3 holds four million messages, nearly all of them in one structure over the value cap. That
    ///     structure is dropped without being built, so its messages do not count: the export is accepted with the
    ///     attribute replaced, not refused.
    /// </summary>
    [TestMethod]
    [DataRow("protobuf")]
    [DataRow("json")]
    [DataRow("grpc")]
    public async Task AnExportOfFourMillionValuesInOneStructure_IsAcceptedWithTheStructureDropped(string transport)
    {
        const int Values = 4_000_000;
        var wide = new StringBuilder("{\"arrayValue\":{\"values\":[", 3 * Values + 64);
        for (var i = 0; i < Values; i++) _ = wide.Append(i == 0 ? "{}" : ",{}");
        var json = Encoding.UTF8.GetBytes(ExportOf(100).Replace("{\"key\":\"k0\",\"value\":{\"intValue\":\"0\"}}",
            "{\"key\":\"wide\",\"value\":" + wide.Append("]}}") + "}", StringComparison.Ordinal));
        var bounded = json.ToArray();
        Assert.AreEqual(new OtlpValueBoundsResult(1, 0), OtlpValueBounds.BoundJson(bounded, s_limits, out var counted));
        Assert.AreEqual(new OtlpRequestCounts(100, 1), counted, "The structure counts as the one value it is replaced by.");

        ExportTraceServiceRequest parsed;
        if (transport == "json")
        {
            var request = new DefaultHttpContext().Request;
            request.Body = new MemoryStream(json, false);
            parsed = await OtlpPayloadParser.ParseTraceRequestAsync(request, OtlpPayloadEncoding.Json, s_limits,
                context.CancellationToken);
        }
        else
        {
            // The same export as protobuf, with the structure written by hand: Google.Protobuf would build four
            // million objects to serialize it here.
            var small = ExportTraceServiceRequest.Parser.ParseJson(TestData.ProtobufJson(ExportOf(100)));
            var span = small.ResourceSpans[0].ScopeSpans[0].Spans[0];
            var first = span.Attributes.Single(static attribute => attribute.Key == "k0");
            first.Key = "wide";
            first.Value = new AnyValue { BytesValue = ByteString.CopyFrom(new byte[2 * Values]) };
            var protobuf = small.ToByteArray();
            // bytes_value (field 7) of 2 x Values zero bytes becomes array_value (field 5) of Values empty items
            // (tag of field 1, length 0): the same length, so no prefix changes.
            var at = protobuf.AsSpan().IndexOf(new byte[64]) - 5;
            Assert.AreEqual((7 << 3) | 2, protobuf[at]);
            protobuf[at] = (5 << 3) | 2;
            for (var i = 0; i < Values; i++) protobuf[at + 5 + 2 * i] = (1 << 3) | 2;
            var scanned = protobuf.ToArray();
            Assert.AreEqual(new OtlpValueBoundsResult(1, 0),
                OtlpValueBounds.BoundProtobuf(scanned, ExportTraceServiceRequest.Descriptor, s_limits, out counted));
            Assert.AreEqual(new OtlpRequestCounts(100, 1), counted);
            if (transport == "grpc")
            {
                parsed = OtlpPayloadParser.ParseProtobuf(ExportTraceServiceRequest.Parser,
                    ExportTraceServiceRequest.Descriptor, new ReadOnlySequence<byte>(protobuf), s_limits);
            }
            else
            {
                var request = new DefaultHttpContext().Request;
                request.Body = new MemoryStream(protobuf, false);
                parsed = await OtlpPayloadParser.ParseTraceRequestAsync(request, OtlpPayloadEncoding.Protobuf, s_limits,
                    context.CancellationToken);
            }
        }

        var attributes = parsed.ResourceSpans[0].ScopeSpans[0].Spans[0].Attributes;
        Assert.AreEqual(OtlpValueBounds.OversizedValueSentinel, attributes.Single(static a => a.Key == "wide").Value.StringValue);
        Assert.AreEqual(1L, attributes.Single(static a => a.Key == "k1").Value.IntValue);
    }

    /// <summary>
    ///     The capture of 15 Nov 2026 as Claude Code sent it: no request is changed by the scan, each is counted as
    ///     the parser builds it, and the largest holds 554 messages and 14 records.
    /// </summary>
    [TestMethod]
    public void TheClaudeCodeCapture_IsUnchanged_AndFarBelowTheBounds()
    {
        var root = Path.Combine(ProcessRunner.RepositoryRoot, "tests", "Nami.IntegrationTests", "Fixtures", "ClaudeCodeOtlp");
        var largest = new OtlpRequestCounts(0, 0);
        var requests = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*.pb", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var sent = File.ReadAllBytes(file);
            var name = Path.GetFileName(file);
            MessageDescriptor descriptor = name.Contains("traces", StringComparison.Ordinal) ? ExportTraceServiceRequest.Descriptor
                : name.Contains("logs", StringComparison.Ordinal) ? ExportLogsServiceRequest.Descriptor
                : ExportMetricsServiceRequest.Descriptor;
            var scanned = sent.ToArray();

            Assert.AreEqual(default, OtlpValueBounds.BoundProtobuf(scanned, descriptor, s_limits, out var counted), file);
            Assert.AreSequenceEqual(sent, scanned, file);
            Assert.AreEqual(Count(descriptor.Parser.ParseFrom(sent)), counted, file);

            var json = File.ReadAllBytes(Path.ChangeExtension(file, ".json"));
            var scannedJson = json.ToArray();
            Assert.AreEqual(default, OtlpValueBounds.BoundJson(scannedJson, s_limits, out var inJson), file);
            Assert.AreSequenceEqual(json, scannedJson, file);
            Assert.AreEqual(counted, inJson, file);

            largest = new OtlpRequestCounts(Math.Max(largest.Messages, counted.Messages), Math.Max(largest.Records, counted.Records));
            requests++;
        }

        Assert.AreEqual(59, requests);
        Assert.AreEqual(new OtlpRequestCounts(554, 14), largest);
    }

    // An OTLP/JSON trace export of exactly records spans, each a row of its own.
    private static string SpansOf(int records) =>
        OtlpData.Traces(TestData.Nanos(DateTimeOffset.UtcNow.AddSeconds(-2)), records);

    // An OTLP/JSON trace export of exactly messages messages: the request, one resource with service.name, one
    // scope list, one span with its status, and attributes of two messages each (a KeyValue and its AnyValue); an
    // odd rest is one member without a value.
    private static string ExportOf(int messages)
    {
        var attributes = new JsonArray();
        var export = JsonNode.Parse(OtlpData.Traces(TestData.Nanos(DateTimeOffset.UtcNow.AddSeconds(-2)),
            attributes: attributes))!;
        var span = export["resourceSpans"]![0]!["scopeSpans"]![0]!["spans"]![0]!;
        var list = span["attributes"]!.AsArray();
        var missing = messages - CountObjects(export);
        Assert.IsGreaterThanOrEqualTo(0, missing);
        for (var i = 0; i < missing / 2; i++)
        {
            list.Add((JsonNode)new JsonObject
            {
                ["key"] = "k" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["value"] = new JsonObject { ["intValue"] = i.ToString(System.Globalization.CultureInfo.InvariantCulture) }
            });
        }

        if (missing % 2 == 1) list.Add((JsonNode)new JsonObject { ["key"] = "odd" });
        Assert.AreEqual(messages, CountObjects(export));
        return export.ToJsonString();
    }

    private static int CountObjects(JsonNode? node) => node switch
    {
        JsonObject value => 1 + value.Sum(static member => CountObjects(member.Value)),
        JsonArray items => items.Sum(CountObjects),
        _ => 0
    };

    // The messages of a parsed request (itself and every message its fields hold) and the records among them,
    // through the generated descriptors.
    private static OtlpRequestCounts Count(IMessage message)
    {
        var messages = 1;
        var records = message is Span or OpenTelemetry.Proto.Logs.V1.LogRecord or OpenTelemetry.Proto.Metrics.V1.NumberDataPoint
            or OpenTelemetry.Proto.Metrics.V1.HistogramDataPoint or OpenTelemetry.Proto.Metrics.V1.ExponentialHistogramDataPoint
            or OpenTelemetry.Proto.Metrics.V1.SummaryDataPoint or OpenTelemetry.Proto.Metrics.V1.Exemplar ? 1 : 0;
        foreach (var field in message.Descriptor.Fields.InDeclarationOrder())
        {
            if (field.FieldType is not FieldType.Message) continue;
            var value = field.Accessor.GetValue(message);
            var nested = field.IsRepeated ? ((IEnumerable)value).Cast<IMessage>() : value is IMessage one ? [one] : [];
            foreach (var inner in nested)
            {
                var counted = Count(inner);
                messages += counted.Messages;
                records += counted.Records;
            }
        }

        return new OtlpRequestCounts(messages, records);
    }
}
