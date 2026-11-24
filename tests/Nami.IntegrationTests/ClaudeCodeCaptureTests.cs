using System.Text.Json;
using Google.Protobuf.Collections;
using Nami.Tests.Infrastructure;
using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Logs.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Trace.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1;

namespace Nami.IntegrationTests;

/// <summary>
///     The scrubbed Claude Code capture and the synthetic variants that M2's acceptance tests post (EndToEnd test
///     lane m2-acceptance), proven in the gate before any M2 tool exists: the planted runs and the content sentinels
///     are what those tests say they are, and the capture is read only as manifest.json records it.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ClaudeCodeCaptureTests
{
    private const string SessionOne = "00000000-0000-4000-8000-000000000009";
    private const string PassingTestRun = "toolu_01FIXTURE000000000000004";
    private const string Sentinel = "NAMI-SENTINEL-";
    private static readonly string[] s_sessions = ["s1", "s2", "s3"];
    private static readonly string[] s_bodiesOfThePassingRun = ["00005-traces.pb", "00006-logs.pb"];

    [TestMethod]
    public void SignedIn_ThirtyFiveBodies_InArrivalOrderAsTheManifestRecordsThem()
    {
        var bodies = ClaudeCodeCapture.SignedIn();
        Assert.HasCount(35, bodies);
        Assert.AreEqual(14, bodies.Count(static b => b.Signal == "logs"));
        Assert.AreEqual(10, bodies.Count(static b => b.Signal == "metrics"));
        Assert.AreEqual(11, bodies.Count(static b => b.Signal == "traces"));
        Assert.AreSequenceEqual(s_sessions, bodies.Select(static b => b.Session).Distinct().ToArray());
        Assert.AreEqual("00001-logs.pb", bodies[0].Name);
        Assert.AreEqual("00035-metrics.pb", bodies[^1].Name);
    }

    [TestMethod]
    public void PlantRun_FailingGateRun_IsALaterToolCallOfSessionOneAfterItsCommit()
    {
        var capture = ClaudeCodeCapture.SignedIn();
        var planted = ClaudeCodeCapture.PlantRun(capture, PassingTestRun,
            new PlantedRun("./eng/test.sh", false, TimeSpan.FromSeconds(4), 103, "toolu_01PLANTED0000000000103"));
        Assert.AreEqual("logs", planted.Signal);
        Assert.AreEqual("s1", planted.Session);
        var records = ExportLogsServiceRequest.Parser.ParseFrom(planted.Payload).ResourceLogs.Single().ScopeLogs.Single()
            .LogRecords;
        Assert.HasCount(2, records);
        var decision = records[0];
        var result = records[1];
        Assert.AreEqual("tool_decision", Text(decision.Attributes, "event.name"));
        Assert.AreEqual("tool_result", Text(result.Attributes, "event.name"));
        Assert.AreEqual("claude_code.tool_result", result.Body.StringValue);
        Assert.AreEqual(SessionOne, Text(result.Attributes, "session.id"));
        Assert.AreEqual("fixture-builder", Text(result.Attributes, "gen_ai.agent.name"));
        Assert.AreEqual("toolu_01PLANTED0000000000103", Text(result.Attributes, "tool_use_id"));
        Assert.AreEqual("false", Text(result.Attributes, "success"));
        Assert.AreEqual("ShellError", Text(result.Attributes, "error_type"));
        using (var parameters = JsonDocument.Parse(Text(result.Attributes, "tool_parameters")!))
        {
            Assert.AreEqual("./eng/test.sh", parameters.RootElement.GetProperty("full_command").GetString());
            Assert.AreEqual("./eng/test.sh", parameters.RootElement.GetProperty("bash_command").GetString());
        }

        Assert.AreEqual(103L, decision.Attributes.Single(static a => a.Key == "event.sequence").Value.IntValue);
        Assert.AreEqual(104L, result.Attributes.Single(static a => a.Key == "event.sequence").Value.IntValue);
        Assert.IsTrue(result.TraceId.IsEmpty && result.SpanId.IsEmpty, "No span was exported for a planted run.");
        var sessionOne = ClaudeCodeCapture.LogRecords(capture).Where(static r => r.Body.Session == "s1").ToArray();
        var commit = sessionOne.Single(static r =>
            Text(r.Record.Attributes, "vcs.ref.head.revision") == "c0ffee0000000000000000000000000000000001");
        Assert.IsGreaterThan(commit.Record.TimeUnixNano, decision.TimeUnixNano, "The run is after s1's commit.");
        Assert.AreEqual(sessionOne.Max(static r => r.Record.TimeUnixNano) + 4_000_000_000UL, decision.TimeUnixNano);
        Assert.IsGreaterThan(decision.TimeUnixNano, result.TimeUnixNano);
        Assert.IsGreaterThan(sessionOne.Max(static r => Sequence(r.Record.Attributes)), 103L,
            "Its event sequence numbers follow every event of s1.");
    }

    [TestMethod]
    public void PlantRun_BackgroundGateRun_DiffersFromAForegroundRunOnlyInItsToolInput()
    {
        var capture = ClaudeCodeCapture.SignedIn();
        var run = new PlantedRun("./eng/test.sh", true, TimeSpan.FromSeconds(2), 101, "toolu_01PLANTED0000000000101");
        var foreground = Records(ClaudeCodeCapture.PlantRun(capture, PassingTestRun, run));
        var background = Records(ClaudeCodeCapture.PlantRun(capture, PassingTestRun, run with { InBackground = true }));
        Assert.AreEqual(foreground[0], background[0], "The tool_decision is the same.");
        Assert.AreEqual("true", Text(background[1].Attributes, "success"));
        using (var input = JsonDocument.Parse(Text(background[1].Attributes, "tool_input")!))
        {
            Assert.IsTrue(input.RootElement.GetProperty("run_in_background").GetBoolean());
        }

        using (var input = JsonDocument.Parse(Text(foreground[1].Attributes, "tool_input")!))
        {
            Assert.IsFalse(input.RootElement.TryGetProperty("run_in_background", out _));
        }

        var differing = foreground[1].Attributes.Zip(background[1].Attributes)
            .Where(static pair => !pair.First.Equals(pair.Second)).Select(static pair => pair.First.Key);
        Assert.AreEqual("tool_input", string.Join(' ', differing));

        static RepeatedField<Nami.Tests.Wire.OpenTelemetry.Proto.Logs.V1.LogRecord> Records(OtlpBody planted) =>
            ExportLogsServiceRequest.Parser.ParseFrom(planted.Payload).ResourceLogs.Single().ScopeLogs.Single().LogRecords;
    }

    [TestMethod]
    public void WithCommand_ChangesOnlyThatBashCallOnItsEventsAndSpan()
    {
        var capture = ClaudeCodeCapture.SignedIn();
        var changed = ClaudeCodeCapture.WithCommand(capture, PassingTestRun, "./eng/test.sh");
        Assert.AreSequenceEqual(s_bodiesOfThePassingRun, capture.Zip(changed)
            .Where(static pair => !pair.First.Payload.AsSpan().SequenceEqual(pair.Second.Payload))
            .Select(static pair => pair.First.Name).ToArray());
        var events = ClaudeCodeCapture.LogRecords(changed)
            .Where(static r => Text(r.Record.Attributes, "tool_use_id") == PassingTestRun).ToArray();
        Assert.HasCount(2, events);
        foreach (var record in events.Select(static e => e.Record))
        {
            using var parameters = JsonDocument.Parse(Text(record.Attributes, "tool_parameters")!);
            Assert.AreEqual("./eng/test.sh", parameters.RootElement.GetProperty("full_command").GetString());
        }

        Assert.AreEqual("true", Text(events[1].Record.Attributes, "success"), "The run keeps its outcome and time.");
        var span = changed.Where(static b => b.Signal == "traces")
            .SelectMany(static b => ExportTraceServiceRequest.Parser.ParseFrom(b.Payload).ResourceSpans)
            .SelectMany(static r => r.ScopeSpans).SelectMany(static s => s.Spans)
            .Single(static s => s.Name == "claude_code.tool" && Text(s.Attributes, "tool_use_id") == PassingTestRun);
        Assert.AreEqual("./eng/test.sh", Text(span.Attributes, "full_command"));
    }

    [TestMethod]
    public void WithContentSentinels_PlantsEveryLedgerAttributeAndKeepsEveryCommand()
    {
        var capture = ClaudeCodeCapture.SignedIn();
        var planted = ClaudeCodeCapture.WithContentSentinels(capture, Sentinel);
        var text = string.Join('\n', planted.Select(static b => b.Signal switch
        {
            "logs" => ExportLogsServiceRequest.Parser.ParseFrom(b.Payload).ToString(),
            "traces" => ExportTraceServiceRequest.Parser.ParseFrom(b.Payload).ToString(),
            _ => ""
        }));
        foreach (var name in ClaudeCodeCapture.ContentSentinelNames)
            Assert.Contains(Sentinel + name, text, "The sentinel of " + name + " is planted.");
        Assert.AreSequenceEqual(Commands(capture), Commands(planted), "Commands in tool_parameters stay as sent.");
        Assert.IsFalse(Commands(planted).Any(static c => c.Contains(Sentinel, StringComparison.Ordinal)));
    }

    private static string[] Commands(IReadOnlyList<OtlpBody> bodies)
    {
        return
        [
            .. ClaudeCodeCapture.LogRecords(bodies)
                .Where(static r => Text(r.Record.Attributes, "event.name") == "tool_result")
                .Select(static r => Text(r.Record.Attributes, "tool_parameters"))
                .OfType<string>()
                .Select(static json =>
                {
                    using var parameters = JsonDocument.Parse(json);
                    return parameters.RootElement.TryGetProperty("full_command", out var command)
                        ? command.GetString() ?? ""
                        : "";
                })
                .Where(static command => command.Length > 0)
        ];
    }

    private static string? Text(RepeatedField<KeyValue> attributes, string key) =>
        ClaudeCodeCapture.Text(attributes, key);

    private static long Sequence(RepeatedField<KeyValue> attributes) =>
        attributes.LastOrDefault(static a => a.Key == "event.sequence")?.Value.IntValue ?? 0;
}
