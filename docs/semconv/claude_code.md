# `claude_code` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `agent.name` | `string` | development |  | Name of the agent that issued an API request, such as the subagent type `general-purpose`. | `"general-purpose"`, `"custom"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `agent.source` | `string` | development |  | Where the definition of a completed subagent came from. | `"built-in"`, `"plugin"`, `"userSettings"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `agent_id` | `string` | development |  | Identifier of the subagent that produced a record; absent on the main session's records. | `"afade000000000001"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `agent_path_count` | `int` | development |  | Number of agent directories a loaded plugin declares. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `agent_type` | `string` | development |  | Type of a completed subagent. | `"general-purpose"`, `"custom"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `anthropic.custom_base_url` | `boolean` | development |  | Whether the session sends its model requests to a custom `ANTHROPIC_BASE_URL`. |  | `nami.persisted`: `false`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `anthropic.plan` | `string` | development |  | Subscription plan of the signed-in account. | `"max"` | `nami.persisted`: `false`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `anthropic.provider` | `string` | development |  | API provider the session sends its model requests to. | `"firstParty"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `attempt` | `int` | development |  | Number of attempts made for an API request, the first one included. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `bash_argv0` | `string` | development |  | First program of a Bash command when it is on Claude Code's fixed list, otherwise `other`. | `"git"`, `"python"`, `"other"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `bash_command` | `string` | development |  | First word of a Bash command line. | `"python3"`, `"git"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `bash_command_class` | `string` | development |  | Category of a Bash command's first program, from Claude Code's fixed list. | `"vcs"`, `"lang_runtime"`, `"package_manager"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `body` | `string` | development |  | A raw Anthropic Messages API request or response body. | `"<REDACTED>"` | `nami.persisted`: `false`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `body_ref` | `string` | development |  | Path of the file that holds an untruncated raw API body. | `"/scratch/bodies/00000000-0000-4000-8000-000000000001.request.json"` | `nami.persisted`: `false`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `cache_creation_1h_tokens` | `int` | development |  | Tokens an API request wrote to the prompt cache with a one-hour lifetime. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `cache_creation_5m_tokens` | `int` | development |  | Tokens an API request wrote to the prompt cache with a five-minute lifetime. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `cache_creation_tokens` | `int` | development |  | Tokens an API request wrote to the prompt cache. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `cache_read_tokens` | `int` | development |  | Tokens an API request read from the prompt cache. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `client_request_id` | `string` | development |  | Client-generated `x-client-request-id` of an API request attempt. | `"00000000-0000-4000-8000-000000000004"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `command_path_count` | `int` | development |  | Number of command directories a loaded plugin declares. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `content` | `string` | development |  | Text a Read returned or a Write was asked to write. | `"<REDACTED>"` | `nami.persisted`: `false`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `cost_usd` | `double` | development |  | Estimated cost of an API request in US dollars, as Claude Code computes it. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `cost_usd_micros` | `int` | development |  | Estimated cost of an API request in millionths of a US dollar: `cost_usd` times one million, rounded. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `decision` | `string` | development |  | Outcome of a permission check for a tool call. | `"accept"`, `"reject"`, `"cancelled"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `diff` | `string` | development |  | Structured patch an Edit applied. | `"<REDACTED>"` | `nami.persisted`: `false`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `duration_ms` | `int` | development |  | Duration in milliseconds of the operation a record describes. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `effort` | `string` | development |  | Effort level applied to an API request; absent when Claude Code sends none. | `"low"`, `"medium"`, `"high"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `enabled_via` | `string` | development |  | How a loaded plugin came to be enabled. | `"default-enable"`, `"user-install"`, `"org-policy"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `error` | `string` | development |  | Error message of a failed tool call or API request. | `"Shell command failed"` | `nami.persisted`: `false`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `error_class` | `string` | development |  | Short class token of a failed API request or tool execution. | `"ShellError"`, `"Error_ENOENT"`, `"invalid_api_key"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `error_type` | `string` | development |  | Category of a failed tool call on `tool_result`. | `"ShellError"`, `"Error:ENOENT"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `event.sequence` | `int` | development |  | Counter that orders Claude Code's events, from 0, per Claude Code process. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `file_path` | `string` | development |  | Target file of a Read, Edit or Write tool call. | `"/workspace/calc/calc.py"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `final_model` | `string` | development |  | Model that produced a subagent's final response. | `"claude-haiku-5-5"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `first_content_ms` | `int` | development |  | Milliseconds from the start of an API request to the first content block of its successful attempt. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `full_command` | `string` | development |  | Command line of a Bash tool call. | `"python3 -m unittest -q"` | `nami.persisted`: `false`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `has_hooks` | `boolean` | development |  | Whether a loaded plugin contributes hooks. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `has_mcp` | `boolean` | development |  | Whether a loaded plugin contributes MCP servers. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `host_owned_mcp` | `boolean` | development |  | Whether the SDK host manages a loaded plugin's MCP connections. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `input_tokens` | `int` | development |  | Input tokens of an API request, without the tokens read from or written to the prompt cache. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `interaction.duration_ms` | `int` | development |  | Wall-clock duration of an interaction: one user prompt and all it triggered. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `interaction.sequence` | `int` | development |  | Counter of interactions, from 1, per Claude Code process. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `is_async` | `boolean` | development |  | Whether a completed subagent ran in the background. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `is_built_in` | `boolean` | development |  | Whether a completed subagent is a built-in agent type. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `language` | `string` | development |  | Programming language of the file a code-editing decision concerned. | `"Python"`, `"TypeScript"`, `"unknown"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `llm_request.context` | `string` | development |  | What the parent of a `claude_code.llm_request` span is. | `"interaction"`, `"tool"`, `"standalone"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `managed_settings.helper.applied` | `string` | development |  | Whether the policy helper's output serves as the managed settings. | `"none"`, `"output"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `managed_settings.helper.state` | `string` | development |  | State of the managed-settings policy helper. | `"none"`, `"ok"`, `"exit_nonzero"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `managed_settings.source_behavior` | `string` | development |  | How Claude Code combines managed settings sources. | `"first-wins"`, `"merge"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `managed_settings.sources` | `string[]` | development |  | Managed settings sources that deliver at least one policy key, highest priority first. | `["remote","file"]` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `managed_settings.trigger` | `string` | development |  | Why Claude Code reported its managed settings. | `"startup"`, `"change"`, `"refused"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `marketplace.name` | `string` | development |  | Marketplace a plugin was installed from. | `"builtin"`, `"synced"`, `"third-party"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `message.uuid` | `string` | development |  | UUID of the transcript entry a prompt or a response became. | `"00000000-0000-4000-8000-000000000006"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `model` | `string` | development |  | Model of an API request, a subagent, or the code change a counter counts. | `"claude-haiku-5-5"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `model_swapped` | `boolean` | development |  | Whether more than one model served a subagent's requests. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `new_context` | `string` | development |  | Content sent with a request under detailed beta tracing. | `"<REDACTED>"` | `nami.persisted`: `false`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `organization.id` | `string` | development |  | UUID of the signed-in account's organization. | `"00000000-0000-4000-8000-000000000003"` | `nami.persisted`: `false`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `output` | `string` | development |  | Combined output of a Bash command, or the result of an MCP, WebFetch or WebSearch call. | `"<REDACTED>"` | `nami.persisted`: `false`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `output_tokens` | `int` | development |  | Output tokens of an API request. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `parent.source` | `string` | development |  | How an interaction span got its trace parent. | `"none"`, `"env"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `permission_mode` | `string` | development |  | Permission mode in force when a tool call was checked. | `"default"`, `"plan"`, `"acceptEdits"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `plugin.name` | `string` | development |  | Name of a loaded plugin. | `"third-party"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `plugin.scope` | `string` | development |  | Provenance category of a loaded plugin. | `"official"`, `"community"`, `"org"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `plugin.version` | `string` | development |  | Version from a loaded plugin's manifest, when its name is not redacted. | `"1.3.2"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `plugin_id_hash` | `string` | development |  | Deterministic hash of a plugin's name and marketplace. | `"da63145d0acbb62d"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `prompt` | `string` | development |  | Text of a user prompt. | `"<REDACTED>"` | `nami.persisted`: `false`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `prompt.id` | `string` | development |  | UUID that links all events produced while one user prompt is processed. | `"00000000-0000-4000-8000-000000000005"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `prompt_length` | `int` | development |  | Length of a user prompt in characters. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `prompt_text` | `string` | development |  | Text of a user prompt; the same value as `prompt`. | `"<REDACTED>"` | `nami.persisted`: `false`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `query_source` | `string` | development |  | Subsystem that issued an API request. | `"main"`, `"subagent"`, `"sdk"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `query_source_safe` | `string` | development |  | Bounded form of `query_source` on the `claude_code.llm_request` span. | `"sdk"`, `"generate_session_title"`, `"agent.builtin.general-purpose"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `queued_sends` | `int` | development |  | Messages queued while an interaction ran. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `request_id` | `string` | development |  | ID of an API request; on tool events, of the response that contained the tool call. | `"req_011FIXTURE00000000000001"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `response` | `string` | development |  | Text of an assistant response. | `"<REDACTED>"` | `nami.persisted`: `false`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `response.model_output` | `string` | development |  | Text of the model's response to a request, under detailed beta tracing. | `"<REDACTED>"` | `nami.persisted`: `false`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `response_length` | `int` | development |  | Length of an assistant response's text in characters. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `safe_mode` | `boolean` | development |  | Whether the session started with `--safe-mode`. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `skill_path_count` | `int` | development |  | Number of skill directories a loaded plugin declares. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `source` | `string` | development |  | Where a permission decision came from, or what ended a wait for one. | `"config"`, `"hook"`, `"user_permanent"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `span.type` | `string` | development |  | Claude Code's type of a span: its name without the `claude_code.` prefix. | `"interaction"`, `"llm_request"`, `"tool"` | `nami.persisted`: `false`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `speed` | `string` | development |  | Speed mode of an API request. | `"normal"`, `"fast"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `start_type` | `string` | development |  | How a CLI session started. | `"fresh"`, `"resume"`, `"continue"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `stop_reason` | `string` | development |  | The API response's `stop_reason`. | `"end_turn"`, `"tool_use"`, `"max_tokens"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `subagent_type` | `string` | development |  | Subagent type of an Agent or Task tool call. | `"general-purpose"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `success` | `boolean` | development |  | Whether a tool call or an API request succeeded. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `system_prompt_preview` | `string` | development |  | First 500 characters of a request's system prompt, under detailed beta tracing. | `"<REDACTED>"` | `nami.persisted`: `false`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `system_reminders` | `string` | development |  | Text of the system reminders among a request's new messages, under detailed beta tracing. | `"<REDACTED>"` | `nami.persisted`: `false`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `terminal.type` | `string` | development |  | Terminal type of the session, when detected. | `"iTerm.app"`, `"vscode"`, `"tmux"` | `nami.persisted`: `false`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `tool_input` | `string` | development |  | JSON-serialized input of a tool call. | `"<REDACTED>"` | `nami.persisted`: `false`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `tool_input_size_bytes` | `int` | development |  | Size of a tool call's JSON-serialized input in bytes. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `tool_name` | `string` | development |  | Name of the tool a record concerns. | `"Bash"`, `"Read"`, `"Edit"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `tool_name_safe` | `string` | development |  | `tool_name` without user-chosen names: MCP tools appear as `mcp_other`. | `"Bash"`, `"Edit"`, `"mcp_other"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `tool_parameters` | `string` | development |  | JSON object with a tool call's parameters. | `"{\"bash_command\":\"python3\",\"full_command\":\"python3 -m unittest -q\"}"` | `nami.persisted`: `false`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `tool_result_size_bytes` | `int` | development |  | Size of a tool call's result in bytes. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `tool_source` | `string` | development |  | Origin of the tool a decision concerns. | `"builtin"`, `"mcp"`, `"sdk_host_builtin_mcp"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `tool_use_id` | `string` | development |  | ID of the model's `tool_use` block; it joins `tool_decision`, `tool_result` and the tool spans. | `"toolu_01FIXTURE000000000000001"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `total_tokens` | `int` | development |  | Tokens of a subagent's final API request: its input, cache and output tokens. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `total_tool_uses` | `int` | development |  | Tool calls a subagent made over its whole run. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `ttft_ms` | `int` | development |  | Time to the first token of an API request in milliseconds. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `type` | `string` | development |  | What a Claude Code counter counts. | `"input"`, `"output"`, `"cacheRead"` | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `user_prompt` | `string` | development |  | Text of the user prompt that started an interaction. | `"<REDACTED>"` | `nami.persisted`: `false`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |
| `user_prompt_length` | `int` | development |  | Length in characters of the user prompt that started an interaction. |  | `nami.persisted`: `true`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `true` |
| `user_system_prompt` | `string` | development |  | System prompt text given with `--system-prompt`, `--append-system-prompt` or the SDK's `systemPrompt`, under detailed beta tracing. | `"<REDACTED>"` | `nami.persisted`: `false`, `nami.producer`: `"claude_code"`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.4.0"`, `nami.token_exempt`: `false` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `claude_code.active_time.total` | counter | `s` | development |  | Total active time, without idle time. |
| `claude_code.code_edit_tool.decision` | counter |  | development |  | Count of permission decisions for the code-editing tools Edit, Write and NotebookEdit. |
| `claude_code.commit.count` | counter |  | development |  | Number of git commits created. |
| `claude_code.cost.usage` | counter | `USD` | development |  | Cost of the Claude Code session. |
| `claude_code.lines_of_code.count` | counter |  | development |  | Count of lines of code modified. |
| `claude_code.pull_request.count` | counter |  | development |  | Number of pull requests created. |
| `claude_code.session.count` | counter |  | development |  | Count of CLI sessions started. |
| `claude_code.token.usage` | counter | `tokens` | development |  | Number of tokens used. |

