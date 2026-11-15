// The two session tools of M2. Every field comes from what Claude Code exports (research ledger,
// "Claude Code telemetry as sent", and the scrubbed fixture `ClaudeCodeOtlp/signed-in`): sessions
// from `session.id`, the agent from `gen_ai.agent.name`, models, effort, tokens and cost from the
// `api_request` events, tool calls, commands and commits from the `tool_result` events. No field
// takes prompt or response text or `tool_input`; an error is its `error_type`, never its message.
// The one free-text field is a command, `tool_parameters.full_command` as the campaign keeps it,
// and a command line can hold text the model wrote into it, such as a commit message with its
// Co-Authored-By address (the fixture scrubbed one there).
import "../common/telemetry-query.tsp";
using Nami.Api.Contracts.Common;
using TypeSpec.JsonSchema;
namespace Nami.Api.Contracts.Tools;

@doc("Claude Code sessions in the window: agent, models, effort, tokens by type, cost in micro-USD as Claude Code reports it, tool calls and failures, duration.")
@jsonSchema
model SessionsSearchInput extends ToolReadInput {
  @doc("Only sessions whose gen_ai.agent.name resource attribute is this value.")
  @minLength(1)
  @maxLength(256)
  @encodedName("application/json", "agent_name")
  agentName?: string;
}
@doc("Sessions with an event in the window, most recent last_seen first.")
@jsonSchema
model SessionsSearchResult extends ToolResult {
  sessions: ProjectedSessionSummary[];
}

@doc("Read one Claude Code session: its tool calls and commands, its commits, and its test claims with the gate runs that decide them.")
@jsonSchema
model SessionsGetInput extends ToolReadInput {
  @encodedName("application/json", "session_id") sessionId: SessionId;
}
@doc("The requested session and evidence completeness.")
@jsonSchema
model SessionsGetResult extends ToolResult {
  session: ProjectedSessionDetail;
}

@doc("A full git commit id: 40 lowercase hex digits, or 64 in a SHA-256 repository.")
@pattern("^(?:[a-f0-9]{40}|[a-f0-9]{64})(?![\\s\\S])")
@minLength(40)
@maxLength(64)
scalar CommitSha extends string;

@doc("Claude Code's id of one tool call, tool_use_id on its events.")
@minLength(1)
@maxLength(256)
scalar ToolUseId extends string;

@doc("Cost in millionths of a US dollar as Claude Code reports it (cost_usd_micros). An integer, so a sum is exact.")
@minValue(0)
scalar CostUsdMicros extends JsonInteger;

@doc("One Claude Code session (session.id), subagents included. Totals count its events inside the window.")
model SessionSummary {
  @encodedName("application/json", "session_id") sessionId: SessionId;

  @doc("The gen_ai.agent.name resource attribute, when the session sets one.")
  @encodedName("application/json", "agent_name")
  agentName?: string;

  @doc("Models of the session's api_request events, sorted.")
  models: string[];

  @doc("Effort levels of the session's api_request events, sorted.")
  efforts: string[];

  @encodedName("application/json", "input_tokens") inputTokens: TokenCount;
  @encodedName("application/json", "output_tokens") outputTokens: TokenCount;
  @encodedName("application/json", "cache_read_tokens") cacheReadTokens: TokenCount;
  @encodedName("application/json", "cache_creation_tokens") cacheCreationTokens: TokenCount;

  @doc("Sum of cost_usd_micros over the session's api_request events.")
  @encodedName("application/json", "cost_usd_micros")
  costUsdMicros: CostUsdMicros;

  @doc("tool_result events.")
  @encodedName("application/json", "tool_call_count")
  toolCallCount: Count;

  @doc("tool_result events whose success is false.")
  @encodedName("application/json", "failed_tool_call_count")
  failedToolCallCount: Count;

  @doc("From first_seen to last_seen.")
  @encodedName("application/json", "duration_ms")
  durationMs: DurationMs;

  @encodedName("application/json", "first_seen") firstSeen: Timestamp;
  @encodedName("application/json", "last_seen") lastSeen: Timestamp;
}

@doc("A session with its tool calls, commits and test claims.")
model SessionDetail extends SessionSummary {
  @doc("Tool calls in the order they finished.")
  @encodedName("application/json", "tool_calls")
  toolCalls: SessionToolCall[];

  @doc("Commits the session made, oldest first.")
  commits: SessionCommit[];

  @encodedName("application/json", "test_claims") testClaims: TestClaim[];
}

@doc("One tool call, from its tool_result event. Claude Code sends no exit code: success covers the whole command line.")
model SessionToolCall {
  @encodedName("application/json", "tool_use_id") toolUseId: ToolUseId;
  @encodedName("application/json", "tool_name") toolName: string;

  @doc("tool_parameters.full_command of a shell call; absent for other tools.")
  command?: string;

  success: boolean;

  @doc("error_type of a failed call, such as ShellError.")
  @encodedName("application/json", "error_type")
  errorType?: string;

  @encodedName("application/json", "duration_ms") durationMs: DurationMs;
  @encodedName("application/json", "finished_at") finishedAt: Timestamp;

  @doc("The subagent that made the call (agent_id); absent for the session's own agent.")
  @encodedName("application/json", "subagent_id")
  subagentId?: string;

  @encodedName("application/json", "trace_id") traceId?: TraceId;
}

@doc("A commit the session made, from the tool_result of its git commit call.")
model SessionCommit {
  @encodedName("application/json", "commit_sha") commitSha: CommitSha;

  @doc("vcs.ref.head.name when HEAD was a branch; absent on a detached HEAD.")
  branch?: string;

  @encodedName("application/json", "tool_use_id") toolUseId: ToolUseId;

  @doc("When the git commit call finished; git's own commit time is not sent.")
  @encodedName("application/json", "committed_at")
  committedAt: Timestamp;
}

@doc("A claim that the gate passes at one commit, for each commit the session made or ran the gate at. verified: this session ran the gate at the commit and it succeeded; contradicted: a later gate run at the commit failed; unverified: otherwise.")
model TestClaim {
  @encodedName("application/json", "commit_sha") commitSha: CommitSha;
  verdict: "verified" | "contradicted" | "unverified";

  @doc("The gate runs at this commit that decide the verdict, oldest first; empty when none does.")
  evidence: GateRun[];
}

@doc("A gate run: a foreground shell call whose whole command line is ./eng/build.sh Check or ./eng/test.sh, with arguments but without a pipe, &&, ; or ||, so its success stands for exit code 0.")
model GateRun {
  @doc("The session that ran it, which for a contradicting run can be another one.")
  @encodedName("application/json", "session_id")
  sessionId: SessionId;

  @encodedName("application/json", "tool_use_id") toolUseId: ToolUseId;
  command: string;
  success: boolean;
  @encodedName("application/json", "finished_at") finishedAt: Timestamp;
}

@doc("A field projection of SessionSummary; omitted members were not requested.")
model ProjectedSessionSummary {
  ...OptionalProperties<SessionSummary>;
}

@doc("A field projection of SessionDetail; omitted members were not requested.")
model ProjectedSessionDetail {
  ...OptionalProperties<SessionDetail>;
}
