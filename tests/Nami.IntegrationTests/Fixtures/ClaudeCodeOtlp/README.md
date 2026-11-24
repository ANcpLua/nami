# Claude Code OTLP captures, scrubbed

Two captures of the OTLP/HTTP export bodies that the Claude Code CLI 2.1.331 sent to a local
sink on 23 Nov 2026, scrubbed. The tests post them to nami's OTLP/HTTP ingest and read the
stored rows back (`tests/Nami.IntegrationTests/ClaudeCode/`), and M2's acceptance tests expect
its session tools to answer from them (`tests/Nami.EndToEndTests/Sessions/`).

| Folder | Sessions | What it shows |
| --- | --- | --- |
| `signed-in/`, the main fixture | 3 sessions, 35 bodies: 14 logs, 10 metrics, 11 traces | a failing and a passing test run, edits, two commits, a background subagent, cost and tokens |
| `not-signed-in/` | 2 sessions, 6 bodies: 2 logs, 2 metrics, 2 traces | a session that never reached the API: `session.count`, `user_prompt`, `api_error`, an interaction span and a failed `llm_request` span |

Both were recorded on the observer's Mac (macOS, arm64) by `eng/capture/capture.sh` and scrubbed
by `eng/capture/scrub.py`. A new Claude Code version can change what is sent; "Re-recording"
below is the whole procedure.

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
returned `a - b`), `test_calc.py` and `eng/test.sh`, with `--model haiku` (resolved to
`claude-haiku-5-5`), `--setting-sources project` (no user settings; the four `plugin_loaded`
events of each session are Claude Code's built-in plugins),
`--strict-mcp-config`, `--max-budget-usd 1.00`, `--max-turns 15`, `--output-format json` and
`--allowedTools "Bash(python3 -m unittest:*)" "Bash(./eng/test.sh:*)" "Bash(git commit:*)" Read Edit Task Agent BashOutput`.
Each session was a fresh process in a clean environment: `env -i` with `HOME`, `USER`, `LOGNAME`,
`SHELL`, `TERM`, `LANG`, `TMPDIR`, `PATH` and the variables below.

| Set | Session | Task (`eng/capture/prompts/`) | Turns | `total_cost_usd` |
| --- | --- | --- | --- | --- |
| signed-in | s1 | run the tests (they fail), read and fix `add` with Edit, run the tests again (they pass), commit | 6 | 0.00319639 |
| signed-in | s2 | start one `general-purpose` subagent that reads both files, add `sub` with a test, run the tests, commit | 8 | 0.00484208 |
| signed-in | s3 | run the tests once more and report the count | 2 | 0.00143670 |
| not-signed-in | A | the s1 task; printed `Not logged in · Please run /login` | 1 | 0 |
| not-signed-in | B | the s1 task with a `TRACEPARENT`; printed the same | 1 | 0 |

- In s2 the subagent ran in the background (`subagent_completed` has `is_async` true). Its
  completion started a second interaction in the same session, so s2 has two `user_prompt`
  events and two traces. Two of s2's ten tool calls and two of its nine requests are the
  subagent's.
- In s3 the model ran `python3 -m unittest -q 2>&1 | tail -n 5`.
- Sessions A and B started with `CLAUDE_CONFIG_DIR` pointing at an empty folder, so Claude Code
  found no credential and no account: their records carry `user.id` but no `user.email`,
  `user.account_uuid`, `user.account_id`, `organization.id` or `anthropic.plan`. B inherited
  `TRACEPARENT`, so its interaction span has a remote parent (`parent.source` is `env`) and its
  startup events carry that trace context.

## Sessions outside the fixture: s4 to s7

`eng/capture/prompts/` holds four more tasks. They are not in this fixture and no test reads
them. They are there to record a case when a milestone needs to see what Claude Code really
sends for it. M2's claim check is the first reader: the fixture has no gate run after a commit,
so `tests/Nami.EndToEndTests/Sessions/ClaimCheckAcceptanceTests.cs` plants its gate runs.

| Session | Task | The case it records |
| --- | --- | --- |
| s4 | fix `add`, commit, then run `./eng/test.sh` in the foreground; it passes | a passing gate run at the session's own commit: a verified claim |
| s5 | run `./eng/test.sh` in the background and read its output | a background gate run, which must not count as verified |
| s6 | change the expected value in `test_mul` without committing, then run `./eng/test.sh`; it fails | after s4, a failing gate run at the same commit: a contradicted claim |
| s7 | use the project skill `calc-summary` | a skill activation |

`SESSIONS` names the signed-in sessions of a capture. They run in that order in one repository,
so s5 and s6 need s4 before them. For s7 the script writes the skill into the throwaway
repository and allows the Skill tool, for that session only. `scrub.py` takes the session names
from the capture and refuses to write other sessions than s1 to s3 into this folder, so name
another one, and delete the raw folder afterwards:

```bash
SESSIONS="s4 s5 s6 s7" bash eng/capture/capture.sh ~/nami-capture-raw
uv run eng/capture/scrub.py ~/nami-capture-raw ~/nami-capture-scrubbed
```

A recording of the four on 24 Nov 2026 (Claude Code 2.1.331, about one cent, not kept) showed:

- s4: `cat calc.py`, Edit, `git commit`, then `./eng/test.sh` with `success` `"true"`.
- s5: the Bash `tool_result` came after 344 ms with `success` `"true"`: it reports that the
  command started, not how the tests ended. Only `tool_input` says `"run_in_background":true`;
  `tool_parameters`, `full_command` and the `claude_code.tool` span are the same as for a
  foreground run. The model then read the output with the Read tool from
  `<tmp>/claude-<uid>/<the repository's path with dashes>/<session id>/tasks/<task>.output`,
  not with a tool for background output. No event or span reported how the background command
  ended.
- s6: Read, Edit, then `./eng/test.sh` with `success` `"false"` and `error`
  `Shell command failed`.
- s7: `skill_activated` with `skill.name` `calc-summary`, `invocation_trigger`
  `claude-proactive` and `skill.source` `projectSettings`, between the `tool_decision` and the
  `tool_result` of the Skill tool call; then a Read.

## Variables

```text
CLAUDE_CODE_ENABLE_TELEMETRY=1
CLAUDE_CODE_ENHANCED_TELEMETRY_BETA=1
OTEL_METRICS_EXPORTER=otlp
OTEL_LOGS_EXPORTER=otlp
OTEL_TRACES_EXPORTER=otlp
OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf
OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4399
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

`eng/capture/scrub.py` decodes the bodies with `opentelemetry-proto` 1.45.2. Every string value
(resource, scope, data point, log body and attributes, span, span event and link attributes,
span status) and every trace, span and span-link id goes through one replacement table shared
by both folders, so a fake that appears in both stands for the same real value. A replacement
is numbered in the order its real value is first met, `not-signed-in` first.

| Real value | Replacement |
| --- | --- |
| `user.email` and every other e-mail address, including the commit trailer inside `full_command` | `scrubbed-email-N` |
| `user.id` | 64 hex digits, `0…0N` |
| `user.account_id` | `user_scrubbedNNNN` |
| Every UUID: `session.id`, `gen_ai.conversation.id`, `user.account_uuid`, `organization.id`, `prompt.id`, `message.uuid`, `client_request_id` | `00000000-0000-4000-8000-00000000000N` |
| The subagent id: `agent_id`, and `gen_ai.conversation.id` on the subagent's spans | `afade…N`, 17 characters |
| Request ids (`request_id`, `gen_ai.response.id`) and tool use ids (`tool_use_id`, `gen_ai.tool.call.id`) | `req_011FIXTURE…N` and `toolu_01FIXTURE…N`, same length |
| Commit SHAs, whole or abbreviated: `vcs.ref.head.revision` and `git_commit_id` in `tool_parameters` | `c0ffee…N`, 40 hex digits |
| Trace and span ids, also those of span links | `fade…N` of the same length; parent links and the log-to-span correlation are kept |
| The repository's path, the temporary directory, the home directory | `/workspace/calc`, `/scratch`, `/home/fixture` |
| The folder of Claude Code's own session files, `<tmp>/claude-<uid>/<the repository's path with dashes>` (s5 only; none in this fixture) | `/scratch/claude` |
| Host names, API keys, bearer credentials, JWTs | `fixture-host-N`, `<scrubbed-…>` (none occurred) |

Everything else is unchanged: timestamps, durations, counts, tokens, costs, model and plugin
names, commands and error text. The messages are re-encoded with deterministic serialization.

The script writes into a temporary folder and checks the files there. They replace the fixture
only when every check passes:

- no at sign, home or temporary path and no credential pattern in any `.json` file or in the
  decoded strings of any `.pb`;
- no UUID, request, tool use or message id and no 40- or 64-digit hex value other than the
  placeholders above;
- none of the real values the table replaced, no host name and no name of the person (login,
  full name, git user name) in any file, as text or as bytes;
- every `.pb` decodes to exactly its `.json` and keeps the structure of the original body
  (records, attribute keys, numbers, the lengths of ids).

The table is never written anywhere. A check finds only what it has a pattern or a recorded
value for, so a new kind of id needs a pattern in `scrub.py` before its capture is committed.

## Cost cross-check (signed-in)

| Session | `api_request` events | sum of `cost_usd` | sum of `cost_usd_micros` | sum of `claude_code.cost.usage` | CLI `total_cost_usd` |
| --- | --- | --- | --- | --- | --- |
| s1 | 6 | 0.00319639 | 3196 | 0.00319639 | 0.00319639 |
| s2 | 9, 2 of them from the subagent | 0.00484208 | 4842 | 0.00484208 (main 0.00390995, subagent 0.00093213) | 0.00484208 |
| s3 | 2 | 0.00143670 | 1437 | 0.00143670 | 0.00143670 |

`cost_usd` is a double and `cost_usd_micros` an integer on the wire. Each `cost_usd_micros` is
`cost_usd` times one million, rounded, so their sum differs from the total by less than one
micro-dollar per request. Input, output, cache read and cache creation tokens agree exactly
between the `api_request` events, `claude_code.token.usage` and the CLI's result.

## Token usage on `claude_code.llm_request` (since 2.1.331)

Each of the 17 answered requests carries its usage twice on its span: under Claude Code's own
keys and, since 2.1.331, under the names of the GenAI conventions. The two input counts differ
by design.

| GenAI attribute | Value | Claude Code's key on the same span |
| --- | --- | --- |
| `gen_ai.usage.input_tokens` | `input_tokens` + `cache_read_tokens` + `cache_creation_tokens` | `input_tokens` holds the uncached part only |
| `gen_ai.usage.output_tokens` | the same number | `output_tokens` |
| `gen_ai.usage.cache_read.input_tokens` | the same number | `cache_read_tokens` |
| `gen_ai.usage.cache_write.input_tokens` | the same number | `cache_creation_tokens` |
| `gen_ai.response.model` | `claude-haiku-5-5`, equal to `gen_ai.request.model` | `model` |
| `gen_ai.operation.name` | `chat` | none |

All six are integers or strings as the conventions define them. A failed request (A and B) has
`gen_ai.operation.name` and `gen_ai.request.model` and no usage. No span carries
`gen_ai.usage.reasoning.output_tokens`. nami copies the six into the span columns
`gen_ai_operation_name`, `gen_ai_response_model`, `gen_ai_input_tokens`, `gen_ai_output_tokens`,
`gen_ai_cache_read_input_tokens` and `gen_ai_cache_write_input_tokens`
(`Ingest_ClaudeCodeCapture_LlmRequestSpansFillTheGenAiColumns`). The `api_request` events and
the `claude_code.token.usage` metric still carry Claude Code's keys only.

## Notes

- A log record's event name is in its body (`claude_code.tool_result`) and in the attribute
  `event.name` (`tool_result`). The OTLP `event_name` field is empty and severity is unset;
  nami stores both as sent: the `event_name` field in its column and the `event.name` attribute
  among the attributes. A tool that needs an event's name reads the field and, when that is
  empty, the attribute. Startup events carry no trace context, except under an inherited
  `TRACEPARENT` (B); the others carry the trace and span id of the interaction or tool span
  they belong to.
- nami stores these records as they were sent: every key under its own name, in the type and
  container it was sent in, `tool_parameters` whole. Ingest removes credentials and applies its
  size bounds, nothing else. What the names mean is recorded in
  `docs/telemetry/claude-code-anthropic-reference.md`.
- `gen_ai.agent.name` is a resource attribute and is also copied onto every data point, log
  record and span, next to `session.id`, `user.id`, `terminal.type`, `anthropic.provider`,
  `anthropic.custom_base_url` and, when signed in, `user.email`, `user.account_uuid`,
  `user.account_id`, `organization.id` and `anthropic.plan`.
- A subagent's records carry the parent's `session.id`; its events and its `llm_request` and
  `claude_code.tool` spans also carry `agent_id`. Its requests have
  `agent.name` `general-purpose` and `query_source` `agent:builtin:general-purpose` on
  `api_request` (`subagent` on the cost and token metrics). Its spans hang under the parent's
  Agent `tool.execution` span and carry `gen_ai.conversation.id` equal to `agent_id`.
- Every answered `claude_code.llm_request` span has one span link with the attribute
  `link.type`, to a trace of its own: the link's trace id occurs nowhere else in the capture. A
  failed request has none.
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

## Re-recording

Re-record both folders together when a Claude Code release changes its telemetry (the
changelog names `claude_code.` spans, events or metrics, or `gen_ai.` attributes), on a Mac
where `claude` is signed in. It costs about one cent (Haiku).

1. Record, into an empty folder outside every git repository:

   ```bash
   bash eng/capture/capture.sh ~/nami-capture-raw
   ```

   The model decides how it works, so a recording can differ from the table under "Sessions".
   The tests need these cases; record again, into another empty folder, until all of them show:
   - s1: five tool calls in this order: a failing `python3 -m unittest -q`, Read, Edit, a
     passing `python3 -m unittest -q`, one successful `git commit`;
   - s2: one subagent with `is_async` true and two interactions, every tool call successful,
     one `git commit` as the last tool call;
   - s3: one Bash call.
2. Scrub into this folder; the script replaces the bodies, both manifests and
   `cli-results.json`. When one of its checks fails it changes nothing here and names the file:

   ```bash
   uv run eng/capture/scrub.py ~/nami-capture-raw
   ```

3. Delete the raw folder: it holds the account's e-mail address and ids.
4. Compare what is sent now with what was sent before, per record kind and attribute key, for
   example with `git diff --stat` and a look at one `-traces.json` and one `-logs.json`. Record
   every difference in `docs/telemetry/claude-code-anthropic-reference.md` (the version under
   "Observed", §3.2 and the crosswalk in §4) and in this file.
5. Update what names the capture: the version, date, counts and tables in this file, and the
   pinned values in the tests. Ids and numbers are pinned in
   `tests/Nami.IntegrationTests/ClaudeCodeCaptureTests.cs`,
   `tests/Nami.IntegrationTests/ClaudeCode/ClaudeCodeIngestTests.cs` (events and failed spans
   per folder, answered requests), `tests/Nami.IntegrationTests/ClaudeCode/ClaudeCodeCapture.cs`,
   `tests/Nami.IntegrationTests/RequestMessageBoundTests.cs` (bodies and the largest request),
   `tests/Nami.Tests/Infrastructure/ClaudeCodeCapture.cs`,
   `tests/Nami.EndToEndTests/Sessions/SignedInSessions.cs` (session ids, tokens, cost, tool
   calls, commands, duration bounds, the window start),
   `tests/Nami.EndToEndTests/Sessions/SessionsSearchAcceptanceTests.cs` (the window starts
   between and after the sessions) and `tests/Nami.EndToEndTests/Sessions/ContentAtIngestTests.cs`
   (the 16 `tool_result` events and a sentence of the subagent's prompt). The runs that
   `ClaimCheckAcceptanceTests.cs` plants use the event sequences 101 and 103, which must stay
   above s1's highest `event.sequence` (22 now). The version or date is also named in
   `ClaudeCode/ClaudeCodeContent.cs`, `Sessions/SessionTools.cs` and a comment in
   `src/nami.collector.ingestion/OtlpConverter.cs`. `README.md` quotes the messages per record
   under its ingest bounds, and `docs/testing.md` the capture's date.
6. When a new attribute has a column in `contracts/models/storage-rows.tsp`, or should get one,
   that is a contract change: edit the TypeSpec model and its `SpanHotAttributeProjection`,
   regenerate with `./eng/build.sh Contracts`, and pin the stored value with a test beside
   `Ingest_ClaudeCodeCapture_LlmRequestSpansFillTheGenAiColumns`. A key without a column needs
   nothing: it is stored among the attributes as it was sent.
7. Run `./eng/build.sh Check --Configuration Release`. The `m2-acceptance` lane cannot pass
   before M2's tools exist; its expected values in `SignedInSessions.cs` are checked against
   the fixture by reading it, not by a run.
