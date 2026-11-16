using System.Net;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Nami.Api.Contracts.Common;
using Nami.Collector.Storage;
using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Logs.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Trace.V1;
using WireCapture = Nami.Tests.Infrastructure.ClaudeCodeCapture;

namespace Nami.IntegrationTests.ClaudeCode;

/// <summary>
///     Claude Code's own OTLP export, the scrubbed capture of finish-line item 6, through nami's OTLP/HTTP ingest:
///     each body is posted in arrival order as Claude Code's exporter posted it (<c>application/x-protobuf</c>) to a
///     collector composed like the engine (<see cref="CollectorHost" />), and the rows are read back from the closed
///     DuckDB file. Everything lands in project <c>default</c>: the capture names no nami project and ingest is
///     unsecured.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ClaudeCodeIngestTests(TestContext context)
{
    private const string Project = "default";

    // Unique strings: the sentinel stands for prompt, response, file and tool content, the detail marker for tool
    // details (commands, file paths, tool parameters, error text); the control goes into a key nami keeps, on the
    // same records.
    private const string Sentinel = "nami-sentinel-5b8e0c71";
    private const string Detail = "nami-detail-6a1f3e92";
    private const string Control = "nami-control-93d4a2f6";

    private const string TruncationMarker = "…[truncated]";

    private static readonly string[] s_contentColumns = ["logs.attributes_json", "logs.body", "spans.attributes_json", "spans.events_json"];


    /// <summary>
    ///     Claude Code copies the text of its <c>error</c> attribute into the status message of a failed span: "Shell
    ///     command failed" on the failing test run of s1, the API client's message on the three failed requests of the
    ///     capture without a credential. nami keeps the status code and the message of these spans; the captured
    ///     messages hold no credential and are shorter than 1024 bytes, so they are stored as sent.
    /// </summary>
    [TestMethod]
    [DataRow(ClaudeCodeCapture.SignedIn, 1)]
    [DataRow(ClaudeCodeCapture.NotSignedIn, 3)]
    public async Task Ingest_ClaudeCodeCapture_KeepsTheStatusCodeAndMessageOfAFailedSpan(string capture, int failed)
    {
        var exports = ClaudeCodeCapture.Load(capture);
        var sent = exports.Where(static export => export.Signal == Signals.Traces)
            .SelectMany(static export => ExportTraceServiceRequest.Parser.ParseFrom(export.Payload).ResourceSpans)
            .SelectMany(static resource => resource.ScopeSpans).SelectMany(static scope => scope.Spans)
            .Where(static span => span.Status is { Message.Length: > 0 }).Select(static span => span.Status.Message)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.HasCount(failed, sent, "the capture's spans with a status message");
        Assert.IsTrue(sent.All(static message => Encoding.UTF8.GetByteCount(message) < 1024));

        var path = await IngestAsync(exports);

        var stored = StoredRows.Rows(path, "SELECT status_code, status_message FROM spans WHERE project_id = $1", Project);
        Assert.HasCount(failed, stored.Where(static row => row[0] == "2"), "the failed spans keep STATUS_CODE_ERROR");
        Assert.AreSequenceEqual(sent,
            stored.Select(static row => row[1]).OfType<string>().Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    ///     Error text is kept through the sanitizer that exception text gets and up to 1024 UTF-8 bytes (observer's
    ///     ruling, 15 Nov): Claude Code's <c>error</c> attribute, and the span status message it copies that text
    ///     into. The failing test run of s1 gets an error that carries a bearer token, a password assignment and a URL
    ///     with user information and a query, followed by more text than fits, on its <c>tool_result</c> event, on
    ///     its <c>tool.execution</c> span and in that span's status message: the credentials reach no column of any
    ///     table, what was cut off neither, and the rest of the text is stored in all three places.
    /// </summary>
    [TestMethod]
    public async Task Ingest_CredentialsInClaudeCodeErrorText_ReachNoStoredRow()
    {
        const string secret = "nami-secret-8f2b61d0";
        const string tail = "nami-tail-5c0a97e4";
        var message = "Shell command failed: could not push\n" +
                      $"Authorization: Bearer {secret}-token\n" +
                      $"remote https://builder:{secret}-userinfo@git.example.test/nami.git?access={secret}-query rejected the push\n" +
                      $"password={secret}-password;retry in 5 s\n" +
                      new string('é', 1024) + tail;
        var (changedEvents, changedSpans) = (0, 0);
        var exports = ClaudeCodeCapture.Load(ClaudeCodeCapture.SignedIn).Select(export =>
        {
            switch (export.Signal)
            {
                case Signals.Logs:
                    {
                        var request = ExportLogsServiceRequest.Parser.ParseFrom(export.Payload);
                        foreach (var error in request.ResourceLogs.SelectMany(static resource => resource.ScopeLogs)
                                     .SelectMany(static scope => scope.LogRecords).SelectMany(static record => record.Attributes)
                                     .Where(static attribute => attribute.Key == "error"))
                        {
                            error.Value = WireCapture.Text(message);
                            changedEvents++;
                        }

                        return export with { Payload = request.ToByteArray() };
                    }
                case Signals.Traces:
                    {
                        var request = ExportTraceServiceRequest.Parser.ParseFrom(export.Payload);
                        foreach (var span in request.ResourceSpans.SelectMany(static resource => resource.ScopeSpans)
                                     .SelectMany(static scope => scope.Spans)
                                     .Where(static span => span.Status is { Message.Length: > 0 }))
                        {
                            span.Status.Message = message;
                            span.Attributes.Single(static attribute => attribute.Key == "error").Value = WireCapture.Text(message);
                            changedSpans++;
                        }

                        return export with { Payload = request.ToByteArray() };
                    }
                default:
                    return export;
            }
        }).ToArray();
        Assert.AreEqual(1, changedEvents, "the failed tool_result of s1");
        Assert.AreEqual(1, changedSpans, "the failed tool.execution span of s1");

        var path = await IngestAsync(exports);

        var failedSpan = Assert.ContainsSingle(StoredRows.Rows(path,
            "SELECT status_message, attributes_json FROM spans WHERE project_id = $1 AND status_message IS NOT NULL", Project));
        var failedResult = Assert.ContainsSingle(AttributesOfEvents(path, "tool_result")
            .Where(static json => Text(json, "error") is not null));
        Assert.IsEmpty(StoredRows.ColumnsContaining(path, secret), "no credential is stored");
        Assert.IsFalse(StoredRows.ColumnsContaining(path, tail).ContainsKey("spans.status_message"),
            "the part of the status message beyond 1024 bytes is not stored");
        foreach (var stored in new[] { failedSpan[0], Text(failedSpan[1]!, "error"), Text(failedResult, "error") })
        {
            Assert.IsNotNull(stored);
            Assert.StartsWith("Shell command failed: could not push\nAuthorization: Bearer [REDACTED]\n", stored);
            Assert.Contains("remote https://[REDACTED]@git.example.test/nami.git?[REDACTED] rejected the push\n", stored);
            Assert.Contains("password=[REDACTED];retry in 5 s\n", stored);
        }

        // The status message keeps its own 1024 bytes; the error attribute is far below the cap of every value.
        Assert.EndsWith(TruncationMarker, failedSpan[0]!);
        Assert.IsLessThanOrEqualTo(1024, Encoding.UTF8.GetByteCount(failedSpan[0]!));
        Assert.EndsWith(tail, Text(failedResult, "error")!);
    }

    /// <summary>
    ///     Content is stored as sent (observer's decision, 16 Nov 2026, which reverses finish-line item 5). The
    ///     sentinel goes into every content-bearing attribute and event Claude Code can send, and into the body of the
    ///     added content events (<see cref="ClaudeCodeContent" />); afterwards the store holds it on the events, the
    ///     spans and the span events it was planted on.
    /// </summary>
    [TestMethod]
    public async Task Ingest_ContentInEveryClaudeCodeContentCarrier_IsStored()
    {
        var path = await IngestPlantedCaptureAsync();

        var stored = StoredRows.ColumnsContaining(path, Sentinel);
        foreach (var column in s_contentColumns)
            Assert.IsGreaterThan(0L, stored.GetValueOrDefault(column), column + ": " + Describe(stored));
    }

    /// <summary>
    ///     The whole capture, record by record: every event and every span is stored with the attributes Claude Code
    ///     sent, under the keys and in the types it sent them, with the event-name field, the body and the span name
    ///     it sent. Nothing is renamed, converted, lifted, added or dropped. (The one addition is
    ///     <c>service.name</c>, which ingest copies from the resource onto every span.)
    /// </summary>
    [TestMethod]
    [DataRow(ClaudeCodeCapture.SignedIn, 77)]
    [DataRow(ClaudeCodeCapture.NotSignedIn, 18)]
    public async Task Ingest_ClaudeCodeCapture_StoresEveryEventAndSpanAsItWasSent(string capture, int events)
    {
        var exports = ClaudeCodeCapture.Load(capture);
        var sentEvents = exports.Where(static export => export.Signal == Signals.Logs)
            .SelectMany(static export => ExportLogsServiceRequest.Parser.ParseFrom(export.Payload).ResourceLogs)
            .SelectMany(static resource => resource.ScopeLogs).SelectMany(static scope => scope.LogRecords)
            .Select(static record => (record.EventName.Length == 0 ? "(no event name)" : record.EventName) + "\n" +
                                     Sent(record.Body) + "\n" + Sent(record.Attributes))
            .Order(StringComparer.Ordinal).ToArray();
        var sentSpans = exports.Where(static export => export.Signal == Signals.Traces)
            .SelectMany(static export => ExportTraceServiceRequest.Parser.ParseFrom(export.Payload).ResourceSpans)
            .SelectMany(static resource => resource.ScopeSpans).SelectMany(static scope => scope.Spans)
            .Select(static span => span.Name + "\n" + Sent(span.Attributes))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.HasCount(events, sentEvents);
        Assert.IsNotEmpty(sentSpans);

        var path = await IngestAsync(exports);

        var storedEvents = StoredRows.Rows(path, "SELECT event_name, body, attributes_json FROM logs WHERE project_id = $1", Project)
            .Select(static row => (row[0] ?? "(no event name)") + "\n" + Stored(StoredAttributeJson.Value(row[1])) + "\n" +
                                  Stored(row[2], null))
            .Order(StringComparer.Ordinal).ToArray();
        var storedSpans = StoredRows.Rows(path, "SELECT name, attributes_json FROM spans WHERE project_id = $1", Project)
            .Select(static row => row[0] + "\n" + Stored(row[1], "service.name"))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.AreSequenceEqual(sentEvents, storedEvents);
        Assert.AreSequenceEqual(sentSpans, storedSpans);
    }

    /// <summary>One line per attribute as Claude Code sent it: key, type and value, in key order.</summary>
    private static string Sent(IEnumerable<Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1.KeyValue> attributes) =>
        string.Join('\n', attributes.OrderBy(static attribute => attribute.Key, StringComparer.Ordinal)
            .Select(static attribute => attribute.Key + "=" + Sent(attribute.Value)));

    private static string Sent(Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1.AnyValue? value)
    {
        return value?.ValueCase switch
        {
            Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.StringValue => "string:" + value.StringValue,
            Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.BoolValue => "bool:" + value.BoolValue,
            Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.IntValue =>
                "int:" + value.IntValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.DoubleValue =>
                "double:" + value.DoubleValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.BytesValue =>
                "bytes:" + value.BytesValue.ToBase64(),
            Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.ArrayValue =>
                "[" + string.Join(",", value.ArrayValue.Values.Select(Sent)) + "]",
            Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.KvlistValue =>
                "{" + Sent(value.KvlistValue.Values).Replace('\n', ',') + "}",
            _ => "empty"
        };
    }

    /// <summary>The same lines for a stored attribute column; <paramref name="added" /> names a key ingest adds.</summary>
    private static string Stored(string? attributesJson, string? added) =>
        string.Join('\n', StoredAttributeJson.Attributes(attributesJson).Where(attribute => attribute.Key != added)
            .Select(static attribute => attribute.Key + "=" + Stored(attribute.Value)));

    private static string Stored(AttributeValue? value)
    {
        return value switch
        {
            null => "empty",
            AttributeValue.StringValue text => "string:" + text.Value,
            AttributeValue.BoolValue flag => "bool:" + flag.Value,
            AttributeValue.ArrayValue list => "[" + string.Join(",", list.Value.Select(Stored)) + "]",
            AttributeValue.ObjectValue item => item.Value switch
            {
                AttributeIntValue number => "int:" + number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                AttributeDoubleValue number =>
                    "double:" + number.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                AttributeBytesValue bytes => "bytes:" + Convert.ToBase64String(bytes.Base64.Span),
                AttributeKeyValueListValue map => "{" + string.Join(",", map.Values
                    .OrderBy(static member => member.Key, StringComparer.Ordinal)
                    .Select(static member => member.Key + "=" + Stored(member.Value))) + "}",
                _ => "other"
            }
        };
    }

    /// <summary>
    ///     The stored attributes of the events Claude Code names <paramref name="eventName" /> in their
    ///     <c>event.name</c> attribute, one JSON object each. The native event-name column holds what the record's
    ///     field held, which Claude Code leaves empty.
    /// </summary>
    private static string[] AttributesOfEvents(string path, string eventName)
    {
        return
        [
            .. StoredRows.Rows(path,
                    "SELECT attributes_json FROM logs WHERE project_id = $1 AND contains(attributes_json, $2)", Project,
                    "\"event.name\":\"" + eventName + "\"")
                .Select(static row => row[0] ?? throw new InvalidDataException("An event without attributes."))
        ];
    }

    private static string? Text(string attributesJson, string key) =>
        (StoredAttributeJson.Attribute(attributesJson, key) as AttributeValue.StringValue)?.Value;

    /// <summary>
    ///     Ingests the signed-in capture with content and tool details planted and returns the closed DuckDB file. The
    ///     control marker on the same records shows that each of them was stored and that the scan reads the columns
    ///     that hold their attributes.
    /// </summary>
    private async Task<string> IngestPlantedCaptureAsync()
    {
        var planted = ClaudeCodeContent.Plant(ClaudeCodeCapture.Load(ClaudeCodeCapture.SignedIn),
            new ContentMarkers(Sentinel, Detail, Control));
        var path = await IngestAsync(planted.Exports);

        var kept = StoredRows.ColumnsContaining(path, Control);
        Assert.AreEqual((long)planted.Logs, kept.GetValueOrDefault("logs.attributes_json"), Describe(kept));
        Assert.AreEqual((long)planted.Spans, kept.GetValueOrDefault("spans.attributes_json"), Describe(kept));
        Assert.AreEqual((long)planted.ToolOutputs, kept.GetValueOrDefault("spans.events_json"), Describe(kept));
        return path;
    }

    /// <summary>Posts every body to a fresh collector, stops it, and returns its closed DuckDB file.</summary>
    private async Task<string> IngestAsync(IReadOnlyList<ClaudeCodeExport> exports)
    {
        var cancellationToken = context.CancellationToken;
        var directory = context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");
        await using var host = await CollectorHost.StartAsync(directory, cancellationToken);
        foreach (var export in exports)
        {
            using var response = await OtlpHttp.PostAsync(host.Endpoint, export.Payload, cancellationToken,
                export.Signal);
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, Encoding.UTF8.GetString(body));
            Assert.AreEqual(0L, Signals.PartialSuccess(export.Signal, body)?.Rejected ?? 0L,
                Signals.PartialSuccess(export.Signal, body)?.Message);
        }

        await host.StopAsync(cancellationToken);
        return host.DataPath;
    }

    private static string Describe(IReadOnlyDictionary<string, long> counts)
    {
        return string.Join(", ", counts.Select(static count => $"{count.Key}={count.Value}"));
    }
}
