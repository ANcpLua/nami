# Nami implementation checklist

Reviewed on 2026-10-27 against source commit `a3f4e74` and the last verified code commit
`00523cc`. The approved scope is defined by [the PRD](prd/nami.md) and its
[request/response review](prd/nami-request-response-review.md); current user decisions take
precedence. Nami owns its Collector. TypeScript/Bun MCP, the C# NativeAOT engine, TypeSpec
contracts, compile-time application instrumentation and explicit observation lifetime remain
the agreed architecture and product scope.

The [I1–I18 / D1–D6 evidence matrix](integration/requirement-evidence.md) records actual source
owners, existing tests and remaining proof for every requirement. A checked item below means
only its stated scope is implemented and evidenced; it does not complete a broader I/D row.
Historical inline user questions are preserved in [review notes](integration/checklist-review-notes.md).

## Current implementation versus target

The two-process MCP/engine stack, own OTLP Collector, DuckDB persistence and six read-only tools
exist. TypeSpec generates application structures, structural mappings, configuration metadata,
wire schemas and C#/TypeScript consumers. TypeSpec-generated C# rows feed the existing Roslyn
generator for DuckDB DDL, binders, readers and persistence metadata. Identity algorithms, query
and retention logic, transactions and transport execution still have authored implementations.

Nami's own tool operations emit telemetry. A product generator that instruments supported
applications and a project-bound observation lifetime manager are still open. Test capture
helpers and generated fixture projects do not implement those product features.

The last complete Release gate at `00523cc` recorded **239 Unit + 465 Integration + 40 EndToEnd
= 744 passed, 0 failed, 0 skipped**, current generated output and unchanged wire schemas.
Native publish retained existing DuckDB trim/AOT and Apple toolchain diagnostics. This is a
historical baseline, not a fresh result for this documentation change: `mise diagnostics`
reports the build/test/contract and inspection records as **STALE** after later documentation
commits. No runtime suite was rerun for this source/documentation reconciliation.

## 1. Product and architecture documentation

- [x] Document agent-operated debugging with six read-only tools and no analysis dashboard.
- [x] Keep the own Collector, TypeScript MCP, native engine and TypeSpec/Weaver ownership explicit.
- [x] Retain all I1–I18 and D1–D6, including their limits and separate self/application scopes.
- [x] Map each requirement to actual implementation, existing evidence and remaining work;
      do not require an existing test to have the illustrative PRD method name.
- [x] Distinguish snapshot completeness, producer capture, export acceptance and loss-free delivery.

## 2. Contract generation — `contracts/`, `internal/nami.contracts/`

- [x] Generate the six tool input/result associations, shared values, errors, field selections,
      TypeScript code/JSON forms and C# forms from TypeSpec.
- [x] Generate ingestion values/provenance, stored JSON, DuckDB row structures, structural mappings,
      options/defaults/environment names and their actual consumer outputs.
- [x] Model private `call`, `health`, `cancel`, responses and limits; distinguish raw input from
      validated tool calls. Snapshot identifiers exist; observation lifetimes remain I11/I12 work.
- [x] Generate a shared wire-schema revision, excluding documentation and internal-only schema
      additions from that hash. Both peers check it; adversarial mismatch proof remains D2 work.
- [x] Exercise encoded names, absent/null/value, exact Int64 bounds, nullable arrays/maps and
      invalid representations in generated C# and TypeScript codecs (I8 evidence).
- [x] Exercise current C# union defaults/unknown tags, compiler exhaustiveness and actual NativeAOT
      writers; keep simple storage writers free of the global named-float policy (I9 evidence).
- [ ] Complete shared, manifest-driven C#/TypeScript fixtures for the entire promised I8/I9 scope.
- [x] Regenerate through `./eng/build.sh Contracts`; verify deterministic outputs, schemas and
      source-generated JSON metadata through the contract gate.

The [migration inventory](../contracts/model-migration.csv) has 193 historical/current entries;
the [consumer manifest](../contracts/generated/model-manifest.json) has 99 roots. These count
different things. Entries retained as algorithms or test/tooling infrastructure are not all
missing product models. Neither count is a full instrumentation coverage claim.

## 3. TypeScript MCP and engine — `src/nami.mcp/`, `src/nami/`

- [x] Use the official v2 SDK, generated schemas, bounded results and six read-only tools;
      C# owns ingestion, storage and analysis.
- [x] Exercise HTTP/stdio tool discovery, structured result parity, real OTLP/DuckDB and snapshot
      replay/restart against the published native engine and TypeScript adapter.
- [x] Implement private JSONL revision checks, frame/pending limits, correlation, cancellation,
      backpressure and process supervision; keep stdout/private frames/diagnostics separate.
- [ ] Complete deliberate incompatible-peer, fragmented/coalesced stream, concurrent-ID,
      slow-peer, cancellation and mid-call process-exit proofs (D2–D4).
