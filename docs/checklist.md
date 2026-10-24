# nami implementation checklist

The product requirements are [docs/prd/nami.md](prd/nami.md). This checklist records the migration
approved on 2026-10-24: **TypeScript MCP + C# NativeAOT engine + TypeSpec contracts**, with
compile-time instrumentation and explicit observation lifetime. Work only in this repository;
qyl is a read-only implementation reference. The earlier ten-workstream plan remains in Git history
and is not an instruction to launch parallel agents or skip tests.

## Current implementation versus target

The repository currently contains the C# host in `src/nami`, ingestion and DuckDB, six tools,
TypeSpec-generated C# contracts and JSON Schemas, Weaver output, and three executable MSTest suites.
The new TypeScript host, private engine transport, connector bundles and full instrumentation proofs
are **pending**. The documentation change does not implement or verify them.

Earlier successful Release runs and native proofs are recorded in [verification.md](verification.md).
Those results concern the previous host and must not be presented as evidence for the new architecture.
Keep its real OTLP, real DuckDB, three-signal, six-tool and persisted-snapshot proofs during migration.
A checked item below records only the work explicitly described, not completion of the whole product.

## 1. Product and architecture documentation

- [x] Describe exclusively agent-operated debugging; no dashboard or human-facing analysis UI.
- [x] Define Claude.ai connector and/or OpenAI plugin with bundled MCP as product integrations;
      local MCP clients remain supported.
- [x] Correct Tomo to an example Rust developer whose application an agent debugs; remove the
      assumption that a particular Rust service is the product's only producer or consumer.
- [x] Replace the single-C#-binary requirement with TypeScript MCP and a native C# engine.
- [x] Retain TypeSpec and both language outputs; keep Weaver as semantic/persistence authority.
- [x] Add PRD I1–I18 and D1–D6, with all supplied instrumentation methods and explicit proof scope.
- [x] Distinguish the approved target from current executable behavior in entry documentation.

## 2. Contract generation — `contracts/`, `internal/nami.contracts/`

- [ ] Restore/adapt qyl's TypeScript emitter for the six nami input/result pairs and shared types.
      Proposed output: `src/nami.mcp/src/contracts/generated/`; generated files are never edited by hand.
- [ ] Model private request/result framing and explicit project-bound state identifiers in TypeSpec.
      Preserve existing tool payloads and wire names unless the PRD explicitly changes them.
- [ ] Generate one semantic revision for both sides; reject incompatible private peers before dispatch.
- [ ] Preserve `kind`, required fields, absent/null distinctions and exact integer encodings across
      C#, TypeScript and enabled Rust consumers (S, I8/I9, D2).
- [ ] Extend `./eng/build.sh Contracts` and parity checks to include the TypeScript artifacts.
- [ ] Keep C# serialization AOT-safe with generated `JsonTypeInfo<T>`; validate TS inputs and outputs
      with the same generated JSON Schemas.

## 3. TypeScript MCP and engine process — `src/nami.mcp/`, `src/nami/`

`src/nami.mcp/` is a proposed target path and is not an existing implementation claim.

- [ ] Build the MCP layer from qyl's TypeScript patterns using the official v2 SDK, strict typing,
      bounded agent results and the six existing read-only tools.
- [ ] Move MCP transport, registration, result framing and host-facing authentication to TypeScript.
      Remove C# MCP SDK/runtime dependency only when replacement behavior is proven.
- [ ] Keep C# ingestion, storage and analysis in the native engine; preserve pure snapshot-based tools.
- [ ] Add private long-lived UTF-8 JSON streams/pipes with framing, request correlation, contract
      revision, bounded buffers, cancellation, backpressure and process-exit handling (D2–D6).
- [ ] Let the local TypeScript launcher supervise the engine and own its private streams. Keep MCP
      stdout separate from internal frames and route diagnostics to stderr.
- [ ] Stream C# serialization into the transport with generated metadata. Avoid unnecessary whole-body
      copies; measure memory/latency before claiming improvement. No shared-heap assumption or required FFI.
- [ ] Preserve authenticated project isolation and trace context through the private boundary (D5).
      Neither public tool arguments nor unverified metadata choose a project.
- [ ] Package both processes and all declared runtime/native dependencies in one release. One native
      file and an internal HTTP API are not requirements.

## 4. Instrumentation and observation lifetime — owning paths to be finalized from existing generators

