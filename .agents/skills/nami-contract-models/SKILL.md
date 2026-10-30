---
name: nami-contract-models
description: Evolve Nami TypeSpec contracts and generated C#/TypeScript consumers across ingestion, stored JSON, DuckDB rows, tools and the private engine. Use for output profiles, exact encodings, presence, unions, structural mappings and package maintenance.
---

# Nami model generation

## Authority and scope

The user authorizes suitable preview, beta, RC, dev and experimental features, their compiler
flags and emitter configuration, and maintenance of this skill. Use the APIs actually resolved
by the project; installation or online documentation alone does not establish an available API.

Nami's own Collector, compile-time instrumentation of declared supported application call sites,
and configured observation lifetimes are binding product requirements. Preserve I1–I18 and
D1–D6 with their limits in
`/Users/alexandernachtmann/RiderProjects/nami/docs/prd/nami-request-response-review.md`.
Self-instrumentation covers Nami's operations; it does not replace application instrumentation.
An unimplemented requirement remains open. Keep snapshot completeness, producer capture and
loss-free delivery distinct. Do not reopen the Collector choice or infer a product SDK/CLI from
build and launcher scripts. Qyl remains retained. After consolidation, the user explicitly authorized
deleting `nami.playground`; its directory was removed on 27 October 2026. Temporary migration
archives and audit reports were removed at the user’s request on 30 October 2026. See
`docs/integration/workspace-handoff.md`.
No contract change authorizes deleting Qyl or starting another prototype migration.

TypeSpec owns Nami data structures, representation metadata and structural mappings. Weaver owns
SemConv keys, successors, stability and persistence/redaction policy. Upstream OTLP protocol types
own the ingestion envelope. Semantic algorithms, transport execution, query planning and runtime
resource ownership remain executable code; do not introduce duplicate application schemas there.
Do not preserve unreleased compatibility paths merely because they already exist.

## Current evidence and navigation

Use the current source and generated manifests. Historical audit counts are not a current verification result.

- `contracts/model-migration.csv`: candidates, current declarations, TypeSpec owner and authored portions.
- `contracts/generated/model-manifest.json`: consumer roots, forms and properties.
- `contracts/generated/consumer-files.json`: generated consumer file ownership.
- `contracts/emit-config.tsp`: output selection.

Regeneration verifies parity, not the absence of handwritten models. Read actual consumers before
migrating a candidate. Use the repository verification entry points for the current source.
Paths below are relative to `/Users/alexandernachtmann/RiderProjects/nami` unless absolute.

Already implemented; do not restart these migrations:

- Consumer-specific C# forms/namespaces/files/JSON contexts; shared property semantics; type-identity traversal.
- Typed ingestion slots, explicit normalization `values`/`renames`, metric variants and `LogTimeOrigin`.
- Generated `StoredAttributeValue` hierarchy, recursive storage/API projections and canonical writers.
- Generated ingestion→row and storage→tool structural mappings, plus Weaver-key hot-attribute extraction.
- Generated option fields/defaults/bounds, credential document, environment bindings and adapter defaults.
- TypeSpec rows → C# DuckDB metadata → Roslyn DDL, binders, readers, upserts, bootstrap metadata and sequences.
- Generated tool associations, field selection metadata, runtime result forms and wire-root revision scope.
- Removal of obsolete private-schema backfills, duplicate snapshot interface and fallback plain-text body parsing.

Authored code still includes identity/canonicalization rules, semantic calculations, query/retention
SQL, transaction/scheduling/transport behavior, configuration validation methods and JSON support
primitives such as `OptionalValue<T>`. Generator intermediate representations, private algorithm
keys, UTF-8 rewrite state and process credentials are support state. The inventory does not label
these as TypeSpec output. Normalized collections, ingestion records, options and `ToolWindow` have
authored behavior partials; generation of their fields does not generate their behavior.

## Modeling and effective property semantics