## Events

| Name | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `claude_code.api_error` | development |  | An API request to the model failed. |
| `claude_code.api_request` | development |  | An API request to the model completed. |
| `claude_code.api_request_body` | development |  | The raw body of an API request. |
| `claude_code.api_response_body` | development |  | The raw body of a successful API response. |
| `claude_code.assistant_response` | development |  | The model answered with text. |
| `claude_code.managed_settings_resolved` | development |  | The managed settings a session resolved. |
| `claude_code.plugin_loaded` | development |  | A plugin was enabled at session start. |
| `claude_code.subagent_completed` | development |  | A subagent finished and returned its result. |
| `claude_code.system_prompt` | development |  | The complete system prompt a session sent. |
| `claude_code.tool_decision` | development |  | A permission decision for a tool call. |
| `claude_code.tool_result` | development |  | A tool call finished. |
| `claude_code.user_prompt` | development |  | A user submitted a prompt. |

## Spans

| Type | Kind | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- |
| `claude_code.interaction` | internal | development |  | One user prompt and everything it triggered; the root of a turn's trace. |
| `claude_code.llm_request` | internal | development |  | One model request with its retries. |
| `claude_code.tool` | internal | development |  | One tool call, from the permission check to the result. |
| `claude_code.tool.blocked_on_user` | internal | development |  | The wait for a tool call's permission decision. |
| `claude_code.tool.execution` | internal | development |  | The execution of a tool call's body. |

