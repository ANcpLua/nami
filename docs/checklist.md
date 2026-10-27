# nami implementation checklist

The draft product requirements are [docs/prd/nami.md](prd/nami.md), under review; current user
decisions take precedence. This checklist records the migration
approved on 2026-10-24: **TypeScript MCP + C# NativeAOT engine + TypeSpec contracts**, with
compile-time instrumentation and explicit observation lifetime. Work only in this repository;
No predecessor checkout is required. The earlier ten-workstream plan remains in Git history
and is not an instruction to launch parallel agents or skip tests.

## Current implementation versus target

TypeScript MCP now supervises the C# NativeAOT engine through bounded private JSONL streams.
TypeSpec generates both language types, stream limits, configuration names and the six-tool catalog.
The EndToEnd suite exercises this stack, including OTLP, project isolation and restart.
DuckDB.NET is 1.5.6.1; its bundled native engine is still 1.5.6.

The product roadmap still includes full instrumentation-model proofs and hosted Claude.ai/OpenAI distribution. Those are product features, separate from the completed prototype test-harness integration. The current testing workflow and handover are documented in [testing.md](testing.md) and [integration/test-harness.md](integration/test-harness.md). Tests may be added or adapted for the behavior under change; old one-turn prohibitions are not repository policy.

## 1. Product and architecture documentation

- [x] Describe exclusively agent-operated debugging; no dashboard or human-facing analysis UI.
- [x] Define Claude.ai connector and/or OpenAI plugin with bundled MCP as product integrations;
      local MCP clients remain supported.
- [x] Describe Tomo as an example developer whose application an agent debugs; remove the
      assumption that a particular service is the product's only producer or consumer.
- [x] Replace the single-C#-binary requirement with TypeScript MCP and a native C# engine.
- [x] Retain TypeSpec and both language outputs; keep Weaver as semantic/persistence authority.
- [x] Add PRD I1–I18 and D1–D6, with all supplied instrumentation methods and explicit proof scope.
- [x] Distinguish the approved target from current executable behavior in entry documentation.

## 2. Contract generation — `contracts/`, `internal/nami.contracts/`

- [x] Maintain the TypeScript emitter for the six nami input/result pairs and shared types./// warum sind es genau six input result pairs und was ist shared types?
      Output: `src/nami.mcp/src/contracts/generated/`; generated files are never edited by hand.  /// wieso sollte man generierten code ändern statt den generator?
- [x] Model private request/result framing and explicit project-bound state identifiers in TypeSpec. /// warum wird hier privat und von framing gesprochen? was hat es mit dem explicit project bound state identifierst für einen hintergedanken?
      Define tool payloads and wire names in TypeSpec; update consumers together when they change. /// was soll preserved werden es gab nie etwas das tool payloads oder wire names in production genutzt geschweige den überhaupt jemals released wurde nami ist in privat auf github LOL, das prd wird wie eine regeln enforced bitte nicht solange wir für ordnung gesorgt haben
- [x] Generate one semantic revision for both sides; reject incompatible private peers before dispatch. /// was ist semantic revision, von welchen both sides wird hier gesprochen, was wird rejected von woher und warum? bahnhof:incompatible private peers before dispatch
- [ ] Preserve `kind`, required fields, absent/null distinctions and exact integer encodings across
      C# and TypeScript (S, I8/I9, D2). /// alles was mit rust zu tun hat ignoriere das ist falsch info rust ist ein anderes projekt das wie nami ist nur in rust und rust only nichts hat mit nami zu tun und kannst du extrahieren in riderprojects/rustgraveyard(folder erstellen) danach die frage warum muss kind require fields absent null distincation and exact integer encodings across c# typescript für welche dinge getan werden?
- [x] Extend `./eng/build.sh Contracts` and parity checks to include the TypeScript artifacts. /// was ist dieses eng/build.sh contracts und für was ist es erstellt worden, parity checks sind was und welche typescript artifacts wird hier gesprochen?
- [x] Keep C# serialization AOT-safe with generated `JsonTypeInfo<T>`; validate TS inputs and outputs
      with the same generated JSON Schemas. /// ist das jsontypeinfo<T> die neue .NET 11 system.text api? wenn nein wir wollen die neue api dafür folgende skills laden /Users/alexandernachtmann/.codex/plugins/cache/dotnet-agent-skills/dotnet11/0.1.2/skills/process-api-net11 /Users/alexandernachtmann/.codex/plugins/cache/dotnet-agent-skills/dotnet11/0.1.2/skills/system-text-json-net11

## 3. TypeScript MCP and engine process — `src/nami.mcp/`, `src/nami/`

`src/nami.mcp/` contains the implemented MCP adapter and supervisor.

- [x] Build the MCP layer from the generated TypeSpec contracts using the official v2 SDK, strict typing,
      bounded agent results and the six existing read-only tools.
- [x] Move MCP transport, registration, result framing and host-facing authentication to TypeScript.
      Remove C# MCP SDK/runtime dependency only when replacement behavior is proven.
- [x] Keep C# ingestion, storage and analysis in the native engine; preserve pure snapshot-based tools.
- [x] Add private long-lived UTF-8 JSON streams/pipes with framing, request correlation, contract
      revision, bounded buffers, cancellation, backpressure and process-exit handling (D2–D6).
- [x] Let the local TypeScript launcher supervise the engine and own its private streams. Keep MCP
      stdout separate from internal frames and route diagnostics to stderr.
- [x] Stream C# serialization into the transport with generated metadata. Avoid unnecessary whole-body
      copies; measure memory/latency before claiming improvement. No shared-heap assumption or required FFI.
- [x] Preserve authenticated project isolation and trace context through the private boundary (D5).
      Neither public tool arguments nor unverified metadata choose a project.
- [x] Package both processes and all declared runtime/native dependencies in one release. One native
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

- [x] Provide pinned, fingerprinted TypeScript/contract and isolated mcpdo inspection lanes;
      keep compiler, schema and protocol findings separate and report the actual probe scope.
- [x] Keep the existing G/R/S/T baseline cases while adapting them to the two-process stack;
      all 188 existing cases pass. This does not prove the newly specified I/D catalog.
- [ ] Add every method named in PRD I1–I18 and D1–D6 to the appropriate proof suite; data-drive model
      checks from the complete generated manifest, with deliberate invalid and boundary fixtures.
- [ ] Unit/Integration cover compiler, contract, controlled-clock and transport failure cases.
      EndToEnd starts the published TypeScript host and NativeAOT engine and verifies real OTLP/DuckDB.
- [ ] Lifecycle helpers establish isolated state and ready receivers, execute a controlled operation,
      wait for its actual completion condition with a deadline, and then drain/tear down.
- [ ] An existing test name, a skipped case, an empty run, successful serialization or a successful
      flush alone never proves the new requirement. Record traceable results and remaining exclusions.
- [x] Run `./eng/build.sh Check --Configuration Release` before this migration's push. Use `./eng/test.sh`
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
