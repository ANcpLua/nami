# Claude Code & Anthropic OTLP Telemetry — Semantic Convention Reference

**Core OpenTelemetry Semantic Conventions:**
[`v1.45.0`](https://github.com/open-telemetry/semantic-conventions/releases/tag/v1.45.0)  
**GenAI Semantic Conventions:**
[`open-telemetry/semantic-conventions-genai`](https://github.com/open-telemetry/semantic-conventions-genai). The core
repository deprecated every `gen_ai.*` definition and moved it there. The GenAI repository publishes no release tags,
so pin it by commit; using it together with core v1.45.0 is this project's choice, not a pairing that upstream
publishes. Every `gen_ai.*` name below has Development stability.  
**Protocol:** [OpenTelemetry Protocol (OTLP)](https://opentelemetry.io/docs/specs/otlp/)  
**Vendor sources, read on 16 Nov 2026:**
- Claude Code: [Monitoring](https://code.claude.com/docs/en/monitoring-usage).
- Claude API: [OpenTelemetry span reference](https://platform.claude.com/docs/en/api/opentelemetry/spans) and
  [OpenTelemetry tracing](https://platform.claude.com/docs/en/manage-claude/opentelemetry).
- Claude Code: [changelog](https://github.com/anthropics/claude-code/blob/main/CHANGELOG.md), read up to
  2.1.331 on 23 Nov 2026.
- Observed: the capture of Claude Code 2.1.331 of 23 Nov 2026 in
  `tests/Nami.IntegrationTests/Fixtures/ClaudeCodeOtlp`. When a release changes the telemetry, record the capture
  again as that folder's README says ("Re-recording"), then bring this document up to what was sent.

Claude Code traces and Claude API tracing are both beta, and their names can change between versions.

This document separates three kinds of name: **standard OTLP fields**, **OpenTelemetry semantic-convention
attributes**, and **vendor-defined fields**. The vendor fields come from two emitters: the **Claude Code CLI**, and the
**Anthropic SDK together with the Claude API's server-side tracing**. The vendor names are kept from the supplied
telemetry inventory and the vendor documentation above. OpenTelemetry guarantees neither their presence nor their
meaning.

This document records names and meanings only. It does not define application-specific schemas, capture modes,
persistence policies or verification states. It renames nothing: data keeps the name it was sent with. Where a vendor
name and a convention name describe the same or a related quantity, §4 states how they relate.

## 1. OTLP Data Model

- OTLP traces include resource and instrumentation-scope information, spans, span events and span links.
  - Span **name** and span **kind** are separate fields. `anthropic.messages.create` with kind CLIENT and
    `anthropic.messages.create` with kind SERVER are two spans with one name.
  - A registry group id such as `span.anthropic.messages.create.client` is not a span name.
  - Vendor-supplied instrumentation-scope names and span names are not standardized semantic conventions.
- OTLP attributes are key/value entries whose values use `opentelemetry.proto.common.v1.AnyValue`. The variants are
  `string_value`, `bool_value`, `int_value`, `double_value`, `array_value`, `kvlist_value` and `bytes_value`. This is
  distinct from `google.protobuf.Any` and from the Protobuf wire type.
- Native timestamps:
  - Log records carry `time_unix_nano` and `observed_time_unix_nano`; span events carry `time_unix_nano`.
  - Claude Code's `event.timestamp` is a vendor attribute holding an ISO 8601 string on its log events. It is neither
    native field.
- Event names:
  - Claude Code documents its log events by prefixed names such as `claude_code.tool_result`.
  - The same records carry an `event.name` attribute with the unprefixed value, such as `tool_result`.
  - Read the record's own event name field and body per version before relying on either.
- In protobuf JSON mapping, `int64` is a decimal string; this does not change the decoded `AnyValue` variant. A
  `string_value` that contains digits stays a string-valued attribute.
  - Claude Code sent some numbers as strings in older versions.
  - Its changelog says numeric attributes on `api_request` and `api_error` events are now emitted as numbers.
- Span links are native OTLP objects. An attribute named `link.type` is a vendor-defined key, not the link model.

### 1.1 Specification rules for values, limits and encodings

These rules come from the current OpenTelemetry Specification, read on 16 Nov 2026, not from semantic conventions
v1.45.0. The sources are
[Common: AnyValue and attribute limits](https://opentelemetry.io/docs/specs/otel/common/#anyvalue),
[Attribute type mapping](https://opentelemetry.io/docs/specs/otel/common/attribute-type-mapping/),
[Mapping to non-OTLP formats](https://opentelemetry.io/docs/specs/otel/common/mapping-to-non-otlp/) and
[OTLP JSON Protobuf Encoding](https://opentelemetry.io/docs/specs/otlp/#json-protobuf-encoding).

In the v1.45.0 baseline, a standard attribute value is a primitive or a homogeneous array of primitives. The value
length limit applies to strings, and to each string in an array. Maps and arbitrarily nested `AnyValue` structures can
be written in the OTLP protobuf model, and the newer Common Specification defines limits for them recursively. A
receiver that accepts them chooses its own handling and budget. That is the receiver's policy, not a v1.45.0
requirement.

**Values** (the API and SDK rules: what an SDK must keep and pass on to processors and exporters)
- An attribute value is any `AnyValue`: maps and arrays of `AnyValue` included, nested to any depth. Only primitive
  arrays must be homogeneous.
- Empty values, zero, empty strings and empty arrays are meaningful and MUST be kept. A null inside an array that could
  not be avoided MUST be kept as it is.
- Keys are non-empty, and their case MUST be preserved.

**Limits** (defined for SDKs; a receiver sets its own)
- A count limit discards the attributes beyond it. It applies to top-level attributes only.
- A length limit truncates strings per character and byte arrays per byte, element by element inside arrays and maps.
- A value deeper than the depth limit is replaced by an empty value.
- The defaults are 128 attributes, unlimited length, and depth 64.
- Resource attributes SHOULD be exempt, and metric attributes are exempt for now.

**Mapping data into `AnyValue`**
- Integers in the signed 64-bit range SHOULD be `int_value`; larger ones a decimal `string_value`.
- Invalid UTF-8 SHOULD be `bytes_value`.
- A sender that puts numbers into `string_value` deviates from this. The value is still a string, as sent.

**Non-OTLP formats** (a tool's JSON answer is one)
- Byte arrays SHOULD be Base64. The string form of a value is lossy.
- Dropped counts (`otel.dropped_attributes_count`, `otel.dropped_events_count`, `otel.dropped_links_count`) MUST be
  reported when not zero, unless the format has a direct equivalent field.

**OTLP/JSON**
- `traceId` and `spanId` are hex.
- 64-bit integers are decimal strings, and decoders accept numbers or strings.
- Enums are integers, never names.
- Keys are lowerCamelCase.
- Unknown fields MUST be ignored.
- Requests and responses MUST carry `Content-Type: application/json`.

## 2. Standard OpenTelemetry / GenAI Attributes

The following names are defined by the OpenTelemetry semantic-convention repositories. The `gen_ai.*` conventions are
maintained in
the [dedicated GenAI repository](https://github.com/open-telemetry/semantic-conventions-genai/blob/main/docs/registry/attributes/gen-ai.md).

| Area                             | Standard attribute names                                                                                                                                                           |
|:---------------------------------|:-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Provider, model, and operation   | `gen_ai.provider.name`, `gen_ai.operation.name`, `gen_ai.request.model`, `gen_ai.response.model`, `gen_ai.request.reasoning.level`                                                 |
| Conversation and agent           | `gen_ai.conversation.id`, `gen_ai.conversation.compacted`, `gen_ai.agent.id`, `gen_ai.agent.name`                                                                                  |
| Token usage                      | `gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens`, `gen_ai.usage.cache_read.input_tokens`, `gen_ai.usage.cache_write.input_tokens`, `gen_ai.usage.reasoning.output_tokens` |
| Tool calls                       | `gen_ai.tool.name`, `gen_ai.tool.call.id`, `gen_ai.tool.type`                                                                                                                      |
| Message content                  | `gen_ai.input.messages`, `gen_ai.output.messages`                                                                                                                                  |
| Response details                 | `gen_ai.response.id`, `gen_ai.response.finish_reasons`, `gen_ai.response.time_to_first_chunk`                                                                                      |
| Resource identity (core semconv) | `service.name`, `service.instance.id`                                                                                                                                              |

- **Messages.** `gen_ai.input.messages` and `gen_ai.output.messages` are defined as structured `AnyValue`
  attributes (type `any`) following the GenAI message schema, on spans as on events.
  - On spans, a JSON string is allowed only as a fallback where an instrumentation cannot record structured
    values. On events it is not allowed.
  - The Claude API's spans use that fallback: JSON strings in the GenAI message format.
  - A vendor string payload under some other key is not a form of the convention.
- **Provider.** `gen_ai.provider.name` identifies the provider and is `anthropic` for Anthropic. `gen_ai.system` is
  the deprecated name it replaced; the GenAI repository does not define `gen_ai.system`.
- **Anthropic's token rule** (GenAI conventions, Anthropic page):
  `gen_ai.usage.input_tokens = input_tokens + cache_read_input_tokens + cache_write_input_tokens`. Anthropic's own
  `input_tokens` excludes cached tokens.
  - `gen_ai.usage.cache_read.input_tokens` counts tokens read from the provider-managed cache.
  - `gen_ai.usage.cache_write.input_tokens` counts tokens written to it.
  - Reasoning tokens are part of the output tokens.
  - Compaction tokens are outside `gen_ai.usage.*` (see §3.3).
- Vendor keys containing `cache_creation` remain vendor-specific and are **not** renamed; §4 gives their relation to
  `gen_ai.usage.cache_write.input_tokens`.

## 3. Anthropic / Claude Code Telemetry (Vendor-Defined)

These names are vendor-defined telemetry, including unprefixed keys. Appearing in OTLP does not make them
OpenTelemetry-defined attribute names.

### 3.1 Telling the emitters apart

| Emitter                    | How to recognise it                                                                                                                                                                                                                                                                       |
|:---------------------------|:------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Claude Code CLI            | `service.name` is `claude-code` unless the user overrides it. Observed instrumentation scopes (Claude Code 2.1.331): `com.anthropic.claude_code` for metrics, `com.anthropic.claude_code.events` for log events, `com.anthropic.claude_code.tracing` for spans. Span, event and metric names start with `claude_code.` |
| Anthropic SDK              | Span names start with `anthropic.`; kind CLIENT for requests, INTERNAL for the tools its tool runner executes. Instrumentation scope per the supplied inventory: `com.anthropic.sdk.*` (not checked against a primary source here)                                                                |
| Claude API (server side)   | Span names start with `anthropic.`; kind SERVER for requests, INTERNAL for the steps inside. Delivered through a telemetry destination registered in the Console, parented under the SDK span through `traceparent`. Instrumentation scope per the supplied inventory: `com.anthropic.claude-platform` (not checked) |

`app.entrypoint` and `app.version` are opt-in and off by default in Claude Code, so they cannot identify the emitter.

### 3.2 Claude Code CLI

**Spans (traces beta).** Every span carries `span.type` with its own name.

- `claude_code.interaction`: the root, one per user prompt.
- `claude_code.llm_request`.
- `claude_code.tool`, with the children `claude_code.tool.blocked_on_user` and `claude_code.tool.execution`.
- `claude_code.hook`, only with detailed beta tracing.

A subagent's `claude_code.llm_request` and `claude_code.tool` spans nest under the Agent tool's `claude_code.tool`
span.

**GenAI attributes on the spans** (observed in 2.1.331). Every span carries `gen_ai.provider.name` and
`gen_ai.conversation.id`, and a copy of each resource attribute set through `OTEL_RESOURCE_ATTRIBUTES`, such as
`gen_ai.agent.name`.

| Span | `gen_ai.operation.name` | Further `gen_ai.*` attributes |
|:-----|:------------------------|:------------------------------|
| `claude_code.interaction` | `invoke_agent` | |
| `claude_code.llm_request` | `chat` (since 2.1.331) | `gen_ai.system`, `gen_ai.request.model`. When the request got an answer: `gen_ai.response.id`, `gen_ai.response.finish_reasons` and, since 2.1.331, `gen_ai.response.model`, `gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens`, `gen_ai.usage.cache_read.input_tokens` and `gen_ai.usage.cache_write.input_tokens`, all integers |
| `claude_code.tool` | `execute_tool` | `gen_ai.tool.name`, `gen_ai.tool.call.id` |
| `claude_code.tool.execution` | | `gen_ai.tool.call.id` |
| `claude_code.tool.blocked_on_user` | | |

- `claude_code.llm_request` keeps Claude Code's own keys beside the GenAI ones: `model`, `request_id`,
  `input_tokens`, `output_tokens`, `cache_read_tokens` and `cache_creation_tokens`. §4 relates each pair.
- `gen_ai.usage.input_tokens` follows Anthropic's token rule (§2): it includes the cache tokens read and written.
  `input_tokens` on the same span does not, so the two numbers differ on every request that used the cache.
- Claude Code sends no `gen_ai.usage.reasoning.output_tokens`. The CLI's own result counts thinking tokens
  (`output_tokens_details.thinking_tokens`); no span attribute carries that count.
- The `api_request` event and the `claude_code.token.usage` metric carry Claude Code's keys only.
- A span of a request that got an answer has one span link with the attribute `link.type`.

**Span events**

- `gen_ai.request.attempt` on `claude_code.llm_request`: one per retry attempt.
- `tool.output` on `claude_code.tool`, with `OTEL_LOG_TOOL_CONTENT=1`.

**Log events.** This list is incomplete:

| Group           | Events                                                                                                                                                                                                                                |
|:----------------|:--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Prompts and API | `claude_code.user_prompt`, `claude_code.assistant_response`, `claude_code.api_request`, `claude_code.api_error`, `claude_code.api_refusal`, `claude_code.api_request_body`, `claude_code.api_response_body`, `claude_code.system_prompt` |
| Tools           | `claude_code.tool_result`, `claude_code.tool_decision`, `claude_code.at_mention`                                                                                                                                                       |
| Hooks           | `claude_code.hook_execution_complete`, `claude_code.hook_plugin_metrics`                                                                                                                                                              |
| Plugins, skills | `claude_code.plugin_installed`, `claude_code.plugin_loaded`, `claude_code.skill_activated`                                                                                                                                             |
| Safety          | `claude_code.safety_event`                                                                                                                                                                                                            |

**Metrics**

| Metric                              | Unit   | `type` attribute                                                                  |
|:------------------------------------|:-------|:----------------------------------------------------------------------------------|
| `claude_code.session.count`         | –      |                                                                                   |
| `claude_code.lines_of_code.count`   | –      |                                                                                   |
| `claude_code.pull_request.count`    | –      |                                                                                   |
| `claude_code.commit.count`          | –      |                                                                                   |
| `claude_code.cost.usage`            | USD    |                                                                                   |
| `claude_code.token.usage`           | tokens | `input` (excludes prompt-cache tokens), `output`, `cacheRead`, `cacheCreation` (5-minute and 1-hour together) |
| `claude_code.code_edit_tool.decision` | –    |                                                                                   |
| `claude_code.active_time.total`     | s      | `user` or `cli`                                                                   |

**Correlation keys**

| Key                                 | Meaning                                                                                                              |
|:------------------------------------|:---------------------------------------------------------------------------------------------------------------------|
| `session.id`                        | Claude Code's own session; `/clear` replaces it                                                                       |
| `ccr.session.id`                    | The cloud session                                                                                                    |
| `tool_use_id`                       | The model's tool-use block id. It joins `claude_code.tool` spans to `tool_result` and `tool_decision` events and to hook payloads |
| `request_id`, `client_request_id`, `message.uuid` | Request and message identity                                                                           |
| `event.sequence`, `interaction.sequence` | Ordering counters, per process rather than per session                                                           |
| `agent_id`, `parent_agent_id`       | Subagents and teammates                                                                                              |
| `query_source`                      | The issuing subsystem; detailed beta tracing only                                                                    |
| `workflow.run_id`, `workflow.name`  | Agents spawned by a workflow                                                                                         |

**Gates**

- Content and details come only with `OTEL_LOG_USER_PROMPTS`, `OTEL_LOG_ASSISTANT_RESPONSES`, `OTEL_LOG_TOOL_DETAILS`,
  `OTEL_LOG_TOOL_CONTENT` or `OTEL_LOG_RAW_API_BODIES`.
- Content is truncated at `CLAUDE_CODE_OTEL_CONTENT_MAX_LENGTH`: 61,440 UTF-16 code units by default, the
  `[TRUNCATED ...]` marker included. The unit is UTF-16 code units, not UTF-8 bytes. One code unit takes at most
  3 UTF-8 bytes, so a value cut at the default is at most 184,320 UTF-8 bytes.
- The documentation's example value for raising the limit is 262,144 code units. At that setting a value can reach
  786,432 UTF-8 bytes.
- A receiver's own byte limit, and its own truncation marker, are separate from Claude Code's. A value can carry
  both markers.

**What is not there.** In the documented tool span and `tool_result` attributes, no attribute carries a Bash exit
code. There are `success`, `error_type` and `error_class`, and `error`, which holds the full message only with
`OTEL_LOG_TOOL_DETAILS`.

### 3.3 Anthropic SDK and Claude API spans

| Span name                                                      | Kind                         | Notes                                                                                         |
|:---------------------------------------------------------------|:-----------------------------|:----------------------------------------------------------------------------------------------|
| `anthropic.messages.create`                                    | CLIENT (SDK), SERVER (API)   | token usage                                                                                   |
| `anthropic.model_request`                                      | INTERNAL (API)               | token usage                                                                                   |
| `anthropic.compaction`                                         | INTERNAL (API)               | token usage                                                                                   |
| `anthropic.session.outcome_evaluation`                         | INTERNAL (API)               | token usage                                                                                   |
| `anthropic.messages.tool_runner`                               | INTERNAL (SDK)               |                                                                                               |
| `anthropic.tool_use`, `anthropic.mcp_tool_use`, `anthropic.custom_tool_use` | INTERNAL        | `gen_ai.operation.name` = `execute_tool`                                                      |
| `anthropic.sessions.create`, `anthropic.<resources>.<method>`  | CLIENT (SDK)                 | resource ids as `anthropic.<resource>.id`                                                     |
| `anthropic.environment.work`                                   |                              |                                                                                               |
| `anthropic.session.turn`                                       | INTERNAL (API)               | from the supplied inventory; the tracing guide describes per-turn spans, name not confirmed here |
| `anthropic.sessions.*`                                         |                              | from the supplied inventory                                                                   |

**Counting tokens without counting twice**

- When a Messages API request has both an SDK span and an API span, sum the API spans. Both are named
  `anthropic.messages.create`; the CLIENT one is the SDK's and its SERVER child is the API's.
- On SDK spans, add `anthropic.usage.compaction.input_tokens` and `.output_tokens`, because `gen_ai.usage.*` leaves
  compaction out.
- On API spans, add the `anthropic.compaction` spans instead, never both.

### 3.4 Vendor attribute inventory

The supplied inventory, unchanged. It mixes the two emitters:
- Keys with the `anthropic.` prefix are mostly span attributes of the SDK and the API.
- Unprefixed keys are mostly Claude Code's. Claude Code also sends a few undocumented `anthropic.*` attributes, such as
  `anthropic.plan`, which was observed in the capture.

| Domain                              | Anthropic / Claude Code keys from the supplied inventory                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
|:------------------------------------|:------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **Session & Request Identity**      | `anthropic.session.id`, `anthropic.session.thread.id`, `ccr.session.id`, `anthropic.request.id`, `request_id`, `client_request_id`, `prompt.id`, `message.uuid`, `anthropic.organization.id`, `anthropic.workspace.id`, `anthropic.environment.id`, `anthropic.environment.work.id`                                                                                                                                                                                                                                                                                                                                                                   |
| **Ordering & Process Scope**        | `event.sequence`, `event.timestamp`, `interaction.sequence`, `interaction.duration_ms`, `app.entrypoint`, `app.version`                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| **Subagents & Queued Links**        | `agent.name`, `agent.source`, `agent_type`, `parent_agent_id`, `parent.source`, `agent_path_count`, `is_async`, `is_built_in`, `link.type`, `queued_sends`                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| **Model, Effort & Thinking**        | `model`, `final_model`, `model_swapped`, `effort`, `anthropic.thinking.type`, `anthropic.thinking.budget_tokens`, `anthropic.beta`, `anthropic.message.stop_reason`, `anthropic.session.turn.stop_reason`, `anthropic.request.service_tier`, `anthropic.response.service_tier`                                                                                                                                                                                                                                                                                                                                                                        |
| **Tokens, Compaction & Cost**       | `input_tokens`, `output_tokens`, `cache_read_tokens`, `cache_creation_tokens`, `cache_creation_5m_tokens`, `cache_creation_1h_tokens`, `anthropic.usage.cache_creation.ephemeral_5m_input_tokens`, `anthropic.usage.cache_creation.ephemeral_1h_input_tokens`, `anthropic.usage.compaction.input_tokens`, `anthropic.usage.compaction.output_tokens`, `pre_tokens`, `post_tokens`, `precompute_reuse`, `cost_usd_micros`, `cost_usd`, `duration_ms`, `first_content_ms`                                                                                                                                                                               |
| **Tools, Bash, Diffs & Hooks**      | `anthropic.mcp.server.name`, `mcp_server.name`, `mcp_server_scope`, `anthropic.toolset.name`, `anthropic.tool_result.content`, `file_path`, `diff`, `bash_command`, `bash_command_class`, `bash_argv0`, `full_command`, `command`, `command_name`, `command_length`, `command_source`, `command_path_count`, `hook_event`, `hook_name`, `hook_type`, `hook_source`, `hook_matcher`, `hook_definitions`, `num_hooks`, `num_success`, `num_blocking`, `num_non_blocking_error`, `num_cancelled`, `anthropic.skill.id`, `anthropic.skill.version.id`, `plugin.name`, `plugin.version`, `plugin.scope`, `plugin_id_hash`                                  |
| **Permissions & Managed Settings**  | `anthropic.tool.evaluated_permission`, `permission_mode`, `from_mode`, `decision`, `decision_source`, `decision_type`, `managed_only`, `managed_settings.resolved_sha256`, `managed_settings.source_behavior`, `managed_settings.sources`, `managed_settings.trigger`, `managed_settings.helper.applied`, `managed_settings.helper.state`, `managed_settings.helper.path`, `managed_settings.settings`, `managed_settings.settings_truncated`                                                                                                                                                                                                         |
| **Content, Truncation & Redaction** | `prompt`, `prompt_text`, `prompt_length`, `response`, `response_length`, `response.has_tool_call`, `response.model_output`, `response.model_output_original_length`, `response.model_output_truncated`, `content`, `content_original_length`, `content_truncated`, `diff_original_length`, `diff_truncated`, `bash_command_original_length`, `bash_command_truncated`, `file_path_original_length`, `file_path_truncated`, `new_context`, `new_context_message_count`, `new_context_original_length`, `new_context_truncated`, `anthropic.content.truncated`, `body`, `body_length`, `body_ref`, `body_truncated`, `body_withheld`, `request_body_id` |
| **Native Outcome Evaluation**       | `anthropic.session.outcome.id`, `anthropic.session.outcome_evaluation.result`, `anthropic.session.outcome_evaluation.explanation`, `anthropic.session.outcome_evaluation.iteration`                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |

The vendor documentation names further keys that the inventory lacks. The correlation keys `agent_id`, `tool_use_id`
and `query_source` matter most:

| Area                       | Missing keys                                                                                                                                                       |
|:---------------------------|:-------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Correlation and ordering   | `agent_id`, `tool_use_id`, `query_source`, `workflow.run_id`, `workflow.name`, `llm_request.context`, `span.type`                                                   |
| Tools                      | `tool_name`, `tool_name_safe`, `tool_parameters`, `tool_input`, `tool_input_size_bytes`, `tool_result_size_bytes`, `result_tokens`, `output`                       |
| Outcome                    | `success`, `error`, `error_type`, `error_class`                                                                                                                    |
| Prompts and system prompts | `user_prompt`, `user_prompt_length`, `system_reminders`, `system_prompt_preview`, `user_system_prompt`                                                              |
| Identity and environment   | `organization.id`, `user.id`, `user.email`, `user.account_uuid`, `user.account_id`, `terminal.type`, and the `vcs.*` repository and ref keys                        |

### 3.5 Vendor content and metadata extensions

- **Payload fields** in the supplied inventory: `prompt_text`, `content`, `diff`, `bash_command`, `file_path`,
  `new_context` and `response.model_output`, with their `*_original_length` and `*_truncated` fields where present.
- **Further source-listed indicators:** `body_withheld`, `body_ref`, `body_length`, `body_truncated` and
  `request_body_id`.
- **Content-block annotations.** In the Claude API's GenAI message parts:
  - a `tool_use` block with a toolset name carries `anthropic_toolset_name`;
  - an `mcp_tool_use` block carries `anthropic_mcp_server_name`;
  - a block without a GenAI part type becomes `anthropic_<block type>`, such as `anthropic_image`.

  These identifiers are not OpenTelemetry attributes.
- **Usage and cost fields.** `input_tokens`, `output_tokens`, `cache_read_tokens`, `cache_creation_tokens`,
  `cost_usd`, `cost_usd_micros` and `duration_ms` are not interchangeable with `gen_ai.usage.*` or other standard
  attributes; §4 says exactly how each relates.

## 4. Crosswalk: vendor names and convention names

The relation describes the values, not a renaming. Data keeps its original key.

| Vendor name (emitter)                                  | Convention name                                             | Relation                                                                                                                                                         |
|:-------------------------------------------------------|:------------------------------------------------------------|:-----------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `tool_use_id` (Claude Code)                            | `gen_ai.tool.call.id`                                       | same value; Claude Code sends both on its tool spans                                                                                                             |
| `request_id` (Claude Code)                             | `gen_ai.response.id`                                        | same value; Claude Code sends both on `claude_code.llm_request`                                                                                                  |
| `model` (Claude Code)                                  | `gen_ai.request.model`, `gen_ai.response.model`             | same value; Claude Code sends all three on `claude_code.llm_request`, the response model since 2.1.331 and only when the request got an answer                   |
| `gen_ai.system` = `anthropic` (Claude Code)            | `gen_ai.provider.name` = `anthropic`                        | deprecated name, same meaning; Claude Code still sends the old name                                                                                              |
| `input_tokens`, `token.usage{type=input}` (Claude Code) | `gen_ai.usage.input_tokens`                                | different meaning: Claude Code excludes cache tokens. The convention's value is `input_tokens + cache_read_tokens + cache_creation_tokens`. Since 2.1.331 Claude Code sends both on `claude_code.llm_request`, each with its own meaning; sum one of them, never both |
| `output_tokens`, `type=output` (Claude Code)           | `gen_ai.usage.output_tokens`                                | same value; Claude Code sends both on `claude_code.llm_request` since 2.1.331                                                                                    |
| `cache_read_tokens`, `type=cacheRead` (Claude Code)    | `gen_ai.usage.cache_read.input_tokens`                      | same quantity, different name; both on `claude_code.llm_request` since 2.1.331                                                                                   |
| `cache_creation_tokens`, `type=cacheCreation` (Claude Code) | `gen_ai.usage.cache_write.input_tokens`                | same quantity, different name; Claude Code adds the 5-minute and 1-hour writes together; both on `claude_code.llm_request` since 2.1.331                          |
| `cache_creation_5m_tokens`, `cache_creation_1h_tokens` (Claude Code) | `anthropic.usage.cache_creation.ephemeral_5m_input_tokens`, `…_1h_…` (API) | vendor to vendor, same split; no GenAI counterpart                                                                                         |
| `effort` (Claude Code)                                 | `gen_ai.request.reasoning.level`                            | same setting (Anthropic's `output_config.effort`), different name                                                                                                |
| `first_content_ms` (Claude Code)                       | `gen_ai.response.time_to_first_chunk`                       | related, not equal: milliseconds against seconds, and different start points (Claude Code: the successful attempt; the SDK: its first send)                      |
| `duration_ms` on `claude_code.llm_request`             | the span's own start and end                                | Claude Code's value includes retries; each retry is a `gen_ai.request.attempt` span event                                                                        |
| `event.timestamp` (Claude Code)                        | the log record's `time_unix_nano`                           | a copy as an ISO 8601 string                                                                                                                                     |
| `event.name` = `tool_result` (Claude Code)             | the event name `claude_code.tool_result`                    | same event, unprefixed in the attribute                                                                                                                          |
| `session.id` (Claude Code)                             | `anthropic.session.id` (API)                                | **different concepts**: a Claude Code CLI session (replaced by `/clear`) against a Managed Agents session. A work session spanning `/clear` needs its own key, for example a run tag in `OTEL_RESOURCE_ATTRIBUTES` |
| `organization.id` (Claude Code)                        | `anthropic.organization.id` (API)                           | vendor to vendor, the same organization; compare the value formats before joining                                                                                |
| `cost_usd`, `cost_usd_micros` (Claude Code)            | none among the names checked                                | vendor only: the same estimate in dollars and in millionths                                                                                                      |

## 5. Upstream References

1. [OpenTelemetry Semantic Conventions v1.45.0 release](https://github.com/open-telemetry/semantic-conventions/releases/tag/v1.45.0)
2. [OpenTelemetry GenAI Semantic Conventions — attribute registry](https://github.com/open-telemetry/semantic-conventions-genai/blob/main/docs/registry/attributes/gen-ai.md)
3. [OpenTelemetry GenAI Semantic Conventions — Anthropic](https://github.com/open-telemetry/semantic-conventions-genai/blob/main/docs/gen-ai/anthropic.md)
4. [Core registry: deprecated GenAI attributes (`gen_ai.system`)](https://github.com/open-telemetry/semantic-conventions/blob/main/docs/registry/attributes/gen-ai.md)
5. [OTLP Specification](https://opentelemetry.io/docs/specs/otlp/)
6. [OTLP common.proto (`AnyValue`)](https://github.com/open-telemetry/opentelemetry-proto/blob/main/opentelemetry/proto/common/v1/common.proto)
7. [Claude Code: Monitoring](https://code.claude.com/docs/en/monitoring-usage)
8. [Claude API: OpenTelemetry span reference](https://platform.claude.com/docs/en/api/opentelemetry/spans)
9. [Claude API: OpenTelemetry tracing](https://platform.claude.com/docs/en/manage-claude/opentelemetry)