## Signal attributes

The attributes of each signal that nami describes for a producer. Type is the type nami
stores; Wire is what the producer sends where that differs. Persisted says whether ingest
keeps the key.

### Metric `claude_code.active_time.total`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `type` | `string` |  | required | yes |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |

### Metric `claude_code.code_edit_tool.decision`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `decision` | `string` |  | required | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `language` | `string` |  | required | yes |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `source` | `string` |  | required | yes |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `tool_name` | `string` |  | required | yes |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |

### Metric `claude_code.commit.count`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |

### Metric `claude_code.cost.usage`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `agent.name` | `string` |  | conditionally_required: On a subagent's requests. | yes |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `effort` | `string` |  | conditionally_required: When Claude Code sends an effort level. | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `model` | `string` |  | required | yes |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `query_source` | `string` |  | required | yes |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `speed` | `string` |  | conditionally_required: When the request ran in fast mode. | yes |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |

### Metric `claude_code.lines_of_code.count`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `model` | `string` |  | required | yes |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `type` | `string` |  | required | yes |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |

### Metric `claude_code.pull_request.count`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |

### Metric `claude_code.session.count`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `start_type` | `string` |  | required | yes |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |

### Metric `claude_code.token.usage`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `agent.name` | `string` |  | conditionally_required: On a subagent's requests. | yes |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `effort` | `string` |  | conditionally_required: When Claude Code sends an effort level. | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `model` | `string` |  | required | yes |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `query_source` | `string` |  | required | yes |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `speed` | `string` |  | conditionally_required: When the request ran in fast mode. | yes |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `type` | `string` |  | required | yes |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |

