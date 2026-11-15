# Claude Code OTLP captures, scrubbed

Two captures of the OTLP/HTTP export bodies that the Claude Code CLI 2.1.325 sent to a local
sink on 15 Nov 2026, scrubbed. They are input for the M2 design and for the fixture tests of
finish-line item 6: `tests/Nami.IntegrationTests/ClaudeCode/ClaudeCodeIngestTests.cs` posts them to
nami's OTLP/HTTP ingest and reads the stored rows back.

| Folder | Captured on | Sessions | What it shows |
| --- | --- | --- | --- |
| `signed-in/`, the main fixture | the observer's Mac (macOS, arm64), signed in | 3 sessions, 53 bodies: 19 logs, 18 metrics, 16 traces | a failing and a passing test run, edits, two commits, a background subagent, cost and tokens |
| `not-signed-in/` | a Linux cloud container without a credential | 2 sessions, 6 bodies | a session that never reached the API: `session.count`, `user_prompt`, `api_error`, an interaction span and failed `llm_request` spans |

## Files

Each folder holds:

- `NNNNN-<signal>.pb`: one POST body each, as nami's `/v1/metrics`, `/v1/logs` and `/v1/traces`
  receive it with `Content-Type: application/x-protobuf` (`ExportMetricsServiceRequest`,
  `ExportLogsServiceRequest`, `ExportTraceServiceRequest`), re-encoded after scrubbing.
- `NNNNN-<signal>.json`: the same message as OTLP/JSON (lowerCamelCase names, hex trace and span
  ids, enums as integers, 64-bit integers as strings).
- `manifest.json`: the bodies in arrival order with signal, session label, the session's
  (scrubbed) `session.id`, content type, exporter user agent, arrival time and the SHA-256 of
  each `.pb`. Every body belongs to exactly one session.

`signed-in/cli-results.json` holds the final result object that `claude -p --output-format json`
printed for each session, reduced to numbers: turns, durations, `total_cost_usd`, tokens and
cost per model, subagents spawned, and the scrubbed session id. The result text and the other
ids are left out.

The exporter was `OTel-OTLP-Exporter-JavaScript/0.208.0`, without compression. Metrics are
delta sums.

## Sessions

All sessions ran `claude -p` in a throwaway git repository with `calc.py` (whose `add`
returned `a - b`) and `test_calc.py`, with `--model haiku` (resolved to `claude-haiku-5-5`),
`--strict-mcp-config`, `--max-budget-usd 1.00` and `--output-format json`. Each session was a
fresh process.

| Set | Session | Task | Turns | `total_cost_usd` |
| --- | --- | --- | --- | --- |
| signed-in | s1 | run the tests (they fail), read and fix `add` with Edit, run the tests again (they pass), commit | 6 | 0.00332877 |
| signed-in | s2 | start one `general-purpose` subagent that reads both files, add `sub` with a test, run the tests, commit | 8 | 0.00576223 |
| signed-in | s3 | run the tests once more and report the count | 2 | 0.00127774 |
| not-signed-in | A | the s1 task; printed `Not logged in · Please run /login` | 1 | 0 |
| not-signed-in | B | the s1 task; printed `Authentication error · This may be a temporary network issue, please try again` | 1 | 0 |

- The signed-in sessions used the runner of session A (a clean environment plus the variables
  below) with endpoint `http://127.0.0.1:4399`, `--setting-sources project` (no user settings,
  hooks or plugins), `--max-turns 15` and
  `--allowedTools "Bash(python3 -m unittest:*)" "Bash(git commit:*)" Read Edit Task Agent`.
- In s2 the subagent ran in the background (`subagent_completed` has `is_async` true). Its
  completion started a second interaction in the same session, so s2 has two `user_prompt`
  events and two traces.