- [x] Exercise credential roles, cross-project reads and valid/missing/malformed trace parents.
- [ ] Complete concurrent/reconnect/manipulated-identifier isolation and ambient-context proofs (D5/I6).
- [x] Build MCP as a `.esproj` member of `nami.slnx`; publish `mcp/main.js` beside the native
      engine. The Dockerfile declares Bun and native runtime dependencies.
- [ ] Verify the distributable in a clean environment without installed .NET, including its
      native libraries and channel separation (D6).

`src/nami.mcp/nami.mcp.esproj` integrates the existing Bun/package.json build into `nami.slnx`.
The JavaScript SDK is pinned, npm install is disabled and the standalone `Mcp` target delegates
to the same project. CLI build verification does not by itself prove Rider's live indexing state.

## 4. Application instrumentation and observation lifetime

- [ ] Resolve markers to pinned semantic contracts and a complete executable target manifest
      with explicit exclusions; enforce requested Stable-only selection (I1–I3).
- [ ] Generate supported call-site wrappers preserving evaluation, exact-once execution,
      return values, exceptions, cancellation and true async/enumeration completion (I4/I5).
- [ ] Prove declared signal shapes and nested/concurrent context across the application scope
      (I6/I7). Existing SDK/receiver fixtures and own-tool spans are narrower evidence.
- [ ] Exercise a genuinely generated application operation through the published stack (I10/I16).
- [ ] Implement explicit authorized project-bound observation identity, XML-configured absolute
      and inactivity deadlines, renewal and idempotent end, including beyond 15 minutes (I11/I12).
- [ ] Measure monotonic durations and acceptance-to-query visibility under declared load (I13).
      The existing last-seen timestamp test is not a one-second end-to-end latency proof.
- [ ] Prove complete promised recording including unsampled parents, metric aggregates and
      dimensions, retry/rejection/drop accounting, finite self-observation and accountable
      shutdown drain (I14/I15/I17/I18).

Snapshots, retention, capture-fixture lifetimes and request timeouts do not substitute for an
observation lifetime. The six read-only tools gain no hidden start/stop/write operations.

## 5. Agent distribution — Claude.ai and OpenAI

- [ ] Implement and verify the actual Claude.ai connector and OpenAI plugin/MCP integration,
      including each supported host's deployment and project-bound authentication.
- [ ] Include host-specific skills/metadata for agents to build, run, inspect and query the
      stack within their authorized scope.
- [ ] Prove six-tool schemas, errors, freshness, bounds and annotations on those hosts (D1).
      Local MCP fixtures and inspection are existing evidence, not hosted acceptance.
- [ ] Record unavailable credentials, deployment and host review as remaining external proof.

## 6. Verification and handover

- [x] Provide pinned, fingerprinted .NET, TypeScript/contract and MCP inspection lanes with
      separate compiler/schema/protocol findings and bounded current-result queries.
- [x] Maintain Unit/Integration/EndToEnd evidence for existing contracts, registry, storage,
      compiler, clock, transport and published-stack behavior; see [testing.md](testing.md).
- [x] Provide isolated fixture/capture/process ownership, observed readiness, deadline-bound
      evidence waits and teardown; verify the helpers themselves independently of product lifetimes.
- [x] Reject empty selections and ignored MTP exit codes; retain failure and missing-evidence
      policy checks. A test name, serialization or flush success alone is not requirement completion.
- [x] Record the completed code gate at `00523cc` with its 744-test scope and visible diagnostics.
- [ ] Close the remaining I/D matrix gaps with behavior-specific evidence and rerun the affected
      checks and complete code gate before claiming the corresponding product work complete.

## Repository conventions

- Work in this repository; the retired Downloads prototype and deleted playground are not dependencies.
- Keep the pinned .NET 11 toolchain, MSTest 4.5.2 and native MTP. `Nami.Tests` is shared support;
  `Nami.UnitTests`, `Nami.IntegrationTests` and `Nami.EndToEndTests` are executable suites.
- No custom test `Main`, VSTest fallback or ignored MTP exit code. Reject empty/skipped runs.
- Use the repository build/test/inspection entry points and preserve their exit codes. Do not
  edit sources during verification or reuse changed source fingerprints as passing evidence.
- Do not overlap full IDE analysis, InspectCode and the full gate; narrow inspections support
  only their declared scope. Earlier workstream plans do not instruct parallel agent work.
- Build/tool CPU parallelism defaults to two; tests use two workers and one module at a time.
- TypeSpec owns configuration structures/defaults/environment metadata. Generated constants live in
  `src/nami.collector.ingestion/Generated/CollectorConfigurationKeys.g.cs`; authored partial
  option types retain semantic validation. Change the TypeSpec definition/emitter and regenerate.
- Weaver owns semantic vocabulary and persistence policy. Regenerate it with `registry/generate.sh`.
- Keep historical decisions in [integration/README.md](integration/README.md) and current evidence
  in the [requirement matrix](integration/requirement-evidence.md).
- Commit and push to `main`; preserve unrelated user changes.
