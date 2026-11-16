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

    // The passing test run of session s1, a Bash call with tool_parameters on its two events and full_command on its span.
    private const string PassingTestRun = "toolu_01FIXTURE000000000000004";
    private const string TruncationMarker = "...[truncated]";

    private static readonly string[] s_persistedDetails = ["bash_command", "error", "file_path", "full_command"];
    private static readonly string[] s_droppedDetails = ["tool_parameters"];
    private static readonly string[] s_liftedMembers = ["bash_command", "full_command", "subagent_type"];
    private static readonly string[] s_droppedMembers = ["tool_parameters", "description", "git_commit_id", "git_branch", "tool_input"];
    private static readonly string[] s_toolEvents = ["claude_code.tool_decision", "claude_code.tool_result"];
    private static readonly string[] s_detailColumns = ["logs.attributes_json", "spans.attributes_json", "spans.events_json"];

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
    ///     Claude Code nests <c>bash_command</c>, <c>full_command</c> and <c>subagent_type</c> in the
    ///     <c>tool_parameters</c> JSON of <c>tool_decision</c> and <c>tool_result</c>. Each stored event carries them as
    ///     its own attributes, with the capture's values, and neither the container nor any other member of it.
    /// </summary>
    [TestMethod]
    public async Task Ingest_ClaudeCodeCapture_LiftsTheNamedMembersOutOfToolParameters()
    {
        var path = await IngestAsync(ClaudeCodeCapture.Load(ClaudeCodeCapture.SignedIn));

        var sent = SentToolParameters();
        Assert.HasCount(14, sent, "7 tool_decision and 7 tool_result events of the capture carry tool_parameters");
        foreach (var name in s_toolEvents)
        {
            var stored = AttributesOfEvents(path, name);
            Assert.HasCount(16, stored, name);
            Assert.IsEmpty(stored.SelectMany(Keys).Where(s_droppedMembers.Contains).Distinct().ToArray(), name);
            foreach (var json in stored)
            {
                var call = Text(json, "tool_use_id") ?? throw new InvalidDataException("A tool event without tool_use_id.");
                foreach (var key in s_liftedMembers)
                {
                    var expected = sent.TryGetValue((name, call), out var members) && members.TryGetProperty(key, out var member)
                        ? member.GetString()
                        : null;
                    Assert.AreEqual(expected, Text(json, key), $"{name} {call} {key}");
                }
            }

            Assert.HasCount(6, stored.Where(static json => Text(json, "full_command") is not null), name);
            Assert.HasCount(6, stored.Where(static json => Text(json, "bash_command") is not null), name);
            Assert.HasCount(1, stored.Where(static json => Text(json, "subagent_type") == "general-purpose"), name);
        }

        var spans = StoredRows.Rows(path,
            "SELECT attributes_json FROM spans WHERE project_id = $1 AND name = 'claude_code.tool'", Project);
        Assert.HasCount(16, spans);
        Assert.HasCount(6, spans.Where(static row => Text(row[0]!, "full_command") is not null),
            "the six Bash calls keep their command line on the tool span");
    }

    /// <summary>
    ///     <c>full_command</c> is persisted and bounded (observer's ruling, 15 Nov): a command line longer than the
    ///     registry's bound of 4096 UTF-8 bytes is stored cut to it, with the marker log bodies use, on both events and
    ///     on the span, and what was cut off reaches no column of any table.
    /// </summary>
    [TestMethod]
    public async Task Ingest_FullCommandBeyondTheRegistrysBound_IsStoredCutToIt()
    {
        var bound = ValueLimits.MaxUtf8Bytes["full_command"];
        const string start = "./eng/test.sh --suite Unit # ";
        const string tail = "nami-tail-4d7e19c3";
        var command = start + new string('é', bound) + tail;
        var changed = WireCapture.WithCommand(WireCapture.SignedIn(), PassingTestRun, command);

        var path = await IngestAsync([.. changed.Select(static body => new ClaudeCodeExport(body.Signal, body.Payload))]);

        var events = StoredRows.Rows(path,
            "SELECT attributes_json FROM logs WHERE project_id = $1 AND contains(attributes_json, $2)", Project, PassingTestRun);
        var spans = StoredRows.Rows(path,
            "SELECT attributes_json FROM spans WHERE project_id = $1 AND name = 'claude_code.tool' AND contains(attributes_json, $2)",
            Project, PassingTestRun);
        Assert.HasCount(2, events, "the tool_decision and the tool_result of the run");
        Assert.HasCount(1, spans);
        var expected = start + new string('é', (bound - Encoding.UTF8.GetByteCount(start) - TruncationMarker.Length) / 2) +
                       TruncationMarker;
        foreach (var row in events.Concat(spans))
        {
            var stored = Text(row[0]!, "full_command");
            Assert.AreEqual(expected, stored);
            Assert.IsLessThanOrEqualTo(bound, Encoding.UTF8.GetByteCount(stored!));
        }

        Assert.IsEmpty(StoredRows.ColumnsContaining(path, tail), "the part beyond the bound is stored nowhere");
        Assert.HasCount(12, StoredRows.Rows(path,
                "SELECT 1 FROM logs WHERE project_id = $1 AND contains(attributes_json, '\"full_command\"')", Project),
            "all twelve events of the six Bash calls keep a command line");
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
    ///     Tool details (the Bash command in <c>tool_parameters</c> and <c>full_command</c>, file paths, full error
    ///     text) may be stored only under a key the registry marks persisted: a mapping must not keep one on its own.
    ///     Finish-line item 4 needs the command, so this is the registry's decision. Since registry 0.4.0 it persists
    ///     <c>bash_command</c>, <c>file_path</c>, <c>full_command</c> and the <c>error</c> text, and not
    ///     <c>tool_parameters</c> as a whole: the first four are stored where they were planted, in the attributes of
    ///     events, spans and span events and in no other column, and the container reaches no stored row.
    /// </summary>
    [TestMethod]
    public async Task Ingest_ClaudeCodeToolDetails_ReachNoStoredRowUnlessTheRegistryPersistsTheKey()
    {
        var path = await IngestPlantedCaptureAsync();

        Assert.AreSequenceEqual(s_persistedDetails, ClaudeCodeContent.DetailKeys.Where(RegistryPolicy.IsPersisted).ToArray());
        Assert.AreSequenceEqual(s_droppedDetails,
            ClaudeCodeContent.DetailKeys.Where(static key => !RegistryPolicy.IsPersisted(key)).ToArray());
        foreach (var key in ClaudeCodeContent.DetailKeys)
        {
            var marker = Detail + ":" + key;
            var stored = StoredRows.ColumnsContaining(path, marker);
            if (RegistryPolicy.IsPersisted(key))
            {
                Assert.IsNotEmpty(stored, key + " is persisted, so its planted value is stored");
                Assert.IsEmpty(stored.Keys.Except(s_detailColumns).ToArray(),
                    $"{key} is stored outside the attributes it was sent in: " + Excerpts(path, stored, marker));
            }
            else
            {
                Assert.IsEmpty(stored,
                    $"{key} reached storage although the registry does not persist it: " + Excerpts(path, stored, marker));
            }
        }
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

    /// <summary>
    ///     What Claude Code sent in <c>tool_parameters</c>, by the registry's event name and <c>tool_use_id</c>, read
    ///     from the capture's bodies.
    /// </summary>
    private static Dictionary<(string Event, string ToolUseId), JsonElement> SentToolParameters()
    {
        var sent = new Dictionary<(string, string), JsonElement>();
        foreach (var export in ClaudeCodeCapture.Load(ClaudeCodeCapture.SignedIn).Where(static e => e.Signal == Signals.Logs))
        {
            foreach (var record in ExportLogsServiceRequest.Parser.ParseFrom(export.Payload).ResourceLogs
                         .SelectMany(static resource => resource.ScopeLogs).SelectMany(static scope => scope.LogRecords))
            {
                if (WireCapture.Text(record.Attributes, "tool_parameters") is not { } parameters) continue;
                using var document = JsonDocument.Parse(parameters);
                sent.Add(("claude_code." + WireCapture.Text(record.Attributes, "event.name"),
                    WireCapture.Text(record.Attributes, "tool_use_id")!), document.RootElement.Clone());
            }
        }

        return sent;
    }

    private static IEnumerable<string> Keys(string attributesJson) =>
        StoredAttributeJson.Attributes(attributesJson).Select(static attribute => attribute.Key);

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
