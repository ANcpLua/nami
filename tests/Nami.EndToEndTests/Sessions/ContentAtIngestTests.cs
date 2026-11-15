using System.Text.Json.Nodes;
using Nami.Tests.Infrastructure;

namespace Nami.EndToEndTests.Sessions;

/// <summary>
///     Finish-line item 5 at the store: nami keeps no prompt, response or tool content, even when a session sends it.
///     These run in the gate today: they need no M2 tool, and they guard the M2 change that teaches the registry
///     Claude Code's keys. Each proves its scan can see: the session ids must be found in the same store, and
///     logs_search must still find the edited tool_result events.
/// </summary>
[TestClass]
[TestCategory("EndToEnd")]
public sealed class ContentAtIngestTests(TestContext context)
{
    internal const string SentinelPrefix = "NAMI-SENTINEL-";

    /// <summary>The names <see cref="ClaudeCodeCapture.WithContentSentinels" /> appends to the prefix.</summary>
    private static readonly string[] s_planted =
    [
        "prompt", "prompt_text", "system_prompt", "system_prompt.prompt", "response", "tool_input",
        "api_request_body", "api_response_body", "user_prompt", "new_context", "system_reminders",
        "system_prompt_preview", "user_system_prompt", "response.model_output", "tool.output.content",
        "tool.output.output", "tool.output.diff"
    ];

    /// <summary>Text that exists in the capture only inside <c>tool_input</c>: the subagent's prompt and an Edit.</summary>
    private static readonly string[] s_toolInputText =
        ["Reply in one line: which functions calc.py defines", "return a + b"];

    private static readonly string[] s_sentinelQuery = [SentinelPrefix];

    private static readonly string[] s_listTools = ["sources_status", "errors_recent", "traces_slowest", "changes_recent"];

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task Ingest_SignedInCapture_KeepsNoToolInputText()
    {
        var directory = context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");
        var bodies = ClaudeCodeCapture.SignedIn();
        List<(string Tool, string Text)> answers;
        await using (var host = await SessionTools.IngestAsync(directory, bodies, context.CancellationToken))
        {
            await RequireToolResultsReadableAsync(host);
            answers = [.. await AnswersAsync(host, bodies, s_toolInputText)];
        }

        foreach (var text in s_toolInputText)
        {
            AssertAbsentFromAnswers(answers, text);
            await AssertAbsentFromStoreAsync(directory, text, null);
        }

        await AssertSessionIdsStoredAsync(directory);
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task Ingest_SentinelInEveryContentAttribute_StoresAndAnswersNoneOfIt()
    {
        var directory = context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");
        var bodies = ClaudeCodeCapture.WithContentSentinels(ClaudeCodeCapture.SignedIn(), SentinelPrefix);
        List<(string Tool, string Text)> answers;
        await using (var host = await SessionTools.IngestAsync(directory, bodies, context.CancellationToken))
        {
            await RequireToolResultsReadableAsync(host);
            answers = [.. await AnswersAsync(host, bodies, s_sentinelQuery)];
        }

        AssertAbsentFromAnswers(answers, SentinelPrefix);
        await AssertAbsentFromStoreAsync(directory, SentinelPrefix, s_planted);
        await AssertSessionIdsStoredAsync(directory);
    }

    /// <summary>The control that the edited events were stored: logs_search finds all 16 tool_result events.</summary>
    private async Task RequireToolResultsReadableAsync(McpHarness host)
    {
        var found = await SessionTools.CallAsync(host, "logs_search",
            new JsonObject
            {
                ["since"] = SignedInSessions.WindowStart, ["max_bytes"] = 262144, ["query"] = "claude_code.tool_result"
            }, context.CancellationToken);
        Assert.AreEqual(16, found.GetProperty("records").GetArrayLength(),
            "logs_search must find the capture's 16 tool_result events, or the scans below prove nothing.");
    }

    /// <summary>The answers of the six tools over the capture's window, every trace included.</summary>
    private async Task<IEnumerable<(string Tool, string Text)>> AnswersAsync(McpHarness host,
        IReadOnlyList<OtlpBody> bodies, IEnumerable<string> queries)
    {
        var answers = new List<(string Tool, string Text)>();
        foreach (var tool in s_listTools)
            answers.Add((tool, (await SessionTools.CallAsync(host, tool,
                SessionTools.Search(SignedInSessions.WindowStart), context.CancellationToken)).GetRawText()));
        foreach (var query in queries)
        {
            var search = SessionTools.Search(SignedInSessions.WindowStart);
            search["query"] = query;
            answers.Add(("logs_search " + query, (await SessionTools.CallAsync(host, "logs_search", search,
                context.CancellationToken)).GetRawText()));
        }

        foreach (var traceId in ClaudeCodeCapture.TraceIds(bodies))
        {
            var input = SessionTools.Search(SignedInSessions.WindowStart);
            input["trace_id"] = traceId;
            answers.Add(("traces_get " + traceId, (await SessionTools.CallAsync(host, "traces_get", input,
                context.CancellationToken)).GetRawText()));
        }

        return answers;
    }

    private static void AssertAbsentFromAnswers(IEnumerable<(string Tool, string Text)> answers, string text)
    {
        var leaks = answers.Where(answer => answer.Text.Contains(text, StringComparison.Ordinal))
            .Select(static answer => answer.Tool).ToArray();
        Assert.IsEmpty(leaks, $"Tool answers contain \"{text}\": {string.Join(", ", leaks)}.");
    }

    private async Task AssertAbsentFromStoreAsync(string directory, string text, IReadOnlyList<string>? planted)
    {
        var database = Path.Combine(directory, "nami.duckdb");
        var hits = await StoreScan.FindAsync(database, text, context.CancellationToken);
        if (hits.Count == 0) return;
        var attributes = new List<string>();
        foreach (var name in planted ?? [])
            if ((await StoreScan.FindAsync(database, text + name, context.CancellationToken)).Count > 0)
                attributes.Add(name);
        Assert.Fail($"nami stored \"{text}\" in {string.Join(", ", hits)}" +
                    (attributes.Count > 0 ? $"; leaked attributes: {string.Join(", ", attributes)}." : "."));
    }

    /// <summary>The control that the scan reads what nami stores: each session id must be found.</summary>
    private async Task AssertSessionIdsStoredAsync(string directory)
    {
        foreach (var session in SignedInSessions.All)
        {
            var hits = await StoreScan.FindAsync(Path.Combine(directory, "nami.duckdb"), session.Id,
                context.CancellationToken);
            Assert.IsNotEmpty(hits, $"The store scan does not find session {session.Label} ({session.Id}); it cannot " +
                                    "prove that content is absent.");
        }
    }
}
