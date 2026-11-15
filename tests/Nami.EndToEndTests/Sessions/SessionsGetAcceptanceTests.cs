using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Nami.Tests.Infrastructure;
using static Nami.EndToEndTests.Sessions.SessionsContract;

namespace Nami.EndToEndTests.Sessions;

/// <summary>
///     Finish-line item 3, sessions_get: one session with its tool calls, commands and their success, commits and test
///     claims, on the scrubbed capture; and item 5 at the new tools. Test lane m2-acceptance until M2 lands.
/// </summary>
[TestClass]
[TestCategory("EndToEnd")]
[TestCategory("M2Acceptance")]
public sealed partial class SessionsGetAcceptanceTests(TestContext context)
{
    private static readonly string[] s_projection = [SessionId, Commits];
    private static readonly string[] s_budgetFields = ["max_bytes", "fields"];

    private string Directory => context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex FullRevision();

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow("s1", DisplayName = "s1: failing then passing test run, edit, commit")]
    [DataRow("s2", DisplayName = "s2: background subagent, edits, commit")]
    [DataRow("s3", DisplayName = "s3: piped test run, no commit")]
    public async Task SessionsGet_SignedInSession_ListsToolCallsCommandsCommitsAndClaims(string label)
    {
        var expected = SignedInSessions.ByLabel(label);
        await using var host = await SessionTools.IngestAsync(Directory, ClaudeCodeCapture.SignedIn(), context.CancellationToken);
        await SessionTools.RequireSessionToolsAsync(host, context.CancellationToken);
        var result = await SessionTools.CallAsync(host, GetTool, SessionTools.Get(expected.Id), context.CancellationToken);
        var where = $"{GetTool} {label} ({expected.Id})";
        var session = SessionTools.Member(result, SessionMember, where);
        Assert.AreEqual(expected.Id, SessionTools.Text(session, SessionId), where);
        Assert.AreEqual(SignedInSessions.Agent, SessionTools.Text(session, AgentName), where);
        AssertCalls(expected.ToolCalls, SessionTools.Member(session, ToolCalls, where), ToolName, where + " tool calls");
        AssertCalls(expected.Commands, SessionTools.Member(session, Commands, where), Command, where + " commands");
        var revisions = SessionTools.Member(session, Commits, where).EnumerateArray()
            .Select(commit => SessionTools.Text(commit, Revision) ?? "(no revision)").ToArray();
        Assert.AreSequenceEqual(expected.Commits.ToArray(), revisions, where + " commits");
        Assert.IsTrue(revisions.All(static r => FullRevision().IsMatch(r)), where + ": a revision is 40 lowercase hex digits.");
        // The capture's sessions ran python3 -m unittest, never the gate, so each commit's claim is unverified.
        Assert.AreSequenceEqual(expected.Commits.Select(static c => (c, Unverified)).ToArray(),
            Claims(session, where), where + " test claims");
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task SessionsGet_UnknownSession_RefusedAsNotFoundNamingTheSession()
    {
        const string Unknown = "00000000-0000-4000-8000-0000000000ff";
        await using var host = await SessionTools.IngestAsync(Directory, ClaudeCodeCapture.SignedIn(), context.CancellationToken);
        await SessionTools.RequireSessionToolsAsync(host, context.CancellationToken);
        var refusal = await SessionTools.RefusalAsync(host, GetTool, SessionTools.Get(Unknown), context.CancellationToken);
        Assert.AreEqual("not_found", refusal.GetProperty("code").GetString(), refusal.GetRawText());
        Assert.AreEqual(SessionId, refusal.GetProperty("field").GetString(), refusal.GetRawText());
        Assert.AreEqual("session", refusal.GetProperty("resource_type").GetString(), refusal.GetRawText());
        Assert.AreEqual(Unknown, refusal.GetProperty("resource_id").GetString(), refusal.GetRawText());
        Assert.IsNull(Nami.Api.Contracts.ContractJson.Validate("Common.ToolError", refusal));
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task SessionsGet_Fields_ProjectsOnlyTheRequestedMembers()
    {
        await using var host = await SessionTools.IngestAsync(Directory, ClaudeCodeCapture.SignedIn(), context.CancellationToken);
        await SessionTools.RequireSessionToolsAsync(host, context.CancellationToken);
        var input = SessionTools.Get(SignedInSessions.S1.Id);
        input["fields"] = new JsonArray(SessionId, Commits);
        var session = SessionTools.Member(
            await SessionTools.CallAsync(host, GetTool, input, context.CancellationToken), SessionMember, GetTool);
        Assert.AreSequenceEqual(s_projection.Order(StringComparer.Ordinal).ToArray(),
            session.EnumerateObject().Select(static p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.AreEqual(SignedInSessions.FirstCommit, SessionTools.Text(session.GetProperty(Commits)[0], Revision));
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task SessionsGet_MinimumByteBudget_StaysWithinItAndPagesOrRefuses()
    {
        await using var host = await SessionTools.IngestAsync(Directory, ClaudeCodeCapture.SignedIn(), context.CancellationToken);
        await SessionTools.RequireSessionToolsAsync(host, context.CancellationToken);
        var input = SessionTools.Get(SignedInSessions.S2.Id, 1024);
        var calls = new List<string>();
        for (var page = 1; ; page++)
        {
            var result = await host.CallAsync(GetTool, input.ToJsonString(), context.CancellationToken);
            var content = SessionTools.Structured(result, GetTool);
            if (result.IsError == true)
            {
                // Like traces_get: a session that cannot be paged into 1024 bytes is a typed, repairable refusal.
                Assert.AreEqual(1, page, "A refusal may only answer the first page: " + content.GetRawText());
                Assert.AreEqual("invalid_argument", content.GetProperty("code").GetString(), content.GetRawText());
                Assert.Contains(content.GetProperty("field").GetString(), s_budgetFields, content.GetRawText());
                return;
            }

            Assert.IsLessThanOrEqualTo(1024, SessionTools.Bytes(content), $"Page {page} exceeds max_bytes.");
            var session = SessionTools.Member(content, SessionMember, GetTool);
            if (session.TryGetProperty(ToolCalls, out var pageCalls))
                calls.AddRange(pageCalls.EnumerateArray().Select(static c => SessionTools.Text(c, ToolName) ?? "?"));
            var more = content.TryGetProperty("next_cursor", out var cursor);
            Assert.AreEqual(more, content.GetProperty("freshness").GetProperty("truncated").GetBoolean(),
                $"Page {page}: truncated must be true exactly when a next_cursor follows.");
            if (!more) break;
            Assert.IsLessThan(60, page, "Paging one session must end.");
            input["cursor"] = cursor.GetString();
        }

        Assert.AreSequenceEqual(SignedInSessions.S2.ToolCalls.Select(static c => c.Name).ToArray(), calls.ToArray(),
            "Across the pages, each of s2's ten tool calls exactly once and in order.");
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task SessionsGet_SnapshotId_ReplaysByteIdentically()
    {
        await using var host = await SessionTools.IngestAsync(Directory, ClaudeCodeCapture.SignedIn(), context.CancellationToken);
        await SessionTools.RequireSessionToolsAsync(host, context.CancellationToken);
        var first = await SessionTools.CallAsync(host, GetTool, SessionTools.Get(SignedInSessions.S1.Id), context.CancellationToken);
        var replay = SessionTools.Get(SignedInSessions.S1.Id);
        replay["snapshot_id"] = first.GetProperty("snapshot_id").GetString();
        Assert.AreEqual(first.GetRawText(),
            (await SessionTools.CallAsync(host, GetTool, replay, context.CancellationToken)).GetRawText());
    }

    /// <summary>
    ///     Finish-line item 5 at the new tools: with a sentinel in every content attribute, neither tool answers any of
    ///     it, and sessions_get still lists each command from <c>tool_parameters</c>.
    /// </summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task SessionTools_SentinelInEveryContentAttribute_AnswerCommandsButNoContent()
    {
        var bodies = ClaudeCodeCapture.WithContentSentinels(ClaudeCodeCapture.SignedIn(), ContentAtIngestTests.SentinelPrefix);
        await using var host = await SessionTools.IngestAsync(Directory, bodies, context.CancellationToken);
        await SessionTools.RequireSessionToolsAsync(host, context.CancellationToken);
        var search = await SessionTools.CallAsync(host, SearchTool, SessionTools.Search(SignedInSessions.WindowStart),
            context.CancellationToken);
        Assert.DoesNotContain(ContentAtIngestTests.SentinelPrefix, search.GetRawText(), SearchTool);
        foreach (var expected in SignedInSessions.All)
        {
            var result = await SessionTools.CallAsync(host, GetTool, SessionTools.Get(expected.Id), context.CancellationToken);
            var where = $"{GetTool} {expected.Label}";
            Assert.DoesNotContain(ContentAtIngestTests.SentinelPrefix, result.GetRawText(), where);
            AssertCalls(expected.Commands, SessionTools.Member(SessionTools.Member(result, SessionMember, where), Commands, where),
                Command, where + " commands");
        }
    }

    internal static (string Commit, string Status)[] Claims(JsonElement session, string where)
    {
        return
        [
            .. SessionTools.Member(session, TestClaims, where).EnumerateArray().Select(static claim =>
                (SessionTools.Text(claim, ClaimCommit) ?? "(no commit)", SessionTools.Text(claim, ClaimStatus) ?? "(no status)"))
        ];
    }

    internal static void AssertCalls(IReadOnlyList<ExpectedCall> expected, JsonElement actual, string nameMember,
        string where)
    {
        var calls = actual.EnumerateArray().Select(call =>
        {
            var success = SessionTools.Member(call, Success, where);
            Assert.IsTrue(success.ValueKind is JsonValueKind.True or JsonValueKind.False,
                $"{where}: success is a JSON boolean, not {success.GetRawText()}.");
            return new ExpectedCall(SessionTools.Text(call, nameMember) ?? "(no " + nameMember + ")", success.GetBoolean());
        }).ToArray();
        Assert.AreSequenceEqual(expected.ToArray(), calls, where);
    }
}