### Event `claude_code.api_error`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `attempt` | `int` |  | required | yes |
| `client_request_id` | `string` |  | recommended | yes |
| `duration_ms` | `int` |  | required | yes |
| `effort` | `string` |  | conditionally_required: When Claude Code sends an effort level. | yes |
| `error` | `string` |  | required | no |
| `event.sequence` | `int` |  | required | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `model` | `string` |  | required | yes |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `prompt.id` | `string` |  | required | yes |
| `query_source` | `string` |  | required | yes |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `speed` | `string` |  | required | yes |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |

### Event `claude_code.api_request`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `agent.name` | `string` |  | conditionally_required: On a subagent's requests. | yes |
| `agent_id` | `string` |  | conditionally_required: On a subagent's requests. | yes |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `cache_creation_1h_tokens` | `int` |  | recommended | yes |
| `cache_creation_5m_tokens` | `int` |  | recommended | yes |
| `cache_creation_tokens` | `int` |  | required | yes |
| `cache_read_tokens` | `int` |  | required | yes |
| `client_request_id` | `string` |  | conditionally_required: On first-party API connections. | yes |
| `cost_usd` | `double` |  | required | yes |
| `cost_usd_micros` | `int` |  | required | yes |
| `duration_ms` | `int` |  | required | yes |
| `effort` | `string` |  | conditionally_required: When Claude Code sends an effort level. | yes |
| `event.sequence` | `int` |  | required | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `input_tokens` | `int` |  | required | yes |
| `model` | `string` |  | required | yes |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `output_tokens` | `int` |  | required | yes |
| `prompt.id` | `string` |  | required | yes |
| `query_source` | `string` |  | required | yes |
| `request_id` | `string` |  | required | yes |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `speed` | `string` |  | required | yes |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `ttft_ms` | `int` |  | recommended | yes |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |

