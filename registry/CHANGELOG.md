# Registry changes

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
