namespace Nami.EndToEndTests.Sessions;

/// <summary>
///     The wire names M2's acceptance tests read. campaign.md (finish-line items 3 and 4) fixes what the two tools
///     answer; these names spell it in the snake_case of the six tools. They are the acceptance contract: if the
///     TypeSpec contract settles on other spellings, change them here, in this file only, after review.
/// </summary>
internal static class SessionsContract
{
    internal const string SearchTool = "sessions_search";
    internal const string GetTool = "sessions_get";

    /// <summary>sessions_search: the result member, an array of sessions with records in the window.</summary>
    internal const string Sessions = "sessions";

    /// <summary>sessions_get: the input argument and every session's identity, Claude Code's <c>session.id</c>.</summary>
    internal const string SessionId = "session_id";

    /// <summary>The resource attribute <c>gen_ai.agent.name</c>.</summary>
    internal const string AgentName = "agent_name";

    internal const string Model = "model";

    /// <summary>The <c>effort</c> of the session's <c>api_request</c> events.</summary>
    internal const string Effort = "effort";

    /// <summary>Token counts by type, summed over the session's <c>api_request</c> events, subagent included.</summary>
    internal const string InputTokens = "input_tokens";

    internal const string OutputTokens = "output_tokens";
    internal const string CacheReadTokens = "cache_read_tokens";
    internal const string CacheCreationTokens = "cache_creation_tokens";

    /// <summary>The integer sum of the session's <c>cost_usd_micros</c>, subagent included; no price table.</summary>
    internal const string CostUsdMicros = "cost_usd_micros";

    /// <summary>The session's <c>tool_result</c> events, subagent included, and those with success false.</summary>
    internal const string ToolCallCount = "tool_call_count";

    internal const string ToolFailureCount = "tool_failure_count";
    internal const string DurationMs = "duration_ms";

    /// <summary>sessions_get: the result member, one session.</summary>
    internal const string Session = "session";

    /// <summary>The session's tool calls in order: <c>tool_name</c> and boolean <c>success</c> of each tool_result.</summary>
    internal const string ToolCalls = "tool_calls";

    internal const string ToolName = "tool_name";
    internal const string Success = "success";

    /// <summary>The session's Bash commands in order: <c>command</c> is <c>full_command</c>, with boolean <c>success</c>.</summary>
    internal const string Commands = "commands";

    internal const string Command = "command";

    /// <summary>The session's commits in order: <c>revision</c> is the 40-hex <c>vcs.ref.head.revision</c>.</summary>
    internal const string Commits = "commits";

    internal const string Revision = "revision";

    /// <summary>
    ///     The session's test claims: one per commit the session made, naming that commit, with the status of the claim
    ///     check (finish-line item 4).
    /// </summary>
    internal const string TestClaims = "test_claims";

    internal const string ClaimCommit = "commit";
    internal const string ClaimStatus = "status";
    internal const string Verified = "verified";
    internal const string Contradicted = "contradicted";
    internal const string Unverified = "unverified";
}