### Event `claude_code.api_request_body`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `body` | `string` |  | conditionally_required: With `OTEL_LOG_RAW_API_BODIES=1`. | no |
| `body_ref` | `string` |  | conditionally_required: With `OTEL_LOG_RAW_API_BODIES=file:<dir>`. | no |
| `event.sequence` | `int` |  | required | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `model` | `string` |  | required | yes |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `query_source` | `string` |  | required | yes |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |

### Event `claude_code.api_response_body`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `body` | `string` |  | conditionally_required: With `OTEL_LOG_RAW_API_BODIES=1`. | no |
| `body_ref` | `string` |  | conditionally_required: With `OTEL_LOG_RAW_API_BODIES=file:<dir>`. | no |
| `event.sequence` | `int` |  | required | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `message.uuid` | `string` |  | recommended | yes |
| `model` | `string` |  | required | yes |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `query_source` | `string` |  | required | yes |
| `request_id` | `string` |  | required | yes |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |

### Event `claude_code.assistant_response`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `agent_id` | `string` |  | conditionally_required: On a subagent's records. | yes |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `event.sequence` | `int` |  | required | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `message.uuid` | `string` |  | required | yes |
| `model` | `string` |  | required | yes |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `prompt.id` | `string` |  | required | yes |
| `query_source` | `string` |  | required | yes |
| `request_id` | `string` |  | required | yes |
| `response` | `string` |  | required | no |
| `response_length` | `int` |  | required | yes |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |

