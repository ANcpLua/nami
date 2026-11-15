using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Nami.Collector.Ingestion;
using Nami.Collector.Storage;
using Nami.SemanticConventions;
using Nami.Tests.Infrastructure;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Nami.IntegrationTests;

/// <summary>
///     Holds the registry's Claude Code definitions against the scrubbed capture of what Claude Code
///     2.1.325 sent on 15 Nov 2026: every key it sent is defined or deliberately left unknown, every
///     signal is defined, each signal declares the keys it carried, the declared wire types are the
///     ones on the wire, the content keys of finish-line item 5 are never persisted, and the capture
///     ingested through the converter and the store keeps the evidence and none of the content.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ClaudeCodeRegistryTests(TestContext context)
{
    private const string Producer = "claude_code";
    private const string ToolParameters = "tool_parameters";

    // Keys Claude Code sends that the registry leaves undefined, so ingest drops them: identity in
    // OpenTelemetry's user namespace, a copy of the record's timestamp, the link type of the
    // llm_request span, and GenAI's excluded predecessor of gen_ai.provider.name.
    private static readonly string[] s_undefined =
        ["event.timestamp", "gen_ai.system", "link.type", "user.account_id", "user.account_uuid"];

    // Signals the monitoring documentation describes but the capture does not contain.
    private static readonly string[] s_documentedOnly =
    [
        "claude_code.api_request_body", "claude_code.api_response_body", "claude_code.pull_request.count",
        "claude_code.system_prompt"
    ];

    // Members of tool_parameters that ingest drops: the model-written description, the command line,
    // which can carry free text, and the commit, which vcs.ref.head.revision and .name carry.
    private static readonly string[] s_droppedParameters = ["description", "full_command", "git_branch", "git_commit_id"];

    // Finish-line item 5: prompt and response text, tool input, raw bodies and tool content.
    private static readonly string[] s_content =
    [
        "body", "body_ref", "content", "diff", "error", "full_command", "gen_ai.input.messages", "gen_ai.output.messages",
        "gen_ai.system_instructions", "gen_ai.tool.call.arguments", "gen_ai.tool.call.result", "new_context",
        "output", "prompt", "prompt_text", "response", "response.model_output", "system_prompt_preview",
        "system_reminders", "tool_input", "tool_parameters", "user_prompt", "user_system_prompt"
    ];

    // Identity and sender setup that Claude Code puts on every record.
    private static readonly string[] s_identity =
        ["anthropic.custom_base_url", "anthropic.plan", "organization.id", "terminal.type", "user.email", "user.id"];

    // What M2 derives sessions, agents, cost, tokens, tool calls, commands and commits from.
    private static readonly string[] s_evidence =
    [
        "agent_id", "bash_command", "cache_creation_tokens", "cache_read_tokens", "cost_usd", "cost_usd_micros",
        "duration_ms", "effort", "error_type", "event.sequence", "gen_ai.agent.name",
        "input_tokens", "model", "output_tokens", "prompt.id", "query_source", "session.id", "success",
        "tool_name", "tool_use_id", "vcs.ref.head.revision"
    ];

    private static string Root => ProcessRunner.RepositoryRoot;

    private static readonly Lazy<Capture> s_capture = new(() =>
        Capture.Load(Path.Combine(Root, "tests/Nami.IntegrationTests/Fixtures/ClaudeCodeOtlp")));

    private static readonly Lazy<RegistrySignals> s_signals = new(() =>
        RegistrySignals.Load(Path.Combine(Root, "registry/resolved.json")));

    [TestMethod]
    public void Capture_EveryKey_IsDefinedOrDeliberatelyUndefined()
    {
        var keys = s_capture.Value.Values.Select(static value => value.Key).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.IsGreaterThan(100, keys.Length);
        var unknown = keys.Where(static key => AttributeCatalog.Find(key) is null && !s_undefined.Contains(key))
            .ToArray();
        Assert.IsEmpty(unknown, "Sent by Claude Code, not defined: " + string.Join(", ", unknown));
        foreach (var key in s_undefined)
        {
            Assert.IsNull(AttributeCatalog.Find(key), key + " is meant to stay undefined");
            Assert.Contains(key, keys, key + " no longer occurs in the capture; drop it from the list");
        }
    }

    [TestMethod]
    public void Capture_EverySignal_IsDefined_AndEveryDefinitionIsCaptured()
    {
        var capture = s_capture.Value;
        var metrics = Metrics.All.Select(static metric => metric.Name).ToHashSet(StringComparer.Ordinal);
        var events = Events.All.Select(static definition => definition.Name).ToHashSet(StringComparer.Ordinal);
        var spans = Spans.All.Select(static span => span.Type).ToHashSet(StringComparer.Ordinal);
        foreach (var signal in capture.Signals)
        {
            var defined = signal.Kind switch
            {
                SignalKind.Metric => metrics,
                SignalKind.Event => events,
                _ => spans
            };
            Assert.Contains(signal.Name, defined, signal.Kind + " " + signal.Name + " is not defined");
        }

        Assert.IsTrue(Producers.Scopes.TryGetValue(Producer, out var scopes));
        Assert.AreSequenceEqual(capture.Scopes, scopes.Order(StringComparer.Ordinal));
        var captured = capture.Signals.Select(static signal => signal.Name).ToHashSet(StringComparer.Ordinal);
        var uncaptured = s_signals.Value.ProducerSignals(Producer).Where(name => !captured.Contains(name))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.AreSequenceEqual(s_documentedOnly, uncaptured);
        Assert.HasCount(8, s_signals.Value.ProducerSignals(Producer).Where(metrics.Contains));
        Assert.HasCount(12, s_signals.Value.ProducerSignals(Producer).Where(events.Contains));
        Assert.HasCount(5, s_signals.Value.ProducerSignals(Producer).Where(spans.Contains));
    }

    [TestMethod]
    public void Capture_SignalAttributes_AreDeclaredOnTheirSignal_AndRequiredOnesAlwaysSent()
    {
        var registry = s_signals.Value;
        var missing = new List<string>();
        foreach (var signal in s_capture.Value.Signals)
        {
            var declared = registry.Attributes(signal.Name);
            foreach (var key in signal.Records.SelectMany(static record => record.Keys).Distinct(StringComparer.Ordinal))
            {
                // Claude Code's event name; ingest moves it into the record's EventName field.
                if (signal.Kind == SignalKind.Event && key == "event.name") continue;
                if (s_undefined.Contains(key)) continue;
                if (!declared.ContainsKey(key)) missing.Add(signal.Name + ": " + key);
            }

            foreach (var (key, attribute) in declared)
            {
                if (attribute.RequirementLevel != "required") continue;
                var without = signal.Records.Count(record => !record.ContainsKey(key));
                if (without > 0) missing.Add(signal.Name + ": required " + key + " absent from " + without + " records");
            }
        }

        Assert.IsEmpty(missing, string.Join(Environment.NewLine, missing));
    }

    [TestMethod]
    public void Capture_WireTypes_AreTheDeclaredOnes_AndConvert()
    {
        var registry = s_signals.Value;
        var mismatches = new List<string>();
        var conversions = 0;
        foreach (var value in s_capture.Value.Values)
        {
            if (s_undefined.Contains(value.Key) || value.Key == "event.name") continue;
            var definition = AttributeCatalog.Find(value.Key);
            Assert.IsNotNull(definition, value.Key);
            var wire = value.Signal is null ? null : registry.Attributes(value.Signal).GetValueOrDefault(value.Key)?.WireType;
            var expected = wire ?? definition.Type;
            if (!HasWireType(value.Value, expected))
            {
                mismatches.Add($"{value.Signal ?? value.Context} {value.Key}: sent {value.Value}, declared {expected}");
                continue;
            }

            if (wire is null) continue;
            conversions++;
            if (!Converts(value.Value.GetProperty("stringValue").GetString()!, definition.Type))
                mismatches.Add($"{value.Signal} {value.Key}: {value.Value} does not convert to {definition.Type}");
        }

        Assert.IsEmpty(mismatches, string.Join(Environment.NewLine, mismatches));
        // success, duration_ms and both sizes on 16 tool results, prompt_length on 6 prompts, safe_mode on 24 plugins.
        Assert.AreEqual(16 * 4 - 1 + 6 + 24, conversions);
    }

    [TestMethod]
    public void Capture_ToolParametersMembers_AreLiftedOrDropped()
    {
        Assert.IsTrue(WireContainers.Members.TryGetValue(ToolParameters, out var lifted));
        Assert.AreSequenceEqual(["bash_command", "subagent_type"], lifted);
        Assert.IsFalse(RegistryPolicy.IsPersisted(ToolParameters));
        foreach (var key in lifted)
        {
            Assert.IsTrue(RegistryPolicy.IsPersisted(key), key);
            Assert.AreEqual("string", AttributeCatalog.Find(key)!.Type, key);
        }

        var members = new List<(string Key, JsonValueKind Kind)>();
        foreach (var value in s_capture.Value.Values.Where(static value => value.Key == ToolParameters))
        {
            using var parameters = JsonDocument.Parse(value.Value.GetProperty("stringValue").GetString()!);
            members.AddRange(parameters.RootElement.EnumerateObject().Select(static member => (member.Name, member.Value.ValueKind)));
        }

        Assert.IsGreaterThan(0, members.Count);
        var undecided = members.Select(static member => member.Key)
            .Where(key => !lifted.Contains(key) && !s_droppedParameters.Contains(key))
            .Distinct(StringComparer.Ordinal).ToArray();
        Assert.IsEmpty(undecided, "Members of tool_parameters with no decision: " + string.Join(", ", undecided));
        Assert.IsTrue(members.Where(member => lifted.Contains(member.Key)).All(static member => member.Kind == JsonValueKind.String));
        foreach (var key in lifted.Concat(s_droppedParameters))
            Assert.Contains(key, members.Select(static member => member.Key), key + " no longer occurs in the capture");
    }

    [TestMethod]
    public void ContentKeys_AreDefined_AndNeverPersisted()
    {
        foreach (var key in s_content)
        {
            var definition = AttributeCatalog.Find(key);
            Assert.IsNotNull(definition, key + " must be defined, so that dropping it is a recorded decision");
            Assert.IsFalse(definition.IsPersisted, key);
            Assert.IsFalse(RegistryPolicy.IsPersisted(key), key);
        }

        // The case the research ledger names: tool_input of the Agent call holds the subagent's whole prompt.
        var agentInput = s_capture.Value.Signals.Single(static signal => signal.Name == "claude_code.tool_result")
            .Records.Where(static record => record.TryGetValue("tool_name", out var tool) &&
                                            tool.GetProperty("stringValue").GetString() == "Agent")
            .Select(static record => record["tool_input"].GetProperty("stringValue").GetString()!).Single();
        using var input = JsonDocument.Parse(agentInput);
        Assert.IsTrue(input.RootElement.TryGetProperty("prompt", out var prompt) && prompt.GetString()!.Length > 0);
    }

    [TestMethod]
    public void IdentityKeys_AreNotPersisted_AndEvidenceKeysAre()
    {
        foreach (var key in s_identity)
            Assert.IsFalse(RegistryPolicy.IsPersisted(key), key);
        foreach (var key in s_evidence)
            Assert.IsTrue(RegistryPolicy.IsPersisted(key), key);
        Assert.AreEqual(Stability.Development, AttributeStability.Of("gen_ai.agent.name"));
        Assert.IsNull(RegistryPolicy.ProducerOf("gen_ai.agent.name"), "nami reuses GenAI's key");
        Assert.IsNull(RegistryPolicy.ProducerOf("session.id"), "nami reuses core's key");
        Assert.AreEqual(Stability.Deprecated, AttributeStability.Of("event.name"));
    }

    [TestMethod]
    public void ProducerKeys_AreDevelopmentNamiDefinitions_InClaudeCodeAttributes()
    {
        var produced = AttributeCatalog.All.Where(static definition => definition.Producer == Producer)
            .Select(static definition => definition.Key).Order(StringComparer.Ordinal).ToArray();
        Assert.HasCount(103, produced);
        Assert.IsTrue(AttributeCatalog.All.Where(static definition => definition.Producer is not null)
            .All(static definition => definition.Producer == Producer && definition.Stability == Stability.Development &&
                                      definition.SourceSchema == RegistryInfo.NamiSchemaUrl));
        var constants = typeof(ClaudeCodeAttributes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static field => field.IsLiteral).Select(static field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.AreSequenceEqual(produced, constants);
        Assert.AreEqual("cost_usd_micros", ClaudeCodeAttributes.CostUsdMicros);
        Assert.AreEqual("claude_code.api_request", Events.ClaudeCodeApiRequest.Name);
        Assert.AreEqual("claude_code.cost.usage", Metrics.ClaudeCodeCostUsage.Name);
        Assert.AreEqual("claude_code.tool.execution", Spans.ClaudeCodeToolExecution.Name);
    }

    [TestMethod]
    public async Task Capture_Ingested_KeepsTheEvidence_AndNoContent()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-15T12:35:00Z", CultureInfo.InvariantCulture));
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        var folder = Path.Combine(Root, "tests/Nami.IntegrationTests/Fixtures/ClaudeCodeOtlp/signed-in");
        foreach (var file in Directory.GetFiles(folder, "*.pb").Order(StringComparer.Ordinal))
        {
            var body = await File.ReadAllBytesAsync(file, context.CancellationToken);
            if (file.EndsWith("-logs.pb", StringComparison.Ordinal))
            {
                var logs = OtlpConverter.ConvertLogs(ExportLogsServiceRequest.Parser.ParseFrom(body), clock);
                Assert.AreEqual(0, logs.RejectedLogRecords, logs.RejectionMessage);
                await store.InsertLogsAsync(IngestionStorageMapper.ToLogStorageRows(logs), context.CancellationToken);
            }
            else if (file.EndsWith("-traces.pb", StringComparison.Ordinal))
            {
                var spans = OtlpConverter.ConvertTraceRequest(ExportTraceServiceRequest.Parser.ParseFrom(body));
                await store.EnqueueAsync(new SpanBatch(IngestionStorageMapper.ToSpanStorageRows(spans)), context.CancellationToken);
            }
            else
            {
                var rows = MetricStorageMapper.ToStorageRows(
                    OtlpConverter.ConvertMetrics(ExportMetricsServiceRequest.Parser.ParseFrom(body)));
                await store.InsertMetricsAsync(rows.Series, rows.Observations, context.CancellationToken);
            }
        }

        var snapshot = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
        Assert.HasCount(77, snapshot.Logs);
        Assert.HasCount(69, snapshot.Spans);

        // No content: not a value of the capture's prompts, edits, descriptions or command lines, and no
        // content, identity or setup key.
        var stored = snapshot.Logs.SelectMany(static log => new[] { log.AttributesJson, log.ResourceJson, log.ScopeAttributesJson, log.Body })
            .Concat(snapshot.Spans.SelectMany(static span => new[]
                { span.AttributesJson, span.ResourceJson, span.ScopeAttributesJson, span.EventsJson, span.LinksJson, span.StatusMessage }))
            .Concat(snapshot.Metrics.SelectMany(static series => new[] { series.AttributesJson, series.ResourceJson, series.ScopeAttributesJson }))
            .OfType<string>().ToArray();
        var leaked = ContentOfTheCapture().Where(text => stored.Any(value => value.Contains(text, StringComparison.Ordinal)))
            .ToArray();
        Assert.IsEmpty(leaked, "Stored content: " + string.Join(" | ", leaked));
        var keys = snapshot.Logs.SelectMany(static log => new[] { log.AttributesJson, log.ResourceJson })
            .Concat(snapshot.Spans.SelectMany(static span => new[] { span.AttributesJson, span.ResourceJson }))
            .Concat(snapshot.Metrics.SelectMany(static series => new[] { series.AttributesJson, series.ResourceJson }))
            .OfType<string>().SelectMany(TopLevelKeys).ToHashSet(StringComparer.Ordinal);
        string[] forbidden = [.. s_content, .. s_identity, .. s_undefined, "event.name", "span.type"];
        Assert.IsEmpty(forbidden.Where(keys.Contains).ToArray(), "Stored keys: " + string.Join(", ", forbidden.Where(keys.Contains)));

        // The evidence: cost per request in micro-dollars, tokens, tool calls, the commit and the session.
        var requests = snapshot.Logs.Where(static log => log.Body?.Contains("claude_code.api_request", StringComparison.Ordinal) == true)
            .Select(static log => log.AttributesJson!).ToArray();
        Assert.HasCount(17, requests);
        Assert.AreEqual(3329L + 5762 + 1277, requests.Sum(static json => Number(json, "cost_usd_micros")));
        Assert.IsTrue(requests.All(static json => Number(json, "output_tokens") > 0));
        var results = snapshot.Logs.Where(static log => log.Body?.Contains("claude_code.tool_result", StringComparison.Ordinal) == true)
            .Select(static log => log.AttributesJson!).ToArray();
        Assert.HasCount(16, results);
        foreach (var key in new[] { "tool_name", "tool_use_id", "success", "duration_ms", "prompt.id", "event.sequence", "session.id" })
            Assert.IsTrue(results.All(json => TopLevelKeys(json).Contains(key)), key);
        Assert.HasCount(2, results.Where(static json => TopLevelKeys(json).Contains("vcs.ref.head.revision")));
        var tokens = snapshot.Metrics.Where(static series => series.MetricName == "claude_code.token.usage")
            .Select(static series => series.AttributesJson!).ToArray();
        Assert.IsTrue(tokens.All(static json => TopLevelKeys(json).IsSupersetOf(["type", "model", "query_source", "session.id"])));
        Assert.AreSequenceEqual(["cacheCreation", "cacheRead", "input", "output"],
            tokens.Select(static json => Text(json, "type")).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.IsTrue(snapshot.Logs.All(static log => log.SessionId is not null), "every event names its session");
        Assert.IsTrue(snapshot.Spans.All(static span => span.SessionId is not null), "every span names its session");
        Assert.IsTrue(snapshot.Logs.All(static log => log.AttributesJson!.Contains("\"gen_ai.agent.name\":\"fixture-builder\"", StringComparison.Ordinal)));
    }

    // Values in the capture that are prompt, response, tool input or command text, ten characters or more.
    // Kept members are not content: the program name, the subagent type, the file path (a tool detail) and
    // the commit and branch, which vcs.ref.head.* carry.
    private static IEnumerable<string> ContentOfTheCapture()
    {
        var texts = new HashSet<string>(StringComparer.Ordinal) { "<REDACTED>" };
        foreach (var value in s_capture.Value.Values.Where(static value => value.Key is "tool_input" or "tool_parameters" or "full_command"))
        {
            var text = value.Value.GetProperty("stringValue").GetString()!;
            if (value.Key == "full_command")
            {
                texts.Add(text);
                continue;
            }

            using var parameters = JsonDocument.Parse(text);
            foreach (var member in parameters.RootElement.EnumerateObject())
                if (member.Name is not ("bash_command" or "subagent_type" or "file_path" or "git_commit_id" or "git_branch") &&
                    member.Value.ValueKind == JsonValueKind.String)
                    texts.Add(member.Value.GetString()!);
        }

        return texts.Where(static text => text.Length >= 10).Order(StringComparer.Ordinal);
    }

    private static HashSet<string> TopLevelKeys(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().Select(static property => property.Name).ToHashSet(StringComparer.Ordinal);
    }

    private static string Text(string json, string key)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(key).GetString()!;
    }

    // An int attribute is stored as {"value": "<digits>"}.
    private static long Number(string json, string key)
    {
        using var document = JsonDocument.Parse(json);
        var value = document.RootElement.GetProperty(key);
        if (value.ValueKind == JsonValueKind.Object) value = value.GetProperty("value");
        return value.ValueKind == JsonValueKind.String
            ? long.Parse(value.GetString()!, CultureInfo.InvariantCulture)
            : value.GetInt64();
    }

    private static bool HasWireType(JsonElement value, string type)
    {
        if (type.EndsWith("[]", StringComparison.Ordinal))
            return value.TryGetProperty("arrayValue", out var array) &&
                   (!array.TryGetProperty("values", out var items) ||
                    items.EnumerateArray().All(item => HasWireType(item, type[..^2])));
        return type switch
        {
            "string" or "enum" => value.TryGetProperty("stringValue", out _),
            "int" => value.TryGetProperty("intValue", out _),
            "double" => value.TryGetProperty("doubleValue", out _),
            "boolean" => value.TryGetProperty("boolValue", out _),
            _ => false
        };
    }

    private static bool Converts(string text, string type) => type switch
    {
        "int" => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out _),
        "double" => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
        "boolean" => text is "true" or "false",
        _ => false
    };

    private enum SignalKind { Metric, Event, Span }

    private sealed record CapturedSignal(SignalKind Kind, string Name, List<Dictionary<string, JsonElement>> Records);

    /// <summary>One attribute value: Signal names the metric, event or span it belongs to, if any.</summary>
    private sealed record CapturedValue(string Context, string? Signal, string Key, JsonElement Value);

    /// <summary>Every OTLP/JSON body of both capture folders, as signals with their records and as single values.</summary>
    private sealed class Capture
    {
        public List<CapturedSignal> Signals { get; } = [];
        public List<CapturedValue> Values { get; } = [];
        public SortedSet<string> Scopes { get; } = new(StringComparer.Ordinal);

        public static Capture Load(string root)
        {
            var capture = new Capture();
            var signals = new Dictionary<string, CapturedSignal>(StringComparer.Ordinal);
            var files = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)
                .Where(static path => Path.GetFileName(path) is not ("manifest.json" or "cli-results.json"))
                .Order(StringComparer.Ordinal).ToArray();
            Assert.IsGreaterThan(50, files.Length);
            foreach (var file in files)
            {
                // The values outlive the document: a cloned element owns its own copy.
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                var body = document.RootElement.Clone();
                foreach (var resource in Items(body, "resourceLogs"))
                {
                    capture.AddResource(resource);
                    foreach (var scope in Items(resource, "scopeLogs").Select(capture.Scope))
                    foreach (var log in Items(scope, "logRecords"))
                    {
                        var name = "claude_code." + Attributes(log).Single(static a => a.Key == "event.name").Value
                            .GetProperty("stringValue").GetString();
                        Assert.AreEqual(name, log.GetProperty("body").GetProperty("stringValue").GetString());
                        capture.Add(signals, SignalKind.Event, name, Attributes(log));
                    }
                }

                foreach (var resource in Items(body, "resourceMetrics"))
                {
                    capture.AddResource(resource);
                    foreach (var scope in Items(resource, "scopeMetrics").Select(capture.Scope))
                    foreach (var metric in Items(scope, "metrics"))
                    foreach (var point in Items(metric.GetProperty("sum"), "dataPoints"))
                        capture.Add(signals, SignalKind.Metric, metric.GetProperty("name").GetString()!, Attributes(point));
                }

                foreach (var resource in Items(body, "resourceSpans"))
                {
                    capture.AddResource(resource);
                    foreach (var scope in Items(resource, "scopeSpans").Select(capture.Scope))
                    foreach (var span in Items(scope, "spans"))
                    {
                        var name = span.GetProperty("name").GetString()!;
                        capture.Add(signals, SignalKind.Span, name, Attributes(span));
                        foreach (var spanEvent in Items(span, "events"))
                            capture.AddContext(name + " event " + spanEvent.GetProperty("name").GetString(), Attributes(spanEvent));
                        foreach (var link in Items(span, "links"))
                            capture.AddContext(name + " link", Attributes(link));
                    }
                }
            }

            capture.Signals.AddRange(signals.Values.OrderBy(static signal => signal.Name, StringComparer.Ordinal));
            return capture;
        }

        private JsonElement Scope(JsonElement scope)
        {
            Scopes.Add(scope.GetProperty("scope").GetProperty("name").GetString()!);
            return scope;
        }

        private void Add(Dictionary<string, CapturedSignal> signals, SignalKind kind, string name,
            List<KeyValuePair<string, JsonElement>> attributes)
        {
            if (!signals.TryGetValue(name, out var signal))
                signals.Add(name, signal = new CapturedSignal(kind, name, []));
            signal.Records.Add(attributes.ToDictionary(StringComparer.Ordinal));
            Values.AddRange(attributes.Select(attribute => new CapturedValue(name, name, attribute.Key, attribute.Value)));
        }

        private void AddContext(string context, List<KeyValuePair<string, JsonElement>> attributes) =>
            Values.AddRange(attributes.Select(attribute => new CapturedValue(context, null, attribute.Key, attribute.Value)));

        private void AddResource(JsonElement resource) =>
            AddContext("resource", resource.TryGetProperty("resource", out var value) ? Attributes(value) : []);

        private static JsonElement[] Items(JsonElement element, string name) =>
            element.TryGetProperty(name, out var items) ? [.. items.EnumerateArray()] : [];

        private static List<KeyValuePair<string, JsonElement>> Attributes(JsonElement element) =>
            [.. Items(element, "attributes").Select(static a => KeyValuePair.Create(a.GetProperty("key").GetString()!, a.GetProperty("value")))];
    }

    private sealed record SignalAttribute(string RequirementLevel, string? WireType);

    /// <summary>The attributes each metric, event and span of the published registry declares.</summary>
    private sealed class RegistrySignals
    {
        private static readonly (string Kind, string Id)[] s_kinds = [("metrics", "name"), ("events", "name"), ("spans", "type")];
        private readonly Dictionary<string, Dictionary<string, SignalAttribute>> _attributes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _producers = new(StringComparer.Ordinal);

        public static RegistrySignals Load(string path)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var catalog = document.RootElement.GetProperty("attribute_catalog");
            var registry = document.RootElement.GetProperty("registry");
            var result = new RegistrySignals();
            foreach (var (kind, id) in s_kinds)
            foreach (var signal in registry.GetProperty(kind).EnumerateArray())
            {
                var name = signal.GetProperty(id).GetString()!;
                if (signal.TryGetProperty("annotations", out var own) && own.TryGetProperty("nami.producer", out var producer))
                    result._producers[name] = producer.GetString()!;
                if (!signal.TryGetProperty("attributes", out var references))
                {
                    result._attributes[name] = new Dictionary<string, SignalAttribute>(StringComparer.Ordinal);
                    continue;
                }

                var attributes = new Dictionary<string, SignalAttribute>(StringComparer.Ordinal);
                foreach (var reference in references.EnumerateArray())
                {
                    var attribute = catalog[reference.GetProperty("base").GetInt32()];
                    var level = reference.GetProperty("requirement_level");
                    var wire = attribute.TryGetProperty("annotations", out var annotations) &&
                               annotations.TryGetProperty("nami.wire_type", out var type)
                        ? type.GetString()
                        : null;
                    attributes[attribute.GetProperty("key").GetString()!] = new SignalAttribute(
                        level.ValueKind == JsonValueKind.String ? level.GetString()! : level.EnumerateObject().First().Name, wire);
                }

                result._attributes[name] = attributes;
            }

            return result;
        }

        public Dictionary<string, SignalAttribute> Attributes(string signal) =>
            _attributes.TryGetValue(signal, out var attributes) ? attributes : new Dictionary<string, SignalAttribute>();

        public IEnumerable<string> ProducerSignals(string producer) =>
            _producers.Where(entry => entry.Value == producer).Select(static entry => entry.Key);
    }
}