Use named representations when field sets, identity roles, tags, encoding or presence differ.
Within one compiler program use TypeSpec type identities. Persistent names need namespaces and
instantiation identity; resolve language names afterward. Select roots/dependencies by consumer,
not by one global API namespace or every model in the program.

`contracts/emitters/csharp/src/semantics.ts` resolves property semantics. The language emitters and
`contracts/emitters/ts-types/src/serialization.ts` must preserve code/wire names, effective encoding,
required/optional, nullable/nonnullable, defaults, exact bounds and inner collection codecs.

- `property.name` is the code name; `resolveEncodedName(program, property, "application/json")` is the JSON name.
- `getEncode(program, propertyOrScalar)` supplies encoding and encoded type; inspect property overrides and scalar inheritance.
- Full `int64`/`uint64` use C# `long`/`ulong` and precise schema-declared JSON forms, normally decimal text. TypeScript transport values use decimal text. A bigint domain form needs a generated codec. Safe bounded values such as `maxBytes` remain numbers.
- Use `getMinValueAsNumeric`/`getMaxValueAsNumeric`; do not round exact bounds through JavaScript `number`.
- Optional nullable values have three states: missing, null, value. The optional wrapper must retain the inner value codec. See `internal/nami.contracts/WireConverters.cs`.
- Map keys are payload. A fixed-field object plus arbitrary entries needs explicit semantics; use a `values` map when that is the model, rather than losing fields through string-indexer handling.

`ToolReadInput.maxBytes` is optional with default 32768. Application TS uses `maxBytes`, C# uses
`MaxBytes`, JSON uses `max_bytes`; the normalized runtime schema does not put it in `required`.
The raw JSON-Schema intermediary still has code spelling; consumers use the normalized bundle.
The Nami snake_case linter is local policy. Removing `@encodedName` alone does not change that policy.
Encoded names select media type, not recipient. Separate models are justified by differing contracts.

## Ingestion, stored JSON and identity

`contracts/models/ingestion.tsp`, `ingestion-records.tsp` and `ingestion-metrics.tsp` own collector
values and provenance. Keep explicit renames, including `canonicalKey: null` for deprecation without
a successor. Keep producer event/observed times, collector receipt and untimed request ordinal
separate from database arrival sequence and retransmission identity.

Gauge and Sum share the number payload; five metric kinds map to four stored payload tags.
Temporality/monotonicity belong to the applicable variants. `MetricPointRow.Value` is a double query
projection, not the original observation. Counts/nanoseconds retain unsigned roles; attributes retain
signed integer roles. Preserve missing number values, recursive attributes, bytes and nonfinite floats.

`contracts/models/storage-json.tsp` and `contracts/emitters/csharp/src/mixed-union.ts` generate stored
and API mixed unions. Storage object tags are `type`; API tags are `kind`. Conversion is recursive
at variant boundaries; keys named `type` or `kind` within a payload map remain unchanged.
`StoredAttribute.Value` is required and nullable. `StoredAttributeValue.StableString` generates the
canonical representation with explicit key ordering and number spelling. Changes to hash inputs
are identity changes and require checking their actual consumers.

`OtlpConverter.MetricIdentity`, `MetricObservationCodec`, `MetricStorageMapper` and
`IngestionStorageMapper` retain named identity algorithms: raw attribute Protobuf identity,
instrument/series identity, full observation identity, normalized measurement identity and original
log-body identity. Display truncation does not replace the original hash basis.

The 2026-10-27 projection cleanup now retains wrong-typed `gen_ai.operation.name` and
`gen_ai.output.type` in span attribute JSON while leaving their string query columns null. Earlier
code dropped those malformed values. This is an intentional span-evidence change; do not infer an
unrelated log/metric identity change from it.

## Structural projections, configuration and storage

`contracts/emitters/csharp/src/projections.ts` owns `@modelProjection` and `@projectionSource`.
The owner is a computed-values model or an explicit output namespace. Every target property needs
a source or computed input; unused inputs fail. Explicit source references are checked by TypeSpec
property identity, preventing same-name properties from another model. Generated
`ModelMappings.ToModel` runs in the consuming project's namespace. Keep calculations/decoding in
consumers and structural assignment in generation. `@catalogValue` uses Weaver catalog symbols.