### Event `claude_code.managed_settings_resolved`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `event.sequence` | `int` |  | required | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `managed_settings.helper.applied` | `string` |  | required | yes |
| `managed_settings.helper.state` | `string` |  | required | yes |
| `managed_settings.source_behavior` | `string` |  | required | yes |
| `managed_settings.sources` | `string[]` |  | required | yes |
| `managed_settings.trigger` | `string` |  | required | yes |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |

### Event `claude_code.plugin_loaded`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `agent_path_count` | `int` |  | required | yes |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `command_path_count` | `int` |  | required | yes |
| `enabled_via` | `string` |  | required | yes |
| `event.sequence` | `int` |  | required | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `has_hooks` | `boolean` |  | required | yes |
| `has_mcp` | `boolean` |  | required | yes |
| `host_owned_mcp` | `boolean` |  | required | yes |
| `marketplace.name` | `string` |  | required | yes |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `plugin.name` | `string` |  | required | yes |
| `plugin.scope` | `string` |  | required | yes |
| `plugin.version` | `string` |  | conditionally_required: When the name is not redacted and the manifest declares a version. | yes |
| `plugin_id_hash` | `string` |  | required | yes |
| `safe_mode` | `boolean` | `string` | required | yes |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `skill_path_count` | `int` |  | required | yes |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |

### Event `claude_code.subagent_completed`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `agent.source` | `string` |  | required | yes |
| `agent_id` | `string` |  | required | yes |
| `agent_type` | `string` |  | required | yes |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `duration_ms` | `int` |  | required | yes |
| `event.sequence` | `int` |  | required | yes |
| `final_model` | `string` |  | required | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `is_async` | `boolean` |  | required | yes |
| `is_built_in` | `boolean` |  | required | yes |
| `model` | `string` |  | required | yes |
| `model_swapped` | `boolean` |  | required | yes |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `prompt.id` | `string` |  | required | yes |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `total_tokens` | `int` |  | required | yes |
| `total_tool_uses` | `int` |  | required | yes |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |

### Event `claude_code.system_prompt`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `event.sequence` | `int` |  | required | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |

### Event `claude_code.tool_decision`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `agent_id` | `string` |  | conditionally_required: On a subagent's records. | yes |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `bash_command` | `string` | in `tool_parameters` | opt_in | yes |
| `decision` | `string` |  | required | yes |
| `event.sequence` | `int` |  | required | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `permission_mode` | `string` |  | recommended | yes |
| `prompt.id` | `string` |  | required | yes |
| `request_id` | `string` |  | recommended | yes |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `source` | `string` |  | required | yes |
| `subagent_type` | `string` | in `tool_parameters` | opt_in | yes |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `tool_name` | `string` |  | required | yes |
| `tool_parameters` | `string` |  | opt_in | no |
| `tool_source` | `string` |  | required | yes |
| `tool_use_id` | `string` |  | required | yes |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |

