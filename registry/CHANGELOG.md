# Registry changes

## 0.5.0 — 2026-11-16

- Core: `open-telemetry/semantic-conventions` tag `v1.45.0`, commit
  `fb30ca17193b551473b1f43c315482918ab6987c` (unchanged, imported through GenAI).
- GenAI: `open-telemetry/semantic-conventions-genai` commit
  `e6658f14c9d9ea0760825f029cb82e7673f48fab` (unchanged).
- Weaver: `0.28.0` (unchanged).
- nami stores telemetry as it was sent (observer's decision, 16 Nov 2026); the credential stage
  and the size bounds are the only things ingest does to a record. The registry therefore no
  longer steers ingest. Removed: the annotations `nami.persisted`, `nami.token_exempt`,
  `nami.producer`, `nami.instrumentation_scope`, `nami.wire_type`, `nami.wire_container` and
  `nami.max_utf8_bytes`; their policy rules (`nami_persistence_required`,
  `nami_token_exemption_requires_persistence`, `nami_wire_type_invalid`,
  `nami_wire_type_redundant`, `nami_wire_container_invalid`, `nami_producer_scope_required`,
  `nami_max_utf8_bytes_invalid`); `source/nami/persistence.yaml`; and the generated
  `RegistryPolicy`, `ValueLimits`, `Producers`, `WireContainers` and the `IsPersisted`,
  `IsTokenExempt` and `Producer` members of `AttributeDefinition`.
- Removed `source/claude_code/`: 8 counters, 12 events, 5 spans and 103 attributes, with
  `ClaudeCodeAttributes` and `docs/semconv/claude_code.md`. What Claude Code and the Claude API
  send is recorded, without any nami policy, in `docs/telemetry/claude-code-anthropic-reference.md`.
- Kept: nami's own `nami.*` attributes, the `nami.project` entity and the collector's counters.
  A `nami.*` key still needs a brief and a stability. The briefs of
  `nami.collector.attributes.dropped` and `nami.collector.attributes.sanitization_failed` now say
  what the counters count since 16 Nov: attributes removed by a size bound, and strings the
  credential stage could not process.
- Added the counter `nami.collector.values.replaced` with the attribute
  `nami.value.replacement.reason` (`depth`, `invalid_utf16`): values ingest replaced while it
  kept their attribute. Until a record can say so in a field of its own, this counter is where
  such a replacement shows. Its brief names everything it counts: a replacement in an attribute
  key or value, and one in a name or a status message, where no attribute is involved.
- Advanced the Nami registry schema to 0.5.0. This does not change the product version.

## 0.4.0 — 2026-11-15

- Core: `open-telemetry/semantic-conventions` tag `v1.45.0`, commit
  `fb30ca17193b551473b1f43c315482918ab6987c` (unchanged, imported through GenAI).
- GenAI: `open-telemetry/semantic-conventions-genai` commit
  `e6658f14c9d9ea0760825f029cb82e7673f48fab` (unchanged).
- Weaver: `0.28.0` (unchanged).
- Content is no longer persisted (finish-line item 5, engine audit A2). Without an annotation
  a key is persisted, so these were stored and returned verbatim: the prompt, response and
  system prompt (`gen_ai.input.messages`, `gen_ai.output.messages`,
  `gen_ai.system_instructions`), tool calls and definitions (`gen_ai.tool.call.arguments`,
  `gen_ai.tool.call.result`, `gen_ai.tool.definitions`), retrieval and memory content
  (`gen_ai.retrieval.documents`, `gen_ai.retrieval.query.text`, `gen_ai.memory.query.text`,
  `gen_ai.memory.records`), prompt template variables (`gen_ai.prompt.variable.<key>`), and
  HTTP bodies (`http.request.body.content`, `http.response.body.content`). Each now carries
  `nami.persisted: false`, so ingest drops it.
- Claude Code (M2): `registry/source/claude_code/` describes the telemetry of Claude Code
  2.1.325 as the capture of 15 Nov 2026 shows it (`tests/Nami.IntegrationTests/Fixtures/ClaudeCodeOtlp`)
  and its monitoring documentation: 8 counters, 12 events, 5 spans and 103 attributes, all
  development. The attributes keep Claude Code's wire names and carry
  `nami.producer: claude_code`; code generation groups them in `ClaudeCodeAttributes` and
  `docs/semconv/claude_code.md`. A type is the type nami stores. Where Claude Code sends
  another type (`success`, `duration_ms` and the two sizes on `tool_result` as strings,
  `prompt_length` on `user_prompt`, `safe_mode` on `plugin_loaded`), the signal's reference
  carries `nami.wire_type: string` and ingest converts. `bash_command`, `full_command` and
  `subagent_type` arrive inside the `tool_parameters` JSON on `tool_decision` and
  `tool_result` (`nami.wire_container`); ingest lifts them out and drops the container. Each signal names
  the instrumentation scope it arrives under (`nami.instrumentation_scope`:
  `com.anthropic.claude_code`, `.events`, `.tracing`), so ingest can keep Claude Code's
  unprefixed keys (`model`, `type`, `success`, `source`) to Claude Code's records.
- `full_command` is persisted as a tool detail, so that the claim check can compare it with
  the gate command, and bounded: `nami.max_utf8_bytes: 4096` declares the longest value
  ingest keeps of it (observer's ruling, 15 Nov). Ingest enforces the bound.
- `error` is persisted too, because error text helps people and agents (observer's ruling,
  15 Nov): ingest passes it through the exception-text sanitizer, which removes
  credentials, and keeps at most 1024 UTF-8 bytes of it (`nami.max_utf8_bytes: 1024`). The
  key is the same on `api_error`, `tool_result` and the `claude_code.llm_request` and
  `claude_code.tool.execution` spans, so it is kept on all four.
- Never persisted (finish-line item 5): the prompt and response text (`prompt`,
  `prompt_text`, `response`, `user_prompt`), `tool_input`, `tool_parameters` as a whole,
  the raw API bodies (`body`, `body_ref`), the detailed-beta content
  (`new_context`, `system_reminders`, `system_prompt_preview`, `user_system_prompt`,
  `response.model_output`), and the `tool.output` span event's `content`, `output` and
  `diff`. Also not persisted: `organization.id`, `anthropic.plan`,
  `anthropic.custom_base_url`, `terminal.type` and `span.type`.
- Reused, not redefined: `session.id`, `user.id`, `user.email`, `vcs.ref.head.*` and
  `error.type` from core; `gen_ai.agent.name`, `gen_ai.conversation.id`,
  `gen_ai.operation.name`, `gen_ai.provider.name`, `gen_ai.request.model`,
  `gen_ai.response.id`, `gen_ai.response.finish_reasons`, `gen_ai.tool.call.id` and
  `gen_ai.tool.name` from GenAI. Claude Code's `event.name` is core's deprecated key, whose
  value belongs in the log record's EventName field. Not defined, so ingest drops them:
  `user.account_uuid`, `user.account_id`, `event.timestamp`, `link.type` and `gen_ai.system`.
- Persistence of inherited keys: `session.id` is now persisted (sessions come from it on
  events, spans and metric points); `user.id` and `user.email` are no longer persisted;
  `gen_ai.agent.name` is persisted explicitly instead of by default.
- Policies: a key with `nami.producer` needs a brief, a stability and a persistence
  annotation, like a `nami.*` key; `nami.wire_type` must be a primitive OTLP type other than
  the stored type; `nami.wire_container` must name a defined, unpersisted string attribute;
  a producer's signal must name its `nami.instrumentation_scope`; `nami.max_utf8_bytes` must
  be a positive integer on a persisted string attribute.
- Generated C#: `AttributeDefinition.Producer`, `RegistryPolicy.ProducerOf`,
  `Producers.Scopes`, `ValueLimits.MaxUtf8Bytes`, `WireContainers.Members`, and `Events` with an `EventDefinition` per event (deprecated
  events carry `[Obsolete]`). Markdown: events and spans per namespace, and the attribute
  table of each producer signal with stored type, wire type, requirement and persistence.
- Weaver 0.28.0 replaces a definition's annotations with a reference's own annotations
  instead of merging them, so a signal's entry for `success` on `tool_result` carries
  `nami.wire_type` but not `nami.persisted`. Persistence is read from the attribute's
  definition, as the generated policy always did.
- Advanced the Nami registry schema to 0.4.0. This does not change the product version.

## 0.3.0 — 2026-11-14

- Core: `open-telemetry/semantic-conventions` tag `v1.45.0`, commit
  `fb30ca17193b551473b1f43c315482918ab6987c` (imported through GenAI).
- GenAI: `open-telemetry/semantic-conventions-genai` commit
  `e6658f14c9d9ea0760825f029cb82e7673f48fab`, the head of upstream `main` on 14 Nov 2026
  (#692). GenAI imports core v1.45.0 since `f4219d1` (#675).
- Weaver: `0.28.0` (unchanged).
- `weaver registry diff` against 0.2.0: attributes 67 added, 32 renamed, 8 deprecated
  without a single successor, 1 obsoleted, 3 removed; metrics 27 added, 2 renamed,
  1 removed; spans 5 added, 1 obsoleted, 20 removed (now refinements); events 8 added;
  entities 6 added, 5 removed.
- Renames that change canonical keys at ingest: `messaging.system` to
  `messaging.system.name`; `messaging.kafka.*`, `messaging.rabbitmq.*` and
  `messaging.rocketmq.*` to `kafka.*`, `rabbitmq.*` and `rocketmq.*`;
  `messaging.gcp_pubsub.*` to `gcp.pubsub.*`; `messaging.servicebus.*` and
  `messaging.eventhubs.*` to `azure.*`; `rpc.request.metadata` and its gRPC and Connect
  variants to `rpc.request.header`; `rpc.response.status_code` and
  `rpc.connect_rpc.error_code` to `rpc.status_code`; `faas.time` to
  `faas.invocation.start_time`; `faas.invocation_id` to `faas.invocation.id`;
  `system.device` to `system.device.name`; `db.client.connection.max` and
  `db.client.connections.max` to `db.client.connection.limit`.
- Deprecated without a single successor, so ingest drops them under the deprecation policy:
  `rpc.response.metadata` and its gRPC and Connect variants (1.45.0 splits them into
  `rpc.response.header` and `rpc.response.trailer`), `rpc.grpc.status_code`,
  `rpc.jsonrpc.error_code`, `service.peer.name`, `service.peer.namespace` and `peer.service`.
- Removed upstream: `enduser.id` and `enduser.pseudo.id` (moved to the client-side
  conventions), `gen_ai.workflow.name` and `gen_ai.invoke_workflow.duration` (folded into
  `gen_ai.invoke_agent.internal`).
- Persistence: the refinements of the deprecated keys `messaging.kafka.message.offset`,
  `messaging.rocketmq.message.type`, `message.type` and `rpc.message.type` are removed;
  `kafka.offset` and `rocketmq.message.type`, the current successors, carry the same
  annotations. Deprecated keys were never persisted, so ingest is unchanged.
- Accepted diagnostics: 16 signal groups of core v1.45.0 without a requirement level and
  the two deprecated `faas` attributes without a stability field, each by exact group or
  attribute, file and commit; the two A2A span groups again, now for this GenAI commit.
  176 known diagnostics in total; the materialization is still validated strictly.
- Advanced the Nami registry schema to 0.3.0. This does not change the product version.

## 0.2.0 — 2026-10-30

- Core: `open-telemetry/semantic-conventions` tag `v1.44.0`, commit
  `e10a930844c6951757a43b849d364f7d056ac32b` (unchanged, imported through GenAI).
- GenAI: `open-telemetry/semantic-conventions-genai` commit
  `3fe84604a581b3d50dd5b4bd29383b462d2a5018`.
- Weaver: `0.28.0` (unchanged).
- Added 13 development attributes for A2A operations, agent transfers and external
  content references, two embeddings metric instruments, and A2A span definitions.
- Replaced the development span type `gen_ai.embeddings.client` with
  `gen_ai.client.embeddings`; generated consumers use the new name.
- Advanced the Nami registry schema to 0.2.0. This does not change the product version.
- Accepted only the two exact missing-requirement-level diagnostics for
  `span.a2a.client` and `span.a2a.server` in this pinned revision's
  `model/a2a/spans.yaml`; strict validation of the materialization remains required.
- Validate the current Core, GenAI, and Weaver pins together with the current publication
  and generated outputs; historical publications and stored diff reports are not required.

## 0.1.0 — 2026-10-24

Initial pin baseline; there is no preceding Nami registry version to diff.

- Core: `open-telemetry/semantic-conventions` tag `v1.44.0`, commit
  `e10a930844c6951757a43b849d364f7d056ac32b`.
- GenAI: `open-telemetry/semantic-conventions-genai` commit
  `9ffc7e00c639be7712e544e3e54331e885dccf84`.
- Weaver: `0.28.0`.
- Core is imported through GenAI to avoid ambiguous duplicate dependencies.
- Added `nami.project.id`, `nami.attribute.namespace`, `nami.attribute.key`,
  `nami.gen_ai.tool.call.arguments_hash`, the Nami project entity, and collector counters.
- Added registry-owned persistence annotations and a Rego rule requiring Nami attribute
  descriptions, stability, and persistence declarations.
- Published the strictly validated materialization as `registry/resolved.json`, with authoring
  retained under `registry/source/`, and generated deterministic C#, Rust and Markdown from it.
  See README.md for the pinned mixed-format compatibility boundary.
