using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using static Nami.EndToEndTests.Sessions.SessionsContract;

namespace Nami.EndToEndTests.Sessions;

/// <summary>
///     Finish-line item 6, the live check: the nami that runs on the Mac answers for the team's own Claude Code
///     sessions. Test lane live-nami (plan rule 14), run on the Mac with <c>./eng/test.sh --lane live-nami</c> while
///     nami and the sessions run; no CI job can reach that nami. Settings: <c>NAMI_LIVE_MCP_URL</c> (default
///     <c>http://127.0.0.1:5100/mcp</c>), <c>NAMI_LIVE_READ_KEY</c> (sent as <c>x-otlp-api-key</c> when set),
///     <c>NAMI_LIVE_AGENTS</c> (default <c>nami-lead,nami-builder,nami-evaluator</c>) and <c>NAMI_LIVE_SINCE</c>
///     (default <c>-24h</c>).
/// </summary>
[TestClass]
[TestCategory("EndToEnd")]
[TestCategory("LiveNami")]
public sealed class LiveNamiTests(TestContext context)
{
    private static readonly string[] s_statuses = [Verified, Contradicted, Unverified];

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task LiveNami_RunningLocalNami_AnswersForEachSessionOfTheTeam()
    {
        var endpoint = new Uri(Setting("NAMI_LIVE_MCP_URL", "http://127.0.0.1:5100/mcp"));
        var key = Environment.GetEnvironmentVariable("NAMI_LIVE_READ_KEY");
        var agents = Setting("NAMI_LIVE_AGENTS", "nami-lead,nami-builder,nami-evaluator")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var since = Setting("NAMI_LIVE_SINCE", "-24h");
        await using var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            AdditionalHeaders = key is null ? null : new Dictionary<string, string> { ["x-otlp-api-key"] = key }
        });
        await using var client = await McpClient.CreateAsync(transport,
            new McpClientOptions { ProtocolVersion = "2026-07-28" }, cancellationToken: context.CancellationToken);
        var names = (await client.ListToolsAsync(cancellationToken: context.CancellationToken))
            .Select(static tool => tool.Name).ToArray();
        Assert.Contains(SearchTool, names, $"The nami at {endpoint} offers {string.Join(", ", names)}.");
        Assert.Contains(GetTool, names, $"The nami at {endpoint} offers {string.Join(", ", names)}.");

        var sessions = new List<JsonElement>();
        var input = new JsonObject { ["since"] = since, ["max_bytes"] = 262144 };
        for (var page = 1; ; page++)
        {
            var result = await CallAsync(client, SearchTool, input);
            sessions.AddRange(result.GetProperty(Sessions).EnumerateArray().Select(static s => s.Clone()));
            if (!result.TryGetProperty("next_cursor", out var cursor)) break;
            Assert.IsLessThan(1000, page, "Paging the live sessions must end.");
            input["cursor"] = cursor.GetString();
        }

        foreach (var agent in agents)
        {
            var own = sessions.Where(s => SessionTools.Text(s, AgentName) == agent).ToArray();
            Assert.IsNotEmpty(own, $"No session of {agent} since {since}; agents seen: " +
                                   string.Join(", ", sessions.Select(static s => SessionTools.Text(s, AgentName)).Distinct()));
            var session = own.MaxBy(static s => s.GetProperty(ToolCallCount).GetInt64());
            var id = SessionTools.Text(session, SessionId)!;
            var where = $"{agent} session {id}";
            Assert.IsFalse(string.IsNullOrEmpty(SessionTools.Text(session, Model)), where);
            Assert.IsGreaterThan(0, session.GetProperty(InputTokens).GetInt64() + session.GetProperty(OutputTokens).GetInt64(), where);
            Assert.IsGreaterThan(0, session.GetProperty(CostUsdMicros).GetInt64(), where);
            Assert.IsGreaterThan(0, session.GetProperty(ToolCallCount).GetInt64(), where);
            Assert.IsGreaterThan(0d, session.GetProperty(DurationMs).GetDouble(), where);

            var detail = (await CallAsync(client, GetTool,
                new JsonObject { [SessionId] = id, ["max_bytes"] = 262144 })).GetProperty(Session);
            Assert.IsNotEmpty(detail.GetProperty(ToolCalls).EnumerateArray().ToArray(), where);
            foreach (var claim in detail.GetProperty(TestClaims).EnumerateArray())
                Assert.Contains(SessionTools.Text(claim, ClaimStatus), s_statuses, where + ": " + claim.GetRawText());
        }
    }

    private async Task<JsonElement> CallAsync(McpClient client, string tool, JsonObject input)
    {
        using var arguments = JsonDocument.Parse(input.ToJsonString());
        var result = await client.CallToolAsync(new CallToolRequestParams
        {
            Name = tool,
            Arguments = arguments.RootElement.EnumerateObject().ToDictionary(static p => p.Name, static p => p.Value.Clone())
        }, context.CancellationToken);
        var content = SessionTools.Structured(result, tool);
        if (result.IsError == true) Assert.Fail($"{tool} refused {input.ToJsonString()}: {content.GetRawText()}");
        return content.Clone();
    }

    private static string Setting(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;
}