- In s3 the model ran `python3 -m unittest -q 2>&1 | tail -n 5`.
- Session A ran in a clean environment and session B in the capture shell's own environment;
  both used endpoint `http://127.0.0.1:4318`, `--max-turns 10` and
  `--allowedTools "Bash(python3 -m unittest*),Bash(git commit*),Read,Edit"`. B inherited
  `TRACEPARENT`, so its interaction span has a remote parent (`parent.source` is `env`) and its
  startup events carry that trace context.

## Variables

```text
CLAUDE_CODE_ENABLE_TELEMETRY=1
CLAUDE_CODE_ENHANCED_TELEMETRY_BETA=1
OTEL_METRICS_EXPORTER=otlp
OTEL_LOGS_EXPORTER=otlp
OTEL_TRACES_EXPORTER=otlp
OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf
OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4399   # not-signed-in: http://127.0.0.1:4318
OTEL_METRIC_EXPORT_INTERVAL=2000
OTEL_LOGS_EXPORT_INTERVAL=1000
OTEL_TRACES_EXPORT_INTERVAL=1000
OTEL_LOG_TOOL_DETAILS=1
OTEL_RESOURCE_ATTRIBUTES=gen_ai.agent.name=fixture-builder
DISABLE_AUTOUPDATER=1
```

These match the team's launch script except for the shorter intervals, the agent name and
`DISABLE_AUTOUPDATER`. No content variable was set: `OTEL_LOG_USER_PROMPTS`,
`OTEL_LOG_ASSISTANT_RESPONSES`, `OTEL_LOG_TOOL_CONTENT`, `OTEL_LOG_RAW_API_BODIES` and
`ENABLE_BETA_TRACING_DETAILED` were unset. `prompt` and `prompt_text` on `user_prompt`,
`response` on `assistant_response` and `user_prompt` on the interaction span are `<REDACTED>`.
Other content still arrives through the tool details; see the notes below.

## What was scrubbed, and how

The bodies were decoded with `opentelemetry-proto` 1.45.2. Every string value (resource, scope,
data point, log body and attributes, span, span event and link attributes, span status) went
through one replacement table shared by both folders, so a fake that appears in both stands for
the same real value. Both captures came from the same Claude account; `user.id` differs per
installation.

| Real value | Replacement |
| --- | --- |
| `user.email` and every other e-mail address, including the commit trailer inside `full_command` | `scrubbed-email-N` |
| `user.id` | 64 hex digits, `0…0N` |
| `user.account_id` | `user_scrubbedNNNN` |
| Every UUID: `session.id`, `gen_ai.conversation.id`, `user.account_uuid`, `organization.id`, `prompt.id`, `message.uuid`, `client_request_id` | `00000000-0000-4000-8000-00000000000N` |
| The subagent id: `agent_id`, and `gen_ai.conversation.id` on the subagent's spans | `afade…N`, 17 characters |
| Request ids (`request_id`, `gen_ai.response.id`) and tool use ids (`tool_use_id`, `gen_ai.tool.call.id`) | `req_011FIXTURE…N` and `toolu_01FIXTURE…N`, same length |
| Commit SHAs: `vcs.ref.head.revision` and `git_commit_id` in `tool_parameters` | `c0ffee…N`, 40 hex digits |
| Trace and span ids | `fade…N` of the same length; parent links and the log-to-span correlation are kept |
| The repository path in the temporary directory, other temporary-directory and home paths | `/workspace/calc`, `/scratch/…`, `/home/fixture` |
| Host names, API keys, bearer credentials, JWTs | `fixture-host-N`, `<scrubbed-…>` (none occurred) |

Everything else is unchanged: timestamps, durations, counts, tokens, costs, model and plugin
names, commands and error text. The messages were then re-encoded with deterministic
serialization.

