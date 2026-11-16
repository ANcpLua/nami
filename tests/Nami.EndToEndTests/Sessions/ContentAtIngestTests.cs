using System.Text.Json.Nodes;
using Nami.Tests.Infrastructure;

namespace Nami.EndToEndTests.Sessions;

/// <summary>
///     Content at the store: nami stores prompt, response and tool content as a session sent it (observer's decision,
///     16 Nov 2026, which reverses finish-line item 5); what is captured is decided at the source.
///     These run in the gate today: they need no M2 tool.
///     Each proves its scan can see: the session ids must be found in the same store, and
///     logs_search must still find the edited tool_result events.
/// </summary>
[TestClass]
[TestCategory("EndToEnd")]
public sealed class ContentAtIngestTests(TestContext context)
{
    internal const string SentinelPrefix = "NAMI-SENTINEL-";

    /// <summary>
    ///     Text that exists in the capture only inside <c>tool_input</c>: the subagent's prompt. (The Edit's
    ///     "return a + b" is there too, but stored JSON escapes the plus sign, so a scan of the raw column cannot
    ///     find it.)
    /// </summary>
    private static readonly string[] s_toolInputText = ["Reply in one line: which functions calc.py defines"];

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task Ingest_SignedInCapture_StoresToolInputTextAsSent()
    {
        var directory = context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");
        var bodies = ClaudeCodeCapture.SignedIn();
        await using (var host = await SessionTools.IngestAsync(directory, bodies, context.CancellationToken))
            await RequireToolResultsReadableAsync(host);

        foreach (var text in s_toolInputText)
            await AssertStoredAsync(directory, text);
        await AssertSessionIdsStoredAsync(directory);
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task Ingest_SentinelInEveryContentAttribute_StoresEveryOne()
    {
        var directory = context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");
        var bodies = ClaudeCodeCapture.WithContentSentinels(ClaudeCodeCapture.SignedIn(), SentinelPrefix);
        await using (var host = await SessionTools.IngestAsync(directory, bodies, context.CancellationToken))
            await RequireToolResultsReadableAsync(host);

        foreach (var name in ClaudeCodeCapture.ContentSentinelNames)
            await AssertStoredAsync(directory, SentinelPrefix + name);
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

    private async Task AssertStoredAsync(string directory, string text)
    {
        var hits = await StoreScan.FindAsync(Path.Combine(directory, "nami.duckdb"), text, context.CancellationToken);
        Assert.IsNotEmpty(hits, $"nami stored \"{text}\" nowhere; content is stored as it was sent.");
    }

    /// <summary>The control that the scan reads what nami stores: each session id must be found.</summary>
    private async Task AssertSessionIdsStoredAsync(string directory)
    {
        foreach (var session in SignedInSessions.All)
        {
            var hits = await StoreScan.FindAsync(Path.Combine(directory, "nami.duckdb"), session.Id,
                context.CancellationToken);
            Assert.IsNotEmpty(hits, $"The store scan does not find session {session.Label} ({session.Id}); it cannot " +
                                    "prove what is stored.");
        }
    }
}
