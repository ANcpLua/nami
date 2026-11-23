using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Nami.Tests.Infrastructure;
using static Nami.EndToEndTests.Sessions.SessionsContract;

namespace Nami.EndToEndTests.Sessions;

/// <summary>
///     Finish-line item 3, sessions_search, on the scrubbed capture of three signed-in sessions, with the byte budget,
///     field selection, cursor and freshness model of the six tools (their tests in ToolContractTests, ToolEdgeTests
///     and NativeConformanceTests). Test lane m2-acceptance until M2 lands.
/// </summary>
[TestClass]
[TestCategory("EndToEnd")]
[TestCategory("M2Acceptance")]
public sealed class SessionsSearchAcceptanceTests(TestContext context)
{
    private static readonly string[] s_projection = [SessionId, CostUsdMicros];

    private string Directory => context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task SessionsSearch_SignedInCapture_ReportsEachSessionAsClaudeCodeSentIt()
    {
        await using var host = await SessionTools.IngestAsync(Directory, ClaudeCodeCapture.SignedIn(), context.CancellationToken);
        await SessionTools.RequireSessionToolsAsync(host, context.CancellationToken);
        var result = await SessionTools.CallAsync(host, SearchTool, SessionTools.Search(SignedInSessions.WindowStart),
            context.CancellationToken);
        Assert.HasCount(3, SessionTools.Member(result, SessionsMember, SearchTool).EnumerateArray().ToArray(),
            "The capture holds exactly three sessions: " + SessionTools.Shorten(result.GetRawText()));
        foreach (var expected in SignedInSessions.All)
        {
            var session = SessionTools.SessionById(result, expected.Id);
            var where = $"{SearchTool} {expected.Label} ({expected.Id})";
            Assert.AreEqual(SignedInSessions.Agent, Member(session, AgentName, where).GetString(), where);
            Assert.AreEqual(SignedInSessions.Model, Member(session, Model, where).GetString(), where);
            Assert.AreEqual(SignedInSessions.Effort, Member(session, Effort, where).GetString(), where);
            Assert.AreEqual(expected.InputTokens, Member(session, InputTokens, where).GetInt64(), where);
            Assert.AreEqual(expected.OutputTokens, Member(session, OutputTokens, where).GetInt64(), where);
            Assert.AreEqual(expected.CacheReadTokens, Member(session, CacheReadTokens, where).GetInt64(), where);
            Assert.AreEqual(expected.CacheCreationTokens, Member(session, CacheCreationTokens, where).GetInt64(), where);
            Assert.AreEqual(expected.CostUsdMicros, Member(session, CostUsdMicros, where).GetInt64(),
                where + ": the sum of cost_usd_micros, subagent requests included");
            Assert.AreEqual(expected.ToolCalls.Count, Member(session, ToolCallCount, where).GetInt32(), where);
            Assert.AreEqual(expected.ToolFailures, Member(session, ToolFailureCount, where).GetInt32(), where);
            Assert.IsInRange(expected.MinimumDurationMs, expected.MaximumDurationMs,
                Member(session, DurationMs, where).GetDouble(), where + ": duration_ms");
        }

        var freshness = result.GetProperty("freshness");
        Assert.IsTrue(freshness.GetProperty("complete").GetBoolean(), "Every signal of the window was ingested.");
        Assert.AreEqual(3, freshness.GetProperty("matched").GetInt32());
        Assert.AreEqual(3, freshness.GetProperty("returned").GetInt32());
        Assert.IsFalse(freshness.GetProperty("truncated").GetBoolean());
        Assert.IsFalse(result.TryGetProperty("next_cursor", out _));
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow("2026-11-23T22:04:22Z", "s3", DisplayName = "Window starting between s2 and s3")]
    [DataRow("2026-11-23T22:05:00Z", "", DisplayName = "Window starting after the last record")]
    public async Task SessionsSearch_Window_ListsOnlySessionsWithRecordsInIt(string since, string labels)
    {
        await using var host = await SessionTools.IngestAsync(Directory, ClaudeCodeCapture.SignedIn(), context.CancellationToken);
        await SessionTools.RequireSessionToolsAsync(host, context.CancellationToken);
        var result = await SessionTools.CallAsync(host, SearchTool, SessionTools.Search(since), context.CancellationToken);
        var expected = labels.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(static label => SignedInSessions.ByLabel(label).Id).Order(StringComparer.Ordinal).ToArray();
        var actual = Member(result, SessionsMember, SearchTool).EnumerateArray()
            .Select(static s => SessionTools.Text(s, SessionId) ?? "(no session_id)").Order(StringComparer.Ordinal).ToArray();
        Assert.AreSequenceEqual(expected, actual, $"Sessions in the window from {since}.");
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task SessionsSearch_Fields_ProjectsOnlyTheRequestedMembers()
    {
        await using var host = await SessionTools.IngestAsync(Directory, ClaudeCodeCapture.SignedIn(), context.CancellationToken);
        await SessionTools.RequireSessionToolsAsync(host, context.CancellationToken);
        var input = SessionTools.Search(SignedInSessions.WindowStart);
        input["fields"] = new JsonArray(SessionId, CostUsdMicros);
        var result = await SessionTools.CallAsync(host, SearchTool, input, context.CancellationToken);
        foreach (var expected in SignedInSessions.All)
        {
            var session = SessionTools.SessionById(result, expected.Id);
            Assert.AreSequenceEqual(s_projection.Order(StringComparer.Ordinal).ToArray(),
                session.EnumerateObject().Select(static p => p.Name).Order(StringComparer.Ordinal).ToArray());
            Assert.AreEqual(expected.CostUsdMicros, session.GetProperty(CostUsdMicros).GetInt64());
        }
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task SessionsSearch_UnknownField_RefusedNamingTheAllowedFields()
    {
        await using var host = await SessionTools.IngestAsync(Directory, ClaudeCodeCapture.SignedIn(), context.CancellationToken);
        await SessionTools.RequireSessionToolsAsync(host, context.CancellationToken);
        var input = SessionTools.Search(SignedInSessions.WindowStart);
        input["fields"] = new JsonArray("prompt");
        var refusal = await SessionTools.RefusalAsync(host, SearchTool, input, context.CancellationToken);
        Assert.AreEqual("invalid_argument", refusal.GetProperty("code").GetString());
        Assert.AreEqual("fields", refusal.GetProperty("field").GetString());
        var allowed = refusal.GetProperty("allowed").EnumerateArray().Select(static a => a.GetString()).ToArray();
        Assert.Contains(SessionId, allowed);
        Assert.Contains(CostUsdMicros, allowed);
        Assert.DoesNotContain("prompt", allowed);
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow(1023, DisplayName = "Below the minimum budget")]
    [DataRow(262145, DisplayName = "Above the maximum budget")]
    public async Task SessionsSearch_ByteBudgetOutsideItsRange_RefusedNamingMaxBytes(int budget)
    {
        await using var host = await SessionTools.StartAsync(Directory, context.CancellationToken);
        await SessionTools.RequireSessionToolsAsync(host, context.CancellationToken);
        var refusal = await SessionTools.RefusalAsync(host, SearchTool,
            SessionTools.Search(SignedInSessions.WindowStart, budget), context.CancellationToken);
        Assert.AreEqual("invalid_argument", refusal.GetProperty("code").GetString());
        Assert.AreEqual("max_bytes", refusal.GetProperty("field").GetString());
        Assert.IsNull(Nami.Api.Contracts.ContractJson.Validate("Common.ToolError", refusal));
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task SessionsSearch_MinimumByteBudget_PagesEverySessionOnceWithinTheBudget()
    {
        await using var host = await SessionTools.IngestAsync(Directory, ClaudeCodeCapture.SignedIn(), context.CancellationToken);
        await SessionTools.RequireSessionToolsAsync(host, context.CancellationToken);
        var input = SessionTools.Search(SignedInSessions.WindowStart, 1024);
        var seen = new List<string>();
        var pages = 0;
        while (true)
        {
            var page = await SessionTools.CallAsync(host, SearchTool, input, context.CancellationToken);
            pages++;
            Assert.IsLessThanOrEqualTo(1024, SessionTools.Bytes(page), $"Page {pages} exceeds max_bytes.");
            seen.AddRange(Member(page, SessionsMember, SearchTool).EnumerateArray()
                .Select(static s => SessionTools.Text(s, SessionId) ?? "(no session_id)"));
            var more = page.TryGetProperty("next_cursor", out var cursor);
            Assert.AreEqual(more, page.GetProperty("freshness").GetProperty("truncated").GetBoolean(),
                $"Page {pages}: truncated must be true exactly when a next_cursor follows.");
            if (!more) break;
            Assert.IsLessThan(10, pages, "Paging three sessions must end.");
            input["cursor"] = cursor.GetString();
        }

        Assert.IsGreaterThan(1, pages, "Three sessions with every member do not fit 1024 bytes with the envelope.");
        Assert.AreSequenceEqual(SignedInSessions.All.Select(static s => s.Id).Order(StringComparer.Ordinal).ToArray(),
            seen.Order(StringComparer.Ordinal).ToArray(), "Every session exactly once across the pages.");
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task SessionsSearch_CursorWithChangedQueryOrMalformed_RefusedNamingCursor()
    {
        await using var host = await SessionTools.IngestAsync(Directory, ClaudeCodeCapture.SignedIn(), context.CancellationToken);
        await SessionTools.RequireSessionToolsAsync(host, context.CancellationToken);
        var first = await SessionTools.CallAsync(host, SearchTool, SessionTools.Search(SignedInSessions.WindowStart, 1024),
            context.CancellationToken);
        var cursor = Member(first, "next_cursor", SearchTool + " at 1024 bytes").GetString();
        var changed = SessionTools.Search("2026-11-23T22:01:00Z", 1024);
        changed["cursor"] = cursor;
        foreach (var bad in new[] { changed, new JsonObject { ["cursor"] = "not base64" } })
        {
            var refusal = await SessionTools.RefusalAsync(host, SearchTool, bad, context.CancellationToken);
            Assert.AreEqual("cursor", refusal.GetProperty("field").GetString(), refusal.GetRawText());
        }
    }

    [TestMethod]
    [Timeout(180000, CooperativeCancellation = true)]
    public async Task SessionsSearch_SnapshotId_ReplaysByteIdenticallyAcrossARestart()
    {
        string snapshot;
        string answer;
        await using (var host = await SessionTools.IngestAsync(Directory, ClaudeCodeCapture.SignedIn(), context.CancellationToken))
        {
            await SessionTools.RequireSessionToolsAsync(host, context.CancellationToken);
            var first = await SessionTools.CallAsync(host, SearchTool, SessionTools.Search(SignedInSessions.WindowStart),
                context.CancellationToken);
            snapshot = first.GetProperty("snapshot_id").GetString()!;
            answer = first.GetRawText();
            Assert.AreEqual(answer, (await SessionTools.CallAsync(host, SearchTool, Replay(snapshot),
                context.CancellationToken)).GetRawText(), "The same snapshot must answer byte-identically.");
        }

        await using var restarted = await SessionTools.StartAsync(Directory, context.CancellationToken);
        Assert.AreEqual(answer, (await SessionTools.CallAsync(restarted, SearchTool, Replay(snapshot),
            context.CancellationToken)).GetRawText(), "The snapshot must survive a restart.");
        return;

        static JsonObject Replay(string id)
        {
            var input = SessionTools.Search(SignedInSessions.WindowStart);
            input["snapshot_id"] = id;
            return input;
        }
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task ToolsList_SessionTools_ShareTheReadModelAndAnnotationsOfTheSixTools()
    {
        await using var host = await SessionTools.StartAsync(Directory, context.CancellationToken);
        await SessionTools.RequireSessionToolsAsync(host, context.CancellationToken);
        var tools = (await host.Client.ListToolsAsync(new ListToolsRequestParams(), context.CancellationToken)).Tools;
        var model = tools.Single(static t => t.Name == "traces_slowest");
        foreach (var name in new[] { SearchTool, GetTool })
        {
            var tool = tools.Single(t => t.Name == name);
            Assert.IsNotNull(tool.Annotations, name);
            Assert.IsTrue(tool.Annotations.ReadOnlyHint, name);
            Assert.IsTrue(tool.Annotations.IdempotentHint, name);
            Assert.IsFalse(tool.Annotations.DestructiveHint, name);
            Assert.IsFalse(tool.Annotations.OpenWorldHint, name);
            Assert.IsNotNull(tool.OutputSchema, name);
            var input = tool.InputSchema.GetProperty("properties");
            var reference = model.InputSchema.GetProperty("properties");
            foreach (var argument in new[] { "fields", "max_bytes", "snapshot_id", "cursor" })
                Assert.IsTrue(input.TryGetProperty(argument, out var actual) &&
                              JsonElement.DeepEquals(reference.GetProperty(argument), actual),
                    $"{name}: argument {argument} must be the six tools' {argument}.");
            Assert.AreEqual("string", input.GetProperty("since").GetProperty("type").GetString(), name);
            var output = tool.OutputSchema.Value.GetProperty("properties");
            var result = model.OutputSchema!.Value.GetProperty("properties");
            foreach (var member in new[] { "freshness", "snapshot_id", "next_cursor" })
                Assert.IsTrue(output.TryGetProperty(member, out var actual) &&
                              JsonElement.DeepEquals(result.GetProperty(member), actual),
                    $"{name}: result member {member} must be the six tools' {member}.");
            var required = tool.OutputSchema.Value.GetProperty("required").EnumerateArray()
                .Select(static r => r.GetString()).ToArray();
            Assert.Contains("freshness", required, name);
            Assert.Contains("snapshot_id", required, name);
        }

        var get = tools.Single(static t => t.Name == GetTool);
        Assert.Contains(SessionId, get.InputSchema.GetProperty("required").EnumerateArray()
            .Select(static r => r.GetString()).ToArray(), "sessions_get reads one session by its id.");
        Assert.AreEqual("array", tools.Single(static t => t.Name == SearchTool).OutputSchema!.Value
            .GetProperty("properties").GetProperty(SessionsMember).GetProperty("type").GetString());
        Assert.AreEqual("object", get.OutputSchema!.Value.GetProperty("properties").GetProperty(SessionMember)
            .GetProperty("type").GetString());
    }

    private static JsonElement Member(JsonElement value, string name, string where) => SessionTools.Member(value, name, where);
}