Checks after scrubbing, all passed: no at sign, API-key prefix, bearer credential, macOS home
or temporary-folder path, or temporary-directory path in any `.json` file or in the decoded
strings of any `.pb`; none of the real values (identity values, ids, SHAs and paths, read from a file outside
the repository) and none of the original trace, span or span-link ids in any file, as text or
as bytes;
every `.pb` decodes to exactly its `.json` and keeps the structure of the original body
(records, attribute keys, timestamps, parent links). The same check flags an unscrubbed body.

## Cost cross-check (signed-in)

| Session | `api_request` events | sum of `cost_usd` | sum of `cost_usd_micros` | sum of `claude_code.cost.usage` | CLI `total_cost_usd` |
| --- | --- | --- | --- | --- | --- |
| s1 | 6 | 0.00332877 | 3329 | 0.00332877 | 0.00332877 |
| s2 | 9, 2 of them from the subagent | 0.00576223 | 5762 | 0.00576223 (main 0.00415660, subagent 0.00160563) | 0.00576223 |
| s3 | 2 | 0.00127774 | 1277 | 0.00127774 | 0.00127774 |

`cost_usd` is a double and `cost_usd_micros` an integer on the wire. Each `cost_usd_micros` is
`cost_usd` times one million, rounded, so their sum differs from the total by less than one
micro-dollar per request. Input, output, cache read and cache creation tokens agree exactly
between the `api_request` events, `claude_code.token.usage` and the CLI's result.

## Notes for M2

- A log record's event name is in its body (`claude_code.tool_result`) and in the attribute
  `event.name` (`tool_result`). The OTLP `event_name` field is empty and severity is unset;
  nami's converter takes the name from `event_name` and, when that is empty, from the deprecated
  `event.name` attribute. On a record under one of Claude Code's instrumentation scopes it
  stores the name the registry defines, `claude_code.tool_result`, and keeps the body only when
  the body is that name. Startup events carry no trace context; the
  others carry the trace and span id of the interaction or tool span they belong to.
- What nami stores of these records follows the registry (`registry/source/claude_code`,
  `docs/semconv/claude_code.md`): Claude Code's unprefixed keys only under its three scopes,
  string values in the declared type, `bash_command`, `full_command` and `subagent_type` out
  of `tool_parameters` and nothing else of it, `full_command` up to 4096 UTF-8 bytes, and a
  span's status code without its status message, into which Claude Code copies the text of
  `error`.
- `gen_ai.agent.name` is a resource attribute and is also copied onto every data point, log
  record and span, next to `session.id`, `user.id`, `user.email`, `user.account_uuid`,
  `user.account_id`, `organization.id`, `terminal.type`, `anthropic.provider`,
  `anthropic.custom_base_url` and, when signed in, `anthropic.plan`.
- A subagent's records carry the parent's `session.id` plus `agent_id`. Its requests have
  `agent.name` `general-purpose` and `query_source` `agent:builtin:general-purpose` on
  `api_request` (`subagent` on the cost and token metrics). Its spans hang under the parent's
  Agent `tool.execution` span and carry `gen_ai.conversation.id` equal to `agent_id`.
- A failing command has `success` `"false"`, `error_type` `ShellError` and `error`
  `Shell command failed` on `tool_result`, and a `tool.execution` span with status error. No
  exit code is sent, and a pipeline reports its last command (s3).
- The commit SHA and branch are on the `tool_result` of the `git commit` command
  (`vcs.ref.head.revision`, `vcs.ref.head.name`, and `git_commit_id`, `git_branch` in
  `tool_parameters`). `claude_code.commit.count` carries no SHA.
- With `OTEL_LOG_TOOL_DETAILS=1`, content arrives in `tool_input` (commands, file paths, the
  old and new text of edits, and the Agent tool's whole `prompt` for the subagent), in
  `tool_parameters` and on the `claude_code.tool` span (`full_command` with commit messages,
  the model's `description`, `file_path`), and in error text.
- On `tool_result`, `success`, `duration_ms` and the two size attributes are strings; the
  numbers on `api_request` and on spans are integers and doubles.
