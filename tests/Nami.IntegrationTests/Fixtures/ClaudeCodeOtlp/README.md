# Claude Code OTLP capture, scrubbed

Real OTLP/HTTP export bodies from the Claude Code CLI 2.1.325, captured on 15 Nov 2026 in a
Linux cloud container (not on the Mac) by a local sink on `127.0.0.1:4318`, then scrubbed.
No test reads these files yet. They are input for the M2 design and for the fixture test of
finish-line item 6.

## Files

- `NNNNN-<signal>.pb`: one POST body each, as nami's `/v1/metrics`, `/v1/logs` and `/v1/traces`
  receive it with `Content-Type: application/x-protobuf` (`ExportMetricsServiceRequest`,
  `ExportLogsServiceRequest`, `ExportTraceServiceRequest`), re-encoded after scrubbing.
- `NNNNN-<signal>.json`: the same message as OTLP/JSON (lowerCamelCase names, hex trace and span
  ids, enums as integers, 64-bit integers as strings).
- `manifest.json`: the files in arrival order with signal, content type, exporter user agent,
  arrival time and the SHA-256 of each `.pb`.

The exporter was `OTel-OTLP-Exporter-JavaScript/0.208.0`, without compression; metrics use
delta temporality.

## What the capture lacks

Both sessions failed to authenticate, because the CLI has no credential in that container.
Session A printed `Not logged in · Please run /login`, session B
`Authentication error · This may be a temporary network issue, please try again`; the debug
log said `Could not resolve authentication method`. No API request succeeded and no tool ran,
so there is no `api_request`, `tool_result` or `tool_decision` event, no
`claude_code.cost.usage`, `claude_code.token.usage`, `claude_code.commit.count` or
`claude_code.lines_of_code.count` data point, and no `claude_code.tool` span. A capture from a
signed-in session that runs a failing and a passing command, edits a file, commits and starts
a subagent is still needed.

## Sessions

| Files | Session | Environment |
| --- | --- | --- |
| 00001 to 00003 | A | A clean environment: `PATH`, `HOME`, the proxy and CA variables, and the variables below. |
| 00004 to 00006 | B | The capture shell's own environment plus the variables below. It inherited `TRACEPARENT`, so its interaction span has a remote parent (`parent.source` is `env`) and its startup events carry that trace context. |

Both ran `claude -p` in a throwaway git repository with one prompt of 426 characters,
`--model haiku` (resolved to `claude-haiku-5-5`), `--max-turns 10`, a narrow `--allowedTools`
list and `--strict-mcp-config`.

## Variables

```text
CLAUDE_CODE_ENABLE_TELEMETRY=1
CLAUDE_CODE_ENHANCED_TELEMETRY_BETA=1
OTEL_METRICS_EXPORTER=otlp
OTEL_LOGS_EXPORTER=otlp
OTEL_TRACES_EXPORTER=otlp
OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf
OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4318
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
`ENABLE_BETA_TRACING_DETAILED` were unset. The prompt text appears nowhere: `prompt` and
`prompt_text` on `user_prompt` and `user_prompt` on the `claude_code.interaction` span are
`<REDACTED>`; only the lengths remain.

## What was scrubbed, and how

The bodies were decoded with `opentelemetry-proto` 1.45.2. Every string value (resource,
scope, data point, log body and attributes, span, span event and link attributes, span status)
went through one replacement table, so a real value gets the same fake in every file:

| Real value | Replacement |
| --- | --- |
| `user.email` | `scrubbed-email-N` |
| `user.id` | 64 hex digits, `0…0N` |
| `user.account_id` | `user_scrubbedNNNN` |
| Every UUID: `session.id`, `gen_ai.conversation.id`, `user.account_uuid`, `organization.id`, `prompt.id`, `message.uuid`, `client_request_id` | `00000000-0000-4000-8000-00000000000N` |
| Trace and span ids, including the inherited remote parent | `fade…N` of the same length; parent links and the log-to-span correlation are kept |
| Host name, home and temporary-directory paths | `fixture-host-N`, `/home/fixture`, `/workspace/calc`, `/scratch` (none occurred) |
| API keys, bearer credentials, JWTs | `<scrubbed-token>`, `<scrubbed-authorization>` (none occurred) |

Timestamps, durations, counts, model and plugin names and the error text are unchanged. The
messages were then re-encoded with deterministic serialization.

Checks after scrubbing, all passed: no at sign, API-key prefix, bearer credential or
temporary-directory path in any `.json` file or in the decoded strings of any `.pb`; none of 29
real identity values (the replaced ones plus the account, host, path and parent-session values
known in the container, read from a file outside the repository) and none of the original
trace or span ids in any file, as text or as bytes; every `.pb` decodes to exactly its `.json`
and keeps the structure of the original body (records, attribute keys, timestamps, parent
links). The same check flags an unscrubbed body.

## Notes for M2

- A log record's event name is in its body (`claude_code.user_prompt`) and in the attribute
  `event.name` (`user_prompt`). The OTLP `event_name` field is empty and severity is unset;
  nami's converter reads only `event_name` today.
- `gen_ai.agent.name` arrives as a resource attribute and is copied onto every data point, log
  record and span, next to the standard attributes `session.id`, `user.id`, `user.email`,
  `user.account_uuid`, `user.account_id`, `organization.id`, `terminal.type`,
  `anthropic.provider` and `anthropic.custom_base_url`.
- `gen_ai.conversation.id` on spans equals `session.id`.
- Attribute types are mixed: `prompt_length` is a string, `user_prompt_length`, `duration_ms`
  and `event.sequence` are integers, `safe_mode` is the string `false`.
- Scopes: `com.anthropic.claude_code` (metrics), `com.anthropic.claude_code.events` (logs) and
  `com.anthropic.claude_code.tracing` 1.0.0 (spans).
