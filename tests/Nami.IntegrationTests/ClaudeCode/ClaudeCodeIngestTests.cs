using System.Net;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Nami.Api.Contracts.Common;
using Nami.Collector.Storage;
using Nami.SemanticConventions;
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

    private const string TruncationMarker = "...[truncated]";


    /// <summary>
    ///     Claude Code names each event only in the <c>event.name</c> attribute (<c>tool_result</c>) and in the body
    ///     (<c>claude_code.tool_result</c>); the OTLP <c>event_name</c> field is empty on all 95 records of both captures.
    ///     nami stores every one of them (77 and 18: <c>event.sequence</c> and <c>plugin.name</c> are persisted, so the
    ///     <c>plugin_loaded</c> events of one millisecond no longer share a row) under the name the registry defines,
    ///     <c>claude_code.{name}</c>, and the stored body is that name.
    /// </summary>
    [TestMethod]
    [DataRow(ClaudeCodeCapture.SignedIn, 77)]
    [DataRow(ClaudeCodeCapture.NotSignedIn, 18)]
    public async Task Ingest_ClaudeCodeCapture_StoresEveryEventWithItsName(string capture, int events)
    {
        var path = await IngestAsync(ClaudeCodeCapture.Load(capture));

        var stored = StoredRows.EventNames(path, Project);

        var expected = ExpectedEvents(capture);
        Assert.AreEqual(events, expected.Values.Sum());
        Assert.AreSequenceEqual(expected.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(Count).ToArray(),
            stored.Select(static pair => Count(KeyValuePair.Create(pair.Key, (int)pair.Value))).ToArray(), Describe(stored));
        var defined = Events.All.Select(static definition => definition.Name).ToHashSet(StringComparer.Ordinal);
        Assert.IsEmpty(stored.Keys.Where(name => !defined.Contains(name)).ToArray(), "every stored name is a registry event");
        var bodies = StoredRows.Rows(path, "SELECT event_name, body FROM logs WHERE project_id = $1", Project);
        Assert.HasCount(events, bodies);
        foreach (var row in bodies)
            Assert.AreEqual(row[0], (StoredAttributeJson.Value(row[1]) as AttributeValue.StringValue)?.Value, "the stored body");

        static string Count(KeyValuePair<string, int> pair) => $"{pair.Key}={pair.Value}";
    }

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
        var failedResult = Assert.ContainsSingle(AttributesOfEvents(path, "claude_code.tool_result")
            .Where(static json => Text(json, "error") is not null));
        Assert.IsEmpty(StoredRows.ColumnsContaining(path, secret), "no credential is stored");
        Assert.IsEmpty(StoredRows.ColumnsContaining(path, tail), "the part beyond 1024 bytes is stored nowhere");
        foreach (var stored in new[] { failedSpan[0], Text(failedSpan[1]!, "error"), Text(failedResult, "error") })
        {
            Assert.IsNotNull(stored);
            Assert.StartsWith("Shell command failed: could not push\nAuthorization: Bearer [REDACTED]\n", stored);
            Assert.Contains("remote https://git.example.test/nami.git rejected the push\n", stored);
            Assert.Contains("password=[REDACTED];retry in 5 s\n", stored);
            Assert.EndsWith(TruncationMarker, stored);
            Assert.IsLessThanOrEqualTo(1024, Encoding.UTF8.GetByteCount(stored));
        }
    }

    /// <summary>
    ///     Finish-line item 5: nami stores no prompt or response text, even when a session sends it. The sentinel goes
    ///     into every content-bearing attribute and event Claude Code can send, and into the body of the added
    ///     content events (<see cref="ClaudeCodeContent" />), and no column of any table may hold it afterwards,
    ///     whatever the registry says.
    /// </summary>
    [TestMethod]
    public async Task Ingest_ContentInEveryClaudeCodeContentCarrier_ReachesNoStoredRow()
    {
        var path = await IngestPlantedCaptureAsync();

        var leaked = StoredRows.ColumnsContaining(path, Sentinel);
        Assert.IsEmpty(leaked, "Content reached storage: " + Excerpts(path, leaked, Sentinel));
    }

    /// <summary>The capture's log records per event, under the registry's names.</summary>
    private static Dictionary<string, int> ExpectedEvents(string capture)
    {
        return capture switch
        {
            ClaudeCodeCapture.SignedIn => new Dictionary<string, int>
            {
                ["claude_code.api_request"] = 17, ["claude_code.assistant_response"] = 7,
                ["claude_code.managed_settings_resolved"] = 3, ["claude_code.plugin_loaded"] = 13,
                ["claude_code.subagent_completed"] = 1, ["claude_code.tool_decision"] = 16,
                ["claude_code.tool_result"] = 16, ["claude_code.user_prompt"] = 4
            },
            ClaudeCodeCapture.NotSignedIn => new Dictionary<string, int>
            {
                ["claude_code.api_error"] = 3, ["claude_code.managed_settings_resolved"] = 2,
                ["claude_code.plugin_loaded"] = 11, ["claude_code.user_prompt"] = 2
            },
            _ => throw new ArgumentOutOfRangeException(nameof(capture), capture, "Unknown capture.")
        };
    }

    /// <summary>The stored attributes of the events named <paramref name="eventName" />, one JSON object each.</summary>
    private static string[] AttributesOfEvents(string path, string eventName)
    {
        return
        [
            .. StoredRows.Rows(path, "SELECT attributes_json FROM logs WHERE project_id = $1 AND event_name = $2", Project,
                eventName).Select(static row => row[0] ?? throw new InvalidDataException("An event without attributes."))
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

    private static string Excerpts(string path, IReadOnlyDictionary<string, long> columns, string marker)
    {
        return string.Join("; ", columns.Select(column =>
            $"{column.Key} in {column.Value} rows, for example {string.Join(" | ", StoredRows.Excerpts(path, column.Key, marker))}"));
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
