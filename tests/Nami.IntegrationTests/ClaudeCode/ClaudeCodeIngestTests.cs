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

    // What the capture stores: the four primitive types, the empty lists of managed_settings.sources and the string
    // lists of gen_ai.response.finish_reasons on the 17 llm_request spans.
    private static readonly string[] s_storedKinds = ["[]", "boolean", "double", "int", "string", "string[]"];
    private static readonly string[] s_persistedDetails = ["bash_command", "file_path", "full_command"];
    private static readonly string[] s_droppedDetails = ["error", "tool_parameters"];
    private static readonly string[] s_liftedMembers = ["bash_command", "full_command", "subagent_type"];
    private static readonly string[] s_droppedMembers = ["tool_parameters", "description", "git_commit_id", "git_branch", "tool_input"];
    private static readonly string[] s_toolEvents = ["claude_code.tool_decision", "claude_code.tool_result"];
    private static readonly string[] s_detailColumns = ["spans.attributes_json", "spans.events_json"];
    private static readonly string[] s_attributeTables = ["logs", "spans", "metric_series"];

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
    ///     A type in the registry is the type nami stores. Every attribute of every stored event, span, span event and
    ///     metric series of the capture has the type its definition declares: among them the 93 values Claude Code
    ///     sends as strings (<c>success</c>, <c>duration_ms</c> and the two sizes on <c>tool_result</c>,
    ///     <c>prompt_length</c>, <c>safe_mode</c>), which the registry marks <c>nami.wire_type: string</c>.
    /// </summary>
    [TestMethod]
    public async Task Ingest_ClaudeCodeCapture_StoresEveryValueInTheTypeTheRegistryDeclares()
    {
        var path = await IngestAsync(ClaudeCodeCapture.Load(ClaudeCodeCapture.SignedIn));

        var mismatches = new List<string>();
        var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (source, json) in StoredAttributes(path))
        {
            foreach (var (key, value) in StoredAttributeJson.Attributes(json))
            {
                var definition = AttributeCatalog.Find(key);
                Assert.IsNotNull(definition, key + " is stored, so the registry defines it");
                var stored = Kind(value);
                kinds[stored] = kinds.GetValueOrDefault(stored) + 1;
                if (!IsStoredAs(definition.Type, stored))
                    mismatches.Add($"{source} {key}: declared {definition.Type}, stored as {stored}");
            }
        }

        Assert.IsEmpty(mismatches, string.Join(Environment.NewLine, mismatches.Distinct(StringComparer.Ordinal)));
        Assert.AreSequenceEqual(s_storedKinds, kinds.Keys.ToArray(), "the capture exercises all four primitive types");

        // The marked values, by event: each of the 16 tool results, 4 prompts and 13 loaded plugins of the signed-in
        // capture holds them in the declared type.
        var results = AttributesOfEvents(path, "claude_code.tool_result");
        Assert.HasCount(16, results);
        foreach (var json in results)
        {
            Assert.AreEqual("boolean", Kind(StoredAttributeJson.Attribute(json, "success")));
            Assert.AreEqual("int", Kind(StoredAttributeJson.Attribute(json, "duration_ms")));
            Assert.AreEqual("int", Kind(StoredAttributeJson.Attribute(json, "tool_input_size_bytes")));
        }

        Assert.HasCount(15, results.Where(static json => Kind(StoredAttributeJson.Attribute(json, "tool_result_size_bytes")) == "int"));
        Assert.HasCount(1, results.Where(static json => StoredAttributeJson.Attribute(json, "success") is AttributeValue.BoolValue { Value: false }),
            "the failing test run of s1");
        var prompts = AttributesOfEvents(path, "claude_code.user_prompt");
        Assert.HasCount(4, prompts);
        Assert.IsTrue(prompts.All(static json => Kind(StoredAttributeJson.Attribute(json, "prompt_length")) == "int"));
        var plugins = AttributesOfEvents(path, "claude_code.plugin_loaded");
        Assert.HasCount(13, plugins);
        Assert.IsTrue(plugins.All(static json => Kind(StoredAttributeJson.Attribute(json, "safe_mode")) == "boolean"));
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
    ///     A span status message is error text: nami keeps a Claude Code span's through the sanitizer that exception
    ///     text gets and up to 1024 UTF-8 bytes (observer's ruling, 15 Nov). The failing test run of s1 gets a status
    ///     message that carries a bearer token, a password assignment and a URL with user information and a query,
    ///     followed by more text than fits: the credentials reach no column of any table, what was cut off neither,
    ///     and the rest of the message is stored on the span.
    /// </summary>
    [TestMethod]
    public async Task Ingest_CredentialsInAClaudeCodeSpanStatusMessage_ReachNoStoredRow()
    {
        const string secret = "nami-secret-8f2b61d0";
        const string tail = "nami-tail-5c0a97e4";
        var message = "Shell command failed: could not push\n" +
                      $"Authorization: Bearer {secret}-token\n" +
                      $"remote https://builder:{secret}-userinfo@git.example.test/nami.git?access={secret}-query rejected the push\n" +
                      $"password={secret}-password;retry in 5 s\n" +
                      new string('é', 1024) + tail;
        var exports = ClaudeCodeCapture.Load(ClaudeCodeCapture.SignedIn).Select(export =>
        {
            if (export.Signal != Signals.Traces) return export;
            var request = ExportTraceServiceRequest.Parser.ParseFrom(export.Payload);
            foreach (var span in request.ResourceSpans.SelectMany(static resource => resource.ScopeSpans)
                         .SelectMany(static scope => scope.Spans).Where(static span => span.Status is { Message.Length: > 0 }))
                span.Status.Message = message;
            return export with { Payload = request.ToByteArray() };
        }).ToArray();

        var path = await IngestAsync(exports);

        var stored = Assert.ContainsSingle(StoredRows.Rows(path,
            "SELECT status_message FROM spans WHERE project_id = $1 AND status_message IS NOT NULL", Project))[0]!;
        Assert.IsEmpty(StoredRows.ColumnsContaining(path, secret), "no credential is stored: " + stored);
        Assert.IsEmpty(StoredRows.ColumnsContaining(path, tail), "the part beyond 1024 bytes is stored nowhere");
        Assert.StartsWith("Shell command failed: could not push\nAuthorization: Bearer [REDACTED]\n", stored);
        Assert.Contains("remote https://git.example.test/nami.git rejected the push\n", stored);
        Assert.Contains("password=[REDACTED];retry in 5 s\n", stored);
        Assert.EndsWith(TruncationMarker, stored);
        Assert.IsLessThanOrEqualTo(1024, Encoding.UTF8.GetByteCount(stored));
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

    /// <summary>
    ///     Tool details (the Bash command in <c>tool_parameters</c> and <c>full_command</c>, file paths, full error
    ///     text) may be stored only under a key the registry marks persisted: a mapping must not keep one on its own.
    ///     Finish-line item 4 needs the command, so this is the registry's decision. Since registry 0.4.0 it persists
    ///     <c>bash_command</c>, <c>file_path</c> and <c>full_command</c> and not <c>tool_parameters</c> as a whole or
    ///     the <c>error</c> text: the first three are stored where they were planted, in the attributes of spans and
    ///     span events and in no other column, and the other two reach no stored row.
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

    /// <summary>Every stored attribute object of the project: events, spans, span events and links, metric series.</summary>
    private static IEnumerable<(string Source, string Json)> StoredAttributes(string path)
    {
        foreach (var table in s_attributeTables)
        {
            foreach (var row in StoredRows.Rows(path,
                         $"SELECT attributes_json FROM {table} WHERE project_id = $1 AND attributes_json IS NOT NULL", Project))
                yield return (table, row[0]!);
        }

        foreach (var row in StoredRows.Rows(path,
                     "SELECT events_json, links_json FROM spans WHERE project_id = $1", Project))
        {
            foreach (var (column, json) in new[] { ("spans.events", row[0]), ("spans.links", row[1]) })
            {
                if (json is null) continue;
                using var children = JsonDocument.Parse(json);
                foreach (var child in children.RootElement.EnumerateArray())
                    if (child.TryGetProperty("attributesJson", out var attributes) && attributes.GetString() is { } text)
                        yield return (column, text);
            }
        }
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

    /// <summary>The type of a stored value, in the registry's words; an empty list is <c>[]</c>.</summary>
    private static string Kind(AttributeValue? value)
    {
        return value switch
        {
            AttributeValue.StringValue => "string",
            AttributeValue.BoolValue => "boolean",
            AttributeValue.ArrayValue array => string.Join('|', array.Value.Select(Kind).Distinct(StringComparer.Ordinal)) + "[]",
            AttributeValue.ObjectValue item => item.Value switch
            {
                AttributeIntValue => "int",
                AttributeDoubleValue => "double",
                _ => "other"
            },
            null => "empty"
        };
    }

    /// <summary>
    ///     Whether a value of kind <paramref name="stored" /> has the registry type: an enum is stored as its string, a
    ///     template as its member type, and an empty list fits every list type.
    /// </summary>
    private static bool IsStoredAs(string registryType, string stored)
    {
        var declared = registryType switch
        {
            "enum" => "string",
            _ when registryType.StartsWith("template[", StringComparison.Ordinal) => registryType[9..^1],
            _ => registryType
        };
        return stored == declared || (stored == "[]" && declared.EndsWith("[]", StringComparison.Ordinal));
    }

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
