# Registry changes

## 0.2.0 — 2026-10-30

- Core: `open-telemetry/semantic-conventions` tag `v1.44.0`, commit
  `e10a930844c6951757a43b849d364f7d056ac32b` (unchanged, imported through GenAI).
- GenAI: `open-telemetry/semantic-conventions-genai` commit
  `3fe84604a581b3d50dd5b4bd29383b462d2a5018`.
- Weaver: `0.28.0` (unchanged).
- Diff: `changes/0.2.0.json`.
- Added 13 development attributes for A2A operations, agent transfers and external
  content references, two embeddings metric instruments, and A2A span definitions.
- Replaced the development span type `gen_ai.embeddings.client` with
  `gen_ai.client.embeddings`; generated consumers use the new name.
- Preserved the 0.1.0 publication under `changes/0.1.0/` and advanced the Nami
  registry schema to 0.2.0. This does not change the product version.
- Accepted only the two exact missing-requirement-level diagnostics for
  `span.a2a.client` and `span.a2a.server` in this pinned revision's
  `model/a2a/spans.yaml`; strict validation of the materialization remains required.
- Included Weaver in the initial pin identity so a Weaver-only upgrade also
  requires a recorded publication diff.

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