`contracts/emitters/csharp/src/outputs.ts` owns output profiles and `@configuredBy`, `@csharpSetter`,
`@csharpInternal`. Mutable option fields/defaults/bounds come from
`contracts/models/configuration-options.tsp`; environment names are independent of JSON names.
`CONFIG_DEFAULTS` is generated as exact strings for the TypeScript adapter. Credential roots use
strict generated metadata (unknown/duplicate members rejected and nullable annotations respected).
Semantic uniqueness, role/project rules and startup checks remain runtime validation.

Rows in `contracts/models/storage-rows.tsp` become `internal sealed partial record`. The Roslyn
owner is `internal/nami.collector.storage.generators`. Every public getter is a column; provenance
is internal. Emit intentional property order. Primary-key ordinals and named parameter-set order
have distinct meanings. JSON encoding is independent of SQL type: `ulong` binds as `UBIGINT`,
while JSON columns bind strings with `SqlType = "JSON"`.

Table conflict policy and per-column merge policy generate upserts (replace/coalesce/min/max/
minPresent/longest); do not restore authored table-specific SQL strings. `SchemaMetadataRow` is a
TypeSpec bootstrap table. Sequence metadata generates DDL/defaults, including insert exclusion for
`IngestSequence`. Schema hash v2 covers bootstrap and declared sequences. Populated incompatible
or unidentified databases are rejected without adopting/rewriting their contents; empty owned
schemas may recreate, and derived projections can rebuild. There is no released-data migration
promise. Query and retention algorithms remain authored SQL, not generated persistence structure.

Storage's main JSON context no longer enables named floating-point literals globally. Ordinary
records retain fast generated writers. `contracts/models/storage-numeric-json.tsp` selects a separate
`StorageNumericJsonSerializerContext` for standalone doubles; property-specific encoding remains
on fields that need it. Do not widen the whole context to fix one numeric root.

## Union and assertion verification

