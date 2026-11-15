// Typed rows for sessions_search and sessions_get, one per Claude Code event of two kinds.
// The events themselves stay in `logs`, which is authoritative; these tables are derived from
// it (`derived: true`), so a schema change resets and rebuilds them instead of refusing a
// database that holds data. Each row keeps the `log_id` of its event: the key that makes an
// OTLP retry a no-op and a rebuild repeatable. No column holds prompt, response or tool input
// text; a command is `tool_parameters.full_command` and an error is its `error_type`.
import "../common/values.tsp";
import "../emitters/csharp/lib/main.tsp";
using Nami.Api.Schema.Emit.CSharp;
namespace Nami.Models.Storage.Rows;

// One `claude_code.api_request` event: the source of a session's models, effort, tokens and
// cost. The subagent's requests share the session and are counted with it, as the CLI does.
@duckDbTable(#{ name: "claude_code_api_requests", derived: true, conflict: "ignore" })
model ClaudeCodeApiRequestRow {
  @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0, sqlType: "VARCHAR(128)" })
  projectId: Nami.Models.Values.ProjectId;
  @duckDbColumn(#{ order: 1, primaryKeyOrdinal: 1 })
  logId: string;
  @duckDbColumn(#{ order: 2 })
  sessionId: string;
  @duckDbColumn(#{ order: 3 })
  timeUnixNano: uint64;
  @duckDbColumn(#{ order: 4 })
  serviceName?: string | null;
  // gen_ai.agent.name
  @duckDbColumn(#{ order: 5 })
  agentName?: string | null;
  @duckDbColumn(#{ order: 6 })
  `model`: string;
  @duckDbColumn(#{ order: 7 })
  effort?: string | null;
  @duckDbColumn(#{ order: 8 })
  inputTokens: int64;
  @duckDbColumn(#{ order: 9 })
  outputTokens: int64;
  @duckDbColumn(#{ order: 10 })
  cacheReadTokens: int64;
  @duckDbColumn(#{ order: 11 })
  cacheCreationTokens: int64;
  // cost_usd_micros as sent; cost_usd and the claude_code.cost.usage metric are not added to it.
  @duckDbColumn(#{ order: 12 })
  costUsdMicros: int64;
}

// One `claude_code.tool_result` event: a tool call, its command, and the commit when the call
// was a git commit. `success` and `duration_ms` arrive as strings and are converted at ingest.
@duckDbTable(#{ name: "claude_code_tool_results", derived: true, conflict: "ignore" })
model ClaudeCodeToolResultRow {
  @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0, sqlType: "VARCHAR(128)" })
  projectId: Nami.Models.Values.ProjectId;
  @duckDbColumn(#{ order: 1, primaryKeyOrdinal: 1 })
  logId: string;
  @duckDbColumn(#{ order: 2 })
  sessionId: string;
  @duckDbColumn(#{ order: 3 })
  timeUnixNano: uint64;
  @duckDbColumn(#{ order: 4 })
  serviceName?: string | null;
  // The record's trace context: the interaction or subagent span it belongs to.
  @duckDbColumn(#{ order: 5 })
  traceId?: string | null;
  @duckDbColumn(#{ order: 6 })
  toolUseId: string;
  @duckDbColumn(#{ order: 7 })
  toolName: string;
  // 1 when success is "true", 0 when it is "false".
  @duckDbColumn(#{ order: 8 })
  success: uint8;
  @duckDbColumn(#{ order: 9 })
  durationMs: int64;
  @duckDbColumn(#{ order: 10 })
  errorType?: string | null;
  // tool_parameters.full_command; the model's `description` beside it is not kept.
  @duckDbColumn(#{ order: 11 })
  command?: string | null;
  // agent_id: set on a subagent's calls.
  @duckDbColumn(#{ order: 12 })
  subagentId?: string | null;
  // vcs.ref.head.revision on the tool_result of a git commit.
  @duckDbColumn(#{ order: 13 })
  commitSha?: string | null;
  // vcs.ref.head.name when vcs.ref.head.type is branch.
  @duckDbColumn(#{ order: 14 })
  branch?: string | null;
  // 1 when the Bash call's input set run_in_background, the one key kept from tool_input; a
  // background gate run never verifies a claim. Not in the 15 Nov fixture, which has no such call.
  @duckDbColumn(#{ order: 15 })
  runInBackground?: uint8 | null;
}