### Event `claude_code.tool_result`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `agent_id` | `string` |  | conditionally_required: On a subagent's records. | yes |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `bash_command` | `string` | in `tool_parameters` | opt_in | yes |
| `duration_ms` | `int` | `string` | required | yes |
| `error` | `string` |  | opt_in | no |
| `error_type` | `string` |  | conditionally_required: When the call failed. | yes |
| `event.sequence` | `int` |  | required | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `prompt.id` | `string` |  | required | yes |
| `request_id` | `string` |  | recommended | yes |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `subagent_type` | `string` | in `tool_parameters` | opt_in | yes |
| `success` | `boolean` | `string` | required | yes |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `tool_input` | `string` |  | opt_in | no |
| `tool_input_size_bytes` | `int` | `string` | required | yes |
| `tool_name` | `string` |  | required | yes |
| `tool_parameters` | `string` |  | opt_in | no |
| `tool_result_size_bytes` | `int` | `string` | recommended | yes |
| `tool_use_id` | `string` |  | required | yes |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |
| `vcs.ref.head.name` | `string` |  | opt_in | yes |
| `vcs.ref.head.revision` | `string` |  | opt_in | yes |
| `vcs.ref.head.type` | `enum` |  | opt_in | yes |

### Event `claude_code.user_prompt`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `event.sequence` | `int` |  | required | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `message.uuid` | `string` |  | conditionally_required: Unless the prompt dispatches a command. | yes |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `prompt` | `string` |  | required | no |
| `prompt.id` | `string` |  | required | yes |
| `prompt_length` | `int` | `string` | required | yes |
| `prompt_text` | `string` |  | required | no |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |

### Span `claude_code.interaction`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `gen_ai.conversation.id` | `string` |  | required | yes |
| `gen_ai.operation.name` | `enum` |  | required | yes |
| `gen_ai.provider.name` | `enum` |  | required | yes |
| `interaction.duration_ms` | `int` |  | required | yes |
| `interaction.sequence` | `int` |  | required | yes |
| `new_context` | `string` |  | opt_in | no |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `parent.source` | `string` |  | required | yes |
| `queued_sends` | `int` |  | recommended | yes |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `span.type` | `string` |  | required | no |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |
| `user_prompt` | `string` |  | required | no |
| `user_prompt_length` | `int` |  | required | yes |

### Span `claude_code.llm_request`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `agent_id` | `string` |  | conditionally_required: On a subagent's requests. | yes |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `attempt` | `int` |  | required | yes |
| `cache_creation_tokens` | `int` |  | conditionally_required: When the request succeeded. | yes |
| `cache_read_tokens` | `int` |  | conditionally_required: When the request succeeded. | yes |
| `client_request_id` | `string` |  | recommended | yes |
| `duration_ms` | `int` |  | required | yes |
| `effort` | `string` |  | conditionally_required: When Claude Code sends an effort level. | yes |
| `error` | `string` |  | conditionally_required: When the request failed. | no |
| `error_class` | `string` |  | conditionally_required: When the request failed. | yes |
| `first_content_ms` | `int` |  | conditionally_required: When the request succeeded on the streaming path. | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `gen_ai.conversation.id` | `string` |  | required | yes |
| `gen_ai.provider.name` | `enum` |  | required | yes |
| `gen_ai.request.model` | `string` |  | required | yes |
| `gen_ai.response.finish_reasons` | `string[]` |  | conditionally_required: When the request succeeded. | yes |
| `gen_ai.response.id` | `string` |  | conditionally_required: When the request succeeded. | yes |
| `input_tokens` | `int` |  | conditionally_required: When the request succeeded. | yes |
| `llm_request.context` | `string` |  | required | yes |
| `model` | `string` |  | required | yes |
| `new_context` | `string` |  | opt_in | no |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `output_tokens` | `int` |  | conditionally_required: When the request succeeded. | yes |
| `query_source_safe` | `string` |  | required | yes |
| `request_id` | `string` |  | conditionally_required: When the request succeeded. | yes |
| `response.model_output` | `string` |  | opt_in | no |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `span.type` | `string` |  | required | no |
| `speed` | `string` |  | required | yes |
| `stop_reason` | `string` |  | conditionally_required: When the request succeeded. | yes |
| `success` | `boolean` |  | required | yes |
| `system_prompt_preview` | `string` |  | opt_in | no |
| `system_reminders` | `string` |  | opt_in | no |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `ttft_ms` | `int` |  | conditionally_required: When the request succeeded. | yes |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |
| `user_system_prompt` | `string` |  | opt_in | no |

