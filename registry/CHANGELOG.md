# Registry changes

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