Use the canonical [C# union skill](../csharp-union-types/SKILL.md); the explicit external plugin
skill and examples were also reviewed during this migration. Closed intermediate classes and sealed
leaves preserve hierarchy completeness. Switch expressions match union contents directly; their
explicit null arm rejects invalid default unions. Semantic outer null remains its declared value.
JSON discriminators remain TypeSpec-owned; reject missing and unknown tags at the boundary.

`tests/Nami.IntegrationTests/UnionCompilerTests.cs` compiles actual generated sources/consumers
with the pinned SDK. Added cases must fail the unchanged consumer. Production
`Directory.Build.props` promotes CS8509/CS8655/CS8846; the test does not invent a separate warning policy.
The targeted native probe in `UnionCompilerTests` executed 15 wire and 8 stored roundtrips,
rejected 7 default states and 12 invalid tags, with reflection disabled. These are recorded targeted
results, not a replacement for the current full gate or proof of unrelated instrumentation.

Assertions distinguish exact exceptions from exception families, ordered sequences from multisets,
and complete payload comparisons from counts. `JsonNode.Parse` can throw a `JsonReaderException`
derived from `JsonException`; the stored-body contract tests the family with `Throws<JsonException>`.
The migrated fixture assertions use unordered sequence equality when file order is irrelevant and
`ContainsSingle` for singleton XML elements. Do not weaken semantic assertions to silence failures.

## Package maintenance and resolved APIs

The user authorizes `@typespec/protobuf`, `@typespec/graphql`, `@typespec/streams`, `@typespec/sse`,
`@typespec/events`, `@typespec/rest`, `@typespec/versioning` and needed experimental compiler APIs.
These are two emitters and five libraries. Installation does not activate a transport or emitter.
Streams/SSE do not implement framing, backpressure, cancellation or delivery. Temporal version views
do not represent simultaneous storage/API projections or infer safe database migrations.

Read [references/typespec-packages.json](references/typespec-packages.json) for resolved entry points
and hashes. The active `next` set pins compiler 1.18.0-dev.13, HTTP 1.18.0-dev.6, JSON Schema
1.18.0-dev.2, OpenAPI 1.18.0-dev.1 and OpenAPI3 1.18.0-dev.7. The exact preview-library
and framework pins are in `contracts/package.json` and the installed-entry reference. The
2026-10-30 reference was collected from an isolated frozen-lockfile install; the parent shared
workspace gate must validate the merged sources. Earlier 1.17.0 and separate dev.8 preview
observations are historical, not evidence about the active APIs.

Use `/Users/alexandernachtmann/RiderProjects/nami/eng/typespec.sh --check` for current registry
metadata and `--apply` for an authorized update; `--check --offline` uses cached knowledge.
`contracts/typespec-packages.json` selects npm `next`; `--tag` explicitly overrides the channel.
The updater uses six concurrent conditional version-document requests, resolves compatible peers,
synchronizes exact compiler peers in local workspaces, validates their other managed TypeSpec
constraints and combined library imports, and checks generated parity. Failure or changed generated
output restores package, local workspace manifest, lock and reference files before a frozen reinstall.
It does not silently migrate schemas or install a scheduler. Compiler 1.18.0-dev.13 still lacks its
declared secondary module-resolver declaration; primary entries/imports are checked separately.

`getVersioningMutators` and `unsafe_mutateSubgraphWithNamespace` were exercised against the separate
preview installation, not established as Nami multi-version delivery. Select one view centrally and
feed it consistently to all emitters. Read the active declaration and implementation before use.

## Observed recurring failures and applied fixes

- **TypeSpec 1.17 reserved identifiers:** a decorator argument called `projection` and a test object key
  called `namespace` each failed parsing. `/Users/alexandernachtmann/RiderProjects/nami/contracts/emitters/csharp/lib/main.tsp`
  uses `additional`; `/Users/alexandernachtmann/RiderProjects/nami/contracts/scripts/lib/row-projection.test.mjs`
  escapes the reserved object key. Generation and the compiler fixtures passed after those changes.
- **RC3 completeness severity:** `.editorconfig` alone left union incompleteness as a warning in the
  generated-source consumers. Explicit `WarningsAsErrors` in the production props fixes it; the real
  added-case probes verify the policy.
- **Incomplete IDE build graphs:** Debug cleanup removed imports for missing Release-generated symbols.
  Prepare Release and use `--properties=Configuration=Release`; never accept an unused-import removal
  justified only by an unresolved generated reference. The old `StorageDefaults` symbol is now retired.
- **Shared capture compilation:** files compile both in the repository and in isolated consumers.
  Explicit Diagnostics type aliases in `tests/Nami.Tests/Capture` preserve bindings without ambient global
  usings. Run the existing managed/native capture consumer checks when changing them.
- **Fixture ownership:** separate `await using var project = new FixtureProject(...)` from configuration
  to keep CA2000 ownership visible. `FixtureDiagnostics.None/Full` is explicit; only the MSTest adapter
  reads `TestContext.Current`. Child-process lifetime belongs to `OwnedProcess`/`ChildProcess`.
- **Inspection fingerprints:** editing source/docs during a running check invalidates that evidence.
  Finish the process before edits. This occurred during this migration; that inspection was discarded.
- **Skill validation:** if local Python lacks PyYAML, the working invocation is
  `uv run --offline --no-project --with PyYAML python /Users/alexandernachtmann/.codex/skills/.system/skill-creator/scripts/quick_validate.py /Users/alexandernachtmann/RiderProjects/nami/.agents/skills/nami-contract-models`.

Use the repository verification entrypoints in `AGENTS.md`. On a recurring failure, update the
existing case with symptom, demonstrated cause, applied fix, exact package/source and result.
Keep proposals separate from fixes that actually worked; replace stale locations and resolved work.