### Span `claude_code.tool`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `agent_id` | `string` |  | conditionally_required: On a subagent's tool calls. | yes |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `bash_argv0` | `string` |  | conditionally_required: For the Bash tool. | yes |
| `bash_command_class` | `string` |  | conditionally_required: For the Bash tool. | yes |
| `duration_ms` | `int` |  | required | yes |
| `file_path` | `string` |  | opt_in | yes |
| `full_command` | `string` |  | opt_in | no |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `gen_ai.conversation.id` | `string` |  | required | yes |
| `gen_ai.operation.name` | `enum` |  | required | yes |
| `gen_ai.provider.name` | `enum` |  | required | yes |
| `gen_ai.tool.call.id` | `string` |  | required | yes |
| `gen_ai.tool.name` | `string` |  | required | yes |
| `new_context` | `string` |  | opt_in | no |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `span.type` | `string` |  | required | no |
| `subagent_type` | `string` |  | opt_in | yes |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `tool_input` | `string` |  | opt_in | no |
| `tool_name` | `string` |  | required | yes |
| `tool_name_safe` | `string` |  | required | yes |
| `tool_use_id` | `string` |  | required | yes |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |

### Span `claude_code.tool.blocked_on_user`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `decision` | `string` |  | required | yes |
| `duration_ms` | `int` |  | required | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `gen_ai.conversation.id` | `string` |  | required | yes |
| `gen_ai.provider.name` | `enum` |  | required | yes |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `source` | `string` |  | required | yes |
| `span.type` | `string` |  | required | no |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |

### Span `claude_code.tool.execution`

| Key | Type | Wire | Requirement | Persisted |
| --- | --- | --- | --- | --- |
| `anthropic.custom_base_url` | `boolean` |  | required | no |
| `anthropic.plan` | `string` |  | conditionally_required: When the session is signed in. | no |
| `anthropic.provider` | `string` |  | required | yes |
| `duration_ms` | `int` |  | required | yes |
| `error` | `string` |  | conditionally_required: When the call failed. | no |
| `error.type` | `enum` |  | conditionally_required: When the call failed. | yes |
| `error_class` | `string` |  | conditionally_required: When the call failed. | yes |
| `gen_ai.agent.name` | `string` |  | conditionally_required: When `OTEL_RESOURCE_ATTRIBUTES` sets it on the resource; Claude Code copies resource attributes onto every record. | yes |
| `gen_ai.conversation.id` | `string` |  | required | yes |
| `gen_ai.provider.name` | `enum` |  | required | yes |
| `gen_ai.tool.call.id` | `string` |  | required | yes |
| `organization.id` | `string` |  | conditionally_required: When the session is authenticated. | no |
| `session.id` | `string` |  | recommended: Claude Code sends it unless `OTEL_METRICS_INCLUDE_SESSION_ID=false`; nami derives sessions from it. | yes |
| `span.type` | `string` |  | required | no |
| `success` | `boolean` |  | required | yes |
| `terminal.type` | `string` |  | recommended: When Claude Code detects the terminal. | no |
| `tool_use_id` | `string` |  | required | yes |
| `user.email` | `string` |  | conditionally_required: When the session is signed in. | no |
| `user.id` | `string` |  | required | no |
