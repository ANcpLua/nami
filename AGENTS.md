# Nami

Work in this repository. The previous Downloads prototype is not a build, test, generator or runtime dependency.

## Ownership

- `src/nami.mcp` is the TypeScript/Bun MCP server. `src/nami` is its C# NativeAOT engine.
- TypeSpec under `contracts/` owns Nami application data contracts, wire names, presence, exact numeric encodings, JSON schemas and C#/TypeScript outputs. Change definitions/emitters and regenerate; do not hand-edit generated artifacts or reintroduce a second contract model.
- Weaver and `registry/` own semantic-convention keys, definitions and persistence policy. Use the generated catalog in `internal/nami.semconv`.
- Upstream OTLP Protobuf messages describe ingestion. Test-only capture records describe evidence, not a second product protocol.
- Read `.agents/skills/nami-contract-models/SKILL.md` for contract or generator changes.

## Tests and fixtures

`nami.slnx` contains the four primary test projects under `tests/`: `Nami.Tests` (shared support), `Nami.UnitTests`, `Nami.IntegrationTests`, and `Nami.EndToEndTests`. Keep product projects and automation in the same solution.

- Unit: deterministic in-memory algorithms, codecs, assertions and policy evaluation.
- Integration: real SDK builds, temporary project materialization, OTLP transport, DuckDB, registry/compiler and runner lifecycle checks.
- End-to-end: the published native engine and actual TypeScript MCP adapter.
- ProcessHost, compiler and invocation probes remain generated build dependencies, not extra primary test projects.
- Shared process execution belongs to `ChildProcess`/`OwnedProcess`. Generated consumers use `FixtureProject` and remain isolated from ambient MSBuild/NuGet configuration.
- Fixture ownership, shared groups and bounded scratch cleanup follow `.agents/skills/fixture-lifecycle/SKILL.md`.
- Keep MSTest and native Microsoft.Testing.Platform at the repository pins. Use source-generated JSON metadata; preserve absent/null distinctions and exact integers.
- Name new tests for the behavior, seal new concrete test classes, and prove failures, cleanup and output contracts without weakening assertions.

## Verification

Use the repository entry points, which resolve the pinned toolchain and preserve diagnostics:

- `./eng/build.sh Contracts` regenerates TypeSpec outputs.
- `./eng/build.sh Compile --Configuration Release` builds the solution and TypeScript MCP.
- `./eng/test.sh --suite Unit`, `--suite Integration`, or `--suite EndToEnd` run the selected suite; `./eng/test.sh` runs all three.
- `./eng/build.sh Check --Configuration Release` runs the complete registry/contract/test/native gate.
- `./eng/inspect.sh`, `./eng/inspect.sh --target typescript`, and `./eng/inspect.sh --target mcp` inspect the current implementation.
- `mise diagnostics` shows current results; use the bounded `--results current` queries rather than historical logs.

Preserve user changes. Do not edit source during build/test/inspection; a changed fingerprint invalidates the result. Never suppress a failing check to manufacture a clean handover. See `docs/testing.md` and `docs/integration/test-harness.md`.