Do not infer that a class or generator exists from its name in this plan. Inspect and reuse actual
instrumentation support before introducing a replacement. C# interception applies to supported
C# call sites; other languages require their appropriate instrumentation path.

- [ ] Enumerate the full declared model manifest, pinned registry, definitions and supported call-site
      forms; diagnose invalid markers and record explicit exclusions (I1–I3).
- [ ] Enforce Stable-only selection at model, attribute and constrained-enum levels when requested;
      retain explicitly selected Development semantics for the rest of the stack (I2).
- [ ] Bind supported call sites to generated wrappers at build time; preserve receiver/argument
      evaluation, exact-once invocation, result, side effects, exception and cancellation (I4).
- [ ] Observe true async completion and supported enumeration lifetimes; restore context under
      nesting, parallelism, failure and cancellation (I5/I6).
- [ ] Cover declared Resource/Span/Metric/Event forms, distinguishing log and span events and
      explicitly representing general logs in the supported scope (I7).
- [ ] Apply XML lifetime rules in runtime code: explicit project-bound observation identity,
      absolute/inactivity deadlines, renewal and idempotent termination; no hidden 15-minute cap (I11/I12).
- [ ] Verify duration units/monotonic timing and the documented one-second controlled visibility
      bound, with recording/export/storage/query evidence separated (I13/I16).
- [ ] Prove complete recording for the promised scope without hidden parent sampling, correct metric
      aggregates and temporality, visible retry/rejection/drop accounting, finite self-observation and
      accountable draining at shutdown (I14/I15/I17/I18).

## 5. Agent distribution — Claude.ai and OpenAI

- [ ] Implement and verify the Claude.ai remote connector with project-bound authentication.
- [ ] Implement the OpenAI plugin/bundled MCP integration against that host's current requirements;
      document the exact supported product surface rather than assuming all OpenAI hosts are identical.
- [ ] Include the skills/metadata needed for agents to build, run, inspect and query the stack within
      their authorized scope; no dashboard or human-facing analysis workflow.
- [ ] Prove host parity for six tool names, input/output schemas, errors, freshness, byte bounds and
      annotations (D1). Distinguish actual host tests from local SDK/protocol fixtures.
- [ ] Record unavailable external credentials or host review as an outstanding proof, not a passing test.

## 6. Verification and release

- [ ] Keep G/R/S/T baseline proofs while adapting them to the two-process stack.
- [ ] Add every method named in PRD I1–I18 and D1–D6 to the appropriate proof suite; data-drive model
      checks from the complete generated manifest, with deliberate invalid and boundary fixtures.
- [ ] Unit/Integration cover compiler, contract, controlled-clock and transport failure cases.
      EndToEnd starts the published TypeScript host and NativeAOT engine and verifies real OTLP/DuckDB.
- [ ] Lifecycle helpers establish isolated state and ready receivers, execute a controlled operation,
      wait for its actual completion condition with a deadline, and then drain/tear down.
- [ ] An existing test name, a skipped case, an empty run, successful serialization or a successful
      flush alone never proves the new requirement. Record traceable results and remaining exclusions.
- [ ] Run `./eng/build.sh Check --Configuration Release` before every push. Use `./eng/test.sh`
      for all executable suites or `--suite Unit|Integration|EndToEnd`; preserve MTP exit codes.

## Repository conventions

- .NET targets `net11.0`; retain the repository SDK/language pins until a separately validated change.
- `Nami.Tests` is shared support. Executable suites are `Nami.UnitTests`, `Nami.IntegrationTests`,
  `Nami.EndToEndTests`, each with its own `testconfig.json`, MSTest.Sdk 4.5.1 and native MTP.
- No custom test `Main`, VSTest fallback or ignored MTP exit code. The runner must reject empty/skipped runs.
- Build/tool CPU parallelism is two; tests use two workers and one module at a time.
- Do not overlap full IDE analysis, InspectCode and the full gate. Use `./eng/inspect.sh`; a narrow
  inspection cannot support a solution-wide claim.
- Centralize configuration constants in
  `src/nami.collector.ingestion/Configuration/CollectorConfigurationKeys.cs`; the owning options type
  defines defaults and validation. Cross-process configuration must come from the declared contract.
- Regenerate contracts with `./eng/build.sh Contracts`; semantic artifacts with `bash registry/generate.sh`.
- Keep decisions and resolved specification mismatches in [integration/README.md](integration/README.md).
- Commit and push to `main`; keep unrelated working-tree changes out of the commit.
