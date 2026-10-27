---
name: nami-contract-models
description: Evolve Nami TypeSpec models and their C#/TypeScript outputs across collector ingestion, domain values, stored JSON, DuckDB rows, MCP tools and the private engine. Use for emitter scopes, type identity, encoded names and values, presence/nullability, metric variants, normalization provenance, structural mappings, schema-version views and recurring preview/compiler problems. Work from the recorded local sources and retain fixes learned in the actual repository.
---

# Nami model generation

## Authority and working approach

Use preview, beta, RC, dev, nightly and experimental features when they serve the architecture.
The user authorizes the corresponding language features, libraries, compiler APIs, emitters,
feature flags, package configuration and independent maintenance of this skill. This includes
.NET 11 RC3/C# 15, TypeScript 7.1 Beta and TypeSpec experimental APIs. Select the implementation
from the versions actually resolved by the project.

Work from the current source map and audit below. Read the actual symbols needed for a change;
update a reference when its source or package moves. Later dated observations and line anchors
are historical evidence, not assertions about current source locations or remaining work.

The current user instruction owns scope. Nami's own Collector, compile-time instrumentation of
declared supported application call sites, and configured observation lifetimes are binding product
requirements. Preserve I1–I18 and D1–D6 with their limits in
`/Users/alexandernachtmann/RiderProjects/nami/docs/prd/nami-request-response-review.md`.
Existing self-instrumentation covers Nami's own operations; it does not replace application
instrumentation. Missing implementation is an open requirement, not an optional or cancelled scope.
Keep snapshot completeness, producer capture and loss-free delivery distinct. Do not reopen these
decisions as a choice between Nami and the OpenTelemetry Collector. Unreleased wire names,
compatibility paths and implementation details may be simplified within these requirements; do not
retain them merely because an earlier file describes them.
TypeSpec owns Nami data structures, representations and structural mappings. Named semantic
algorithms, transport execution and external protocol implementations have their own code;
do not hide a duplicate Nami schema in that code or in emitter string literals.
Carry each authorized migration through the TypeSpec definition, emitter, structural mapping
and actual consumer. Keep observed implementation, intended design and unverified claims separate.

## Current audit and authorized capabilities (2026-10-27)

Audit source baseline: commit `e0dd040f0b9bf884de30e0206bfa49213a39efe7` plus the current
working tree. The audit inspected all 149 tracked/nonignored project files under `internal/`
and `src/`, including 110 C# files parsed with the pinned SDK Roslyn parser. It did not
establish that every runtime behavior is correct. Reports are under
`/Users/alexandernachtmann/RiderProjects/nami/artifacts/contract-audit-2026-10-27/`.
The reports describe the captured baseline, not later migrations.

The original 155-row `contracts/model-migration.csv` was a historical candidate inventory:
83 entries concerned `internal/` or `src/`. All 47 entries labelled generated had a generated
C# declaration, but 14 also had handwritten partial declarations. `OtlpAttributeValueKind`
was incorrectly still labelled retained, and 22 of the 69 model-manifest entries were absent.
The refreshed inventory retains historical `original_source`, adds current source locations
and audit status, and includes those 22 emitted types. `generated declaration` does not mean
all behavior or persistence for that type is generated. Entries outside the audited scope
remain explicitly unverified. Do not treat the inventory as proof of full migration.

Already implemented; do not repeat these as fresh migration tasks:

- Consumer-specific C# output profiles and shared property-semantics resolution.
- Type-identity traversal and namespace-aware naming support in the language emitters.
- Typed ingestion slots and explicit `values`/`renames` normalization provenance.
- Metric-shape variants and `LogTimeOrigin`; the handwritten ingestion partials expose derived views.
- `ActiveSignalStatus`/`SilentSignalStatus`, typed tool associations and generated field metadata.
- A revision hash restricted to `wire-manifest.json` roots, rather than the entire schema bundle.
- Generic .NET 11 `JsonSerializerOptions.GetTypeInfo<T>()` with generated JSON metadata.

Verified remaining gaps in the baseline:

- `contracts/models/storage-json.tsp` uses `@csharpType` to bind `StoredAttributeValue` to the
  handwritten hierarchy and converter in `src/nami.collector.storage/StoredAttributeValue.cs`.
- `IngestionStorageMapper.cs`, `MetricStorageMapper.cs` and `StorageAttributeProjection.cs`
  contain handwritten field mappings and data projections.
- Row provenance fields remain in handwritten partial records. Several option and helper
  data models remain handwritten; generated configuration keys are not generated option models.
- `storage-rows.tsp` contains authored SQL conflict clauses. The Roslyn schema emitter also
  hardcodes bootstrap tables/sequences, and store query/migration/retention SQL is handwritten.
- Contract support and some TypeScript input/result construction still contain authored shapes.
- Contract parity compares generated artifacts; it does not enforce absence of handwritten models
  and does not automatically refresh or validate `model-migration.csv`.

Current navigation comes from `contracts/generated/model-manifest.json`,
`contracts/generated/consumer-files.json`, `contracts/emit-config.tsp` and the actual consumers.
Core stored records are in `src/nami.collector.storage/Generated/StoredMetricRecords.g.cs`;
the generated `StoredRecordMapper` is in `Generated/AttributeIngestion.g.cs` in that project;
ingestion metric enums are in `src/nami.collector.ingestion/Generated/MetricShapes.g.cs`.

The user's explicit preview authorization includes two emitters and five libraries:

- `@typespec/protobuf`: Proto3 definitions, RPC services and streaming metadata.
- `@typespec/graphql`: GraphQL SDL for queries, mutations and subscriptions.
- `@typespec/streams`, `@typespec/sse`, `@typespec/events`: stream and event semantics and SSE binding.
- `@typespec/rest`: resource/operation modeling.
- `@typespec/versioning`: temporal schema views, including `getVersioningMutators` and the needed
  `@typespec/compiler/experimental` mutator APIs such as `unsafe_mutateSubgraphWithNamespace`.

The official TypeSpec navigation labels these packages preview as inspected on 2026-10-27:
https://typespec.io/docs/emitters/protobuf/guide/ and
https://typespec.io/docs/libraries/versioning/guide/ . Their functions must still be checked
against the installed declarations. At audit start none of those seven optional packages was
installed. The user subsequently requested their installation and automatic package maintenance.
They are now pinned in `contracts/package.json`, with required TypeSpec peer packages. The
machine-generated current version and API entry-point reference is
[references/typespec-packages.json](references/typespec-packages.json); read it before relying on
a dated version mentioned elsewhere in this skill. The active compiler at this update is `1.17.0`.
The separate compiler `1.18.0-dev.8` reference installation has `@typespec/versioning` `0.88.0-dev.2`.
Installing a package does not activate its emitter or change transport. `Stream<T>` does not
itself create an AsyncIterable/IAsyncEnumerable codec, framing, cancellation, backpressure or a
runtime. Temporal schema views do not infer a safe database data migration. Keep upstream OTLP
protobuf ownership intact.

## TypeSpec package maintenance

Use `/Users/alexandernachtmann/RiderProjects/nami/eng/typespec.sh --check` to query the configured
npm tag and compare exact pins and installed entry-point metadata. Use `--apply` for an authorized
update. `contracts/typespec-packages.json` defines the root packages and defaults to npm `latest`;
this can be a preview `0.x` release. Required TypeSpec peers are resolved and checked with them.
`--tag next` is an explicit channel change. `--check --offline` only reports cached knowledge.
Equivalent tasks: `mise run typespec:check` and `mise run typespec:update`.

The Python updater requests only one version document per package, uses up to six concurrent
requests and conditional HTTP caching when the registry provides validators. It checks peers
before mutation and validates a combined import of the installed TypeSpec libraries. It then
runs contract parity. Failed installation, import validation or changed generated outputs cause
package/lock/reference restoration and a frozen-lockfile reinstall. It does not silently migrate
changed model output. Run the repository's relevant build/tests after package changes.

The generated reference records actual entry paths and hashes; it is not a complete API catalog
or proof of runtime behavior. Compiler `1.17.0` advertises the missing secondary declaration
`./dist/module-resolver/index.d.ts`; that absence is recorded rather than inventing a working API.
Primary entry points must exist and the combined library import must compile. No scheduled or
build-triggered network update is installed.

## Verified encoded-name path (2026-10-27)

`ToolReadInput.maxBytes` is optional with default 32768, unlike a required-field example.
Its generated application TS property is `maxBytes`, C# is `MaxBytes`, JSON is `max_bytes`,
and normalized runtime schemas use `properties.max_bytes` without listing it in `required`.
A direct execution of generated `ToolReadInputJson` confirmed both directions and absence.
The raw artifact `contracts/json-schema/raw/nami-tools` still contains `maxBytes`; it is not
interchangeable with the normalized runtime bundle. The custom
`contracts/emitters/nami-lint/src/rules/wire-name-snake-case.ts` currently enforces snake_case.
Removing `@encodedName` alone will therefore not complete a camelCase migration; update that
project policy with the intended contract. This is not a TypeSpec or MCP requirement.
`@encodedName` selects by media type, not by recipient. Reuse one model when the wire form is
identical; define separate representations only when their data contract actually differs.
The engine's `input` member currently carries the tool payload; it does not rename its inner fields.

## Historical source basis and version context

The 2026-10-25 source package records branch `snapshot/1932-current-code`, commit
`9320d39bdfd3a2af0a41a4f5458e19866d5bcd67`, plus selected working-tree changes.
Line numbers below refer to that package. Earlier experiments retained from the supplied skill
were recorded on 2026-10-24 against `a2b49d05ecc80209faa8a0a858be4774651ce331`
and its working tree. Use each observation with its stated source/version scope.

- `/Users/alexandernachtmann/RiderProjects/nami/global.json`: SDK `11.0.100-rc.3.26522.148` (security servicing update, 2026-10-26).
- `/Users/alexandernachtmann/RiderProjects/nami/contracts/package.json`: active TypeSpec
  compiler/HTTP/OpenAPI/JSON-Schema `1.17.0`, TypeScript `7.1.0-beta`.
- `/Users/alexandernachtmann/RiderProjects/nami/src/nami.mcp/package.json`: the MCP package's TypeScript pin; its
  lockfile and installed packages resolve independently.
- `/Users/alexandernachtmann/RiderProjects/nami/contracts/node_modules/@typespec`: active package implementations and
  declarations.
- `/Users/alexandernachtmann/.local/share/nami/sources/typespec-preview-1.18.0-dev.8/node_modules`: separate preview
  reference; compiler `1.18.0-dev.8`, versioning `0.88.0-dev.2`, JSON-Schema `1.18.0-dev.1`.

These upstream sources are installed/cached release artifacts. A TypeSpec or TypeScript-Go Git
checkout path was not established by this context. Record an actual checkout and its HEAD when
one is used; keep that identity separate from the project's active package identity.

## Model ownership and consumer outputs

Use TypeSpec for Nami-owned data shapes and structural mappings across these consumers:

- Ingestion/domain: native collector values, resource/scope context, normalization provenance,
  metric variants and time origin.
- Storage JSON: the exact value and metric-payload representations stored in JSON columns.
- Storage rows: physical column shapes, nullability and DuckDB generation metadata.
- Tools: agent-facing inputs, projected results and structured errors.
- Engine: raw private envelopes and the typed commands/results produced from them.
- Configuration and evidence: fields, defaults and constraints for their actual consumers.

Share semantic scalars and field definitions where their meaning is identical. Give differing
field sets, tags, presence rules or identity roles named representations and explicit mappings.
Use the handwritten-model audit as a work inventory; classify object shapes by meaning rather
than creating one model for every object literal or dictionary expression.

Weaver owns SemConv keys, successors/deprecations and attribute persistence/redaction policy.
TypeSpec owns structural models and structural SQL metadata. Refer to generated catalog symbols
for hot-attribute mappings. Source anchors:

- `/Users/alexandernachtmann/RiderProjects/nami/registry/source/nami/persistence.yaml:94`
- `/Users/alexandernachtmann/RiderProjects/nami/src/nami.collector.storage/StorageAttributeProjection.cs:7`

Use the existing upstream OTLP protocol types at ingestion. OTLP/JSON has its own field names,
numeric enums, 64-bit number handling and unknown-field policy. Assign naming, unknown-member
handling and serialization contexts per boundary. Derive authenticated project selection from
the credential/project context rather than client tool arguments or ingestion hints.

REST, streams, SSE, Protobuf, GraphQL, XML, custom functions/decorators and Alloy-based emission
are available when a concrete consumer benefits. The private engine can use TypeSpec data shapes
with its existing transport. Represent simultaneous storage/API/domain forms through models and
output profiles; use versioning for temporal schema views.

## Emitter roots, identities and declaration forms

Resolve an output profile's source roots and needed type dependencies before emitting. Associate
the profile with its target project, files, namespace, declaration form and serialization context.
Emit TypeScript, JSON schemas or JSON metadata where that output has a consumer.

Within a compiler program/view, key traversal and deduplication by TypeSpec type identity.
For stable external names, include namespace and instantiated template arguments. Resolve final
language names after identity resolution. Keep view-local identities distinct when using clones.
Scope JSON contexts and wire-schema registries to the corresponding serialized roots.

Emit the declaration form required by the consumer: class, record, positional construction where
used, or `internal sealed partial record` for DuckDB rows. Preserve meaningful accessibility and
`with`/construction behavior through the generated form or the migrated call site.

Source anchors for replacing the original API-only assumptions:

- `/Users/alexandernachtmann/RiderProjects/nami/contracts/emitters/ts-types/src/index.ts:94`: enum/model/union
  registration; `definitionName` at line 146 and template names around line 260.
- `/Users/alexandernachtmann/RiderProjects/nami/contracts/emitters/csharp/src/emitter.ts:103`: registry/context output;
  `renderModel` at line 329.
- `/Users/alexandernachtmann/RiderProjects/nami/contracts/emit-config.tsp:4`: existing language namespace mappings.
- `/Users/alexandernachtmann/RiderProjects/nami/contracts/emitters/csharp/lib/main.tsp:7`: Nami emitter metadata
  declarations.

## Effective property representation

Resolve semantic type, code name, wire name, effective encoding, presence, nullability, defaults,
constraints, target-language type and any value codec together. Share this resolution across
language output, serialization and schemas. Apply property metadata with the appropriate scalar
fallback and carry element semantics through arrays, maps and nullable/union wrappers.

Use `property.name` for the application property and
`resolveEncodedName(program, property, "application/json")` for its JSON name. The active
signature includes `program`; it is declared in
`/Users/alexandernachtmann/RiderProjects/nami/contracts/node_modules/@typespec/compiler/dist/src/lib/encoded-names.d.ts:27`.
This supports TypeScript `maxBytes`, C# `MaxBytes` and JSON `max_bytes` from one property.
An interface alone has no runtime renaming effect.

Use `getEncode(program, target)` on the relevant property/scalar and interpret its encoded type
and encoding. Read the active declaration at
`/Users/alexandernachtmann/RiderProjects/nami/contracts/node_modules/@typespec/compiler/dist/src/lib/decorators.d.ts:178`.
Codecs implement the resolved representation, including value conversion when code and JSON
types differ. A naming-only codec is one supported case, not a permanent limit on generation.

Keep these property states distinct: required value, optional value, required nullable value,
and optional nullable value. Compose an outer presence representation with its actual inner
value codec. For `value?: AttributeInt64 | null`, preserve absent, explicit null and integer
text simultaneously. Write required nullable values even under a context-wide null-omitting policy.

Use C# `long`/`ulong` for the corresponding native values. Use exact decimal strings for full
64-bit JSON transport values, or an explicit TypeScript `bigint` application representation with
a generated codec. Use TypeScript `number` for ranges that fit its exact representation, such as
`maxBytes`. Carry numeric bounds as precise compiler values or decimal text through emission.
The active helpers `getMinValueAsNumeric` and `getMaxValueAsNumeric` are declared in
`/Users/alexandernachtmann/RiderProjects/nami/contracts/node_modules/@typespec/compiler/dist/src/core/intrinsic-type-state.d.ts`.

Model non-finite floating values, bytes, enum/discriminator values and nested collections according
to the selected representation. An explicit tagged union gives object variants a deterministic
selection rule. For an untagged union, resolve alternatives by their actual shape semantics.
Treat map keys as payload data. Represent fixed fields plus arbitrary entries deliberately, for
example through an explicit `values` member; retain both if supporting a mixed indexer model.

Relevant implementation anchors:

- `/Users/alexandernachtmann/RiderProjects/nami/contracts/emitters/ts-types/src/index.ts:233`: field types; scalar
  encoding at line 276.
- `/Users/alexandernachtmann/RiderProjects/nami/contracts/emitters/ts-types/src/serialization.ts:16`: property shapes
  and recursive conversion.
- `/Users/alexandernachtmann/RiderProjects/nami/contracts/emitters/csharp/src/emitter.ts:348`: property type/presence;
  numeric bounds at line 389; number handling at line 455.
- `/Users/alexandernachtmann/RiderProjects/nami/internal/nami.contracts/WireConverters.cs:75`: `OptionalValue<T>` and
  its inner serialization.

## Ingestion values and normalization provenance

Describe the semantic value family as Empty, String, Bool, Int64, Float64, Bytes, Array<Value>
and KeyValueList<Record<Value>>. Generate the representations and structural mappings needed by
the consumer. Use the native typed slots/factories where their collector behavior is useful.

Carry normalization provenance explicitly with normalized values. Collection inheritance is an
implementation choice; provenance must survive copying, projection and conversion. A suitable
target shape is `NormalizedAttributeSet<T> { values: Record<T>; renames: AttributeRename[]; }`.
`AttributeRename` contains required `originalKey: string` and `canonicalKey: string | null`.
The null canonical key records the existing deprecation case without a different successor key.
Exemplar and list projections carry their corresponding provenance explicitly as well.

Source anchors:

- `/Users/alexandernachtmann/RiderProjects/nami/src/nami.collector.ingestion/OtlpAttributeValue.cs:9`
- `/Users/alexandernachtmann/RiderProjects/nami/src/nami.collector.ingestion/OtlpIngestionModels.cs:36`
- `/Users/alexandernachtmann/RiderProjects/nami/src/nami.collector.ingestion/NormalizedAttributes.cs:9`
- `/Users/alexandernachtmann/RiderProjects/nami/src/nami.collector.storage/MetricStorageMapper.cs:47`

## Metric variants and log time origin

Express the valid metric-kind/payload relationships produced by the converter: Gauge and Sum
have Number data; Histogram, ExponentialHistogram and Summary have their corresponding payloads.
Associate temporality and monotonicity with the variants where they have meaning. Keep shared
resource, scope, attribute and timing fields reusable. Generate API forms from those meanings
without coupling the native ingestion representation to an API-specific serialization choice.

Five metric kinds map to four stored payload tags: Gauge and Sum both use `type: "number"`.
Keep metric kind and payload tag as separate information. Distinguish signed attribute integers,
unsigned nanoseconds/counts, floats and derived query values. `MetricPointRow.Value: double?`
is a query projection; the original observation has its own lossless representation.

For logs, retain producer event time, producer-provided observation time, collector receive time
and the ordinal within an otherwise untimed request. Derive effective presentation time and
the producer-observed-time flag from that information. Keep receive time and database arrival
sequence distinct from the identity used to recognize a re-received log.

Source anchors:

- `/Users/alexandernachtmann/RiderProjects/nami/src/nami.collector.ingestion/OtlpIngestionModels.cs:89`: log fields;
  metric fields at line 145.
- `/Users/alexandernachtmann/RiderProjects/nami/src/nami.collector.ingestion/Generated/MetricShapes.g.cs`
- `/Users/alexandernachtmann/RiderProjects/nami/src/nami.collector.ingestion/OtlpConverter.Metrics.cs:111`
- `/Users/alexandernachtmann/RiderProjects/nami/src/nami.collector.ingestion/OtlpConverter.cs:552`
- `/Users/alexandernachtmann/RiderProjects/nami/src/nami.collector.storage/IngestionStorageMapper.cs:117`
- `/Users/alexandernachtmann/RiderProjects/nami/src/nami.collector.storage/MetricStorageModels.cs:124`

## Stored JSON and identity algorithms

Give storage and API representations explicit names and generate their structural mappings.
The source storage representation uses bare strings/booleans, arrays and null, plus objects
tagged with `type: "int" | "double" | "bytes" | "kvlist"`. The API object variants use `kind`.
Transform the tag at the variant boundary and recursively transform values; keys inside
`kvlist.values` remain data even when named `type` or `kind`.

Keep `StoredAttribute.Value` required and nullable. A Number-point value can be absent when
the producer supplied neither an integer nor a double. Preserve those distinctions in schemas
and codecs alongside integer text, base64 bytes and the selected non-finite float representation.

Bind generated input shapes to named identity algorithms. Raw attribute identity uses its
canonical Protobuf basis. Instrument/series identity uses its selected semantic attributes.
Observation identity includes the observation representation; measurement identity normalizes
the intended mathematical equivalence, including appropriate integer/double equality and the
Gauge start-time rule. Log identity uses the original body even when the display body is shortened.

Keep key ordering, number formatting, escaping and field selection explicit where hashes consume
them. TypeSpec can describe identity inputs and roles; the algorithms implement mathematical,
temporal and canonicalization rules. Treat a changed hash basis as an intentional identity change
and carry its consequences through the affected storage/read paths.

Source anchors:

- `/Users/alexandernachtmann/RiderProjects/nami/src/nami.collector.storage/StoredAttributeValue.cs:27`
- `/Users/alexandernachtmann/RiderProjects/nami/src/nami.collector.storage/Generated/StoredMetricRecords.g.cs`: payload tags;
  optional number at line 23; stored attributes around line 140.
- `/Users/alexandernachtmann/RiderProjects/nami/src/nami.collector.storage/Generated/AttributeIngestion.g.cs`
- `/Users/alexandernachtmann/RiderProjects/nami/src/nami.collector.storage/MetricObservationCodec.cs:29`
- `/Users/alexandernachtmann/RiderProjects/nami/src/nami.collector.ingestion/OtlpConverter.MetricIdentity.cs:12`
- `/Users/alexandernachtmann/RiderProjects/nami/src/nami.collector.storage/MetricStorageMapper.cs:101`
- `/Users/alexandernachtmann/RiderProjects/nami/src/nami.collector.storage/IngestionStorageMapper.cs:198`
- `/Users/alexandernachtmann/RiderProjects/nami/src/nami/Tools/ReadTools.cs:308`

## DuckDB output through the existing row generator

Generate C# rows and DuckDB attributes from TypeSpec, then use the existing Roslyn generator for
DDL, binders and readers. Match its `internal sealed partial record` consumer shape and required
property accessors. Express table name, conflict behavior, indexes, Derived status, column order,
SQL type/nullability, primary-key ordinal, SQL default and insert exclusion as Nami-owned metadata.
Keep behavioral SQL/mapping decisions attached to the code or metadata that actually owns them.

The source generator treats all public getter properties as columns; column attributes supply
additional metadata. Keep internal provenance/helpers appropriately internal. Its column order
comes from source path and declaration position. Emit a deliberate property order; primary-key
ordinal orders the key, while `ParameterSets` names CLR properties in its own explicit order.

Resolve physical SQL types independently of JSON encoding. The current binder supports
`ulong` as `UBIGINT` and `long` as `BIGINT`. JSON columns use the binder's C# string representation
with `SqlType = "JSON"`. Give server-generated sequences their `DefaultSql`/`ExcludeFromInsert`
metadata. Resolve boolean/flag representation against the actual binder's supported cases.

Source anchors:

- `/Users/alexandernachtmann/RiderProjects/nami/src/nami.collector.storage/StorageRecords.cs:47`: row form; internal
  provenance at line 49; sequence at line 186.
- `/Users/alexandernachtmann/RiderProjects/nami/src/nami.collector.storage/MetricStorageModels.cs:40`
- `/Users/alexandernachtmann/RiderProjects/nami/internal/nami.collector.storage.generators/DuckDbAttributes.cs:36`
- `/Users/alexandernachtmann/RiderProjects/nami/internal/nami.collector.storage.generators/DuckDbInsertGenerator.cs:272`
- `/Users/alexandernachtmann/RiderProjects/nami/internal/nami.collector.storage.generators/DuckDbEmitter.cs:233`

## Tools, private engine and projected results

Generate the relationship ToolName -> Input -> SuccessResult/Error from the tool definitions.
Model raw transport input and a valid command as separate states. The raw tool-argument path
deliberately admits unknown data so invalid arguments can produce the structured ToolError.
After input interpretation, use the generated typed relationship. Give call, health and cancel
their required forms and correlate responses through the pending request's expected result.
Decoding a transport envelope and accepting a valid tool command are different operations.

Build results through generated shapes and derive field-selection metadata from their models.
Preserve real absence for unrequested fields. Keep grouping, paging, budgets, authorization and
cancellation in their owning runtime logic while supplying generated data forms to that logic.
Use generated defaults at their intended boundary; apply request defaults when interpreting the
request rather than erasing missingness during a generic structural conversion.

Encode producer-established invariants in the models. For `SignalStatus`, active requires
`lastSeen`; silent requires `silentFor` and optionally includes `lastSeen`. Named variants with
`@discriminated(#{ envelope: "none", discriminatorPropertyName: "kind" })` express this flat form.
Keep envelope choice, discriminator spelling and variant tags consistent across output profiles.

Source anchors:

- `/Users/alexandernachtmann/RiderProjects/nami/contracts/models/tools.tsp:20`: status; projections at line 52; limit
  default at line 64.
- `/Users/alexandernachtmann/RiderProjects/nami/contracts/models/engine.tsp:6`
- `/Users/alexandernachtmann/RiderProjects/nami/contracts/common/telemetry-query.tsp:43`
- `/Users/alexandernachtmann/RiderProjects/nami/src/nami.mcp/src/server.ts:58`
- `/Users/alexandernachtmann/RiderProjects/nami/src/nami.mcp/src/engine.ts:50`
- `/Users/alexandernachtmann/RiderProjects/nami/src/nami/Hosting/EngineProtocol.cs:99`
- `/Users/alexandernachtmann/RiderProjects/nami/src/nami/Tools/ReadTools.cs:24`: producer invariants; result
  construction at lines 97 and 328.
- `/Users/alexandernachtmann/RiderProjects/nami/src/nami/Tools/ToolDispatcher.cs:44`: defaults; envelope construction at
  line 185.

## Schemas, revision scope and temporal versions

Generate schema keys, required members, nullability, encoded values and discriminators from the
same resolved representation as the code. Select schema roots and their dependencies by consumer.
Compute a wire revision from the agreed wire roots; give purely internal rows their own scope.
Declare environment-variable mappings explicitly for configuration models, separately from JSON names.

The inspected JSON-Schema 1.17.0 and preview 1.18.0-dev.1 emitters used `prop.name` for required
names. Nami's OpenAPI-based normalization supplied its wire-schema semantics. Evolve the effective
schema pipeline with the representation rules above; a package upgrade alone does not establish
encoded-name support. Anchors:

-
`/Users/alexandernachtmann/RiderProjects/nami/contracts/node_modules/@typespec/json-schema/dist/src/json-schema-emitter.js:104`
- `/Users/alexandernachtmann/RiderProjects/nami/contracts/scripts/emit-schemas.mjs:6`: schema inputs; tool bundle around
  line 286.
- `/Users/alexandernachtmann/RiderProjects/nami/contracts/scripts/emit-contract-revision.mjs:6`

Select temporal schema views explicitly before emission. A schema package version or fingerprint
does not select a TypeSpec version. When versions are used, resolve one requested view centrally
and feed that same view to its language/schema consumers. Handle an unavailable selection explicitly.

The 2026-10-24 preview experiment used `getVersioningMutators(program, namespace)` and
`unsafe_mutateSubgraphWithNamespace(program, [mutator], namespace)`. The former returned either a
transient mutator or versioned snapshots; the latter returned a cloned Namespace view. A v1/v2
example with `@renamedFrom` and `@added` produced the expected differing fields while leaving the
source namespace intact. This is an observed API experiment, not evidence of Nami multi-version delivery.
An explicit `@encodedName` still controls JSON spelling when the source property is renamed.

Read the exact preview declarations at:

-
`/Users/alexandernachtmann/.local/share/nami/sources/typespec-preview-1.18.0-dev.8/node_modules/@typespec/compiler/dist/src/experimental/mutators.d.ts`
-
`/Users/alexandernachtmann/.local/share/nami/sources/typespec-preview-1.18.0-dev.8/node_modules/@typespec/versioning/dist/src/mutator.d.ts`
-
`/Users/alexandernachtmann/.local/share/nami/sources/typespec-preview-1.18.0-dev.8/node_modules/@typespec/versioning/dist/src/mutator.js`

## Preview APIs and retained implementation lessons

Use project/library-owned feature flags for the syntax actually selected. The inspected compiler
listed `declaration-expressions`, `union-extends`, `auto-decorators`, `function-declarations` and
`type-info-provider`; configuration accepts `kind: project` and `features`. Read
`/Users/alexandernachtmann/RiderProjects/nami/contracts/node_modules/@typespec/compiler/dist/src/core/features.js`
and
`/Users/alexandernachtmann/RiderProjects/nami/contracts/node_modules/@typespec/compiler/dist/src/config/config-schema.js`.
The recorded `union extends` experiment accepted a named model base after rejecting an inline base.
Union inheritance and discriminator serialization are independent choices.

Read C# 15 union-specific guidance in the bundled
[union skill](../csharp-union-types/SKILL.md). This checked-in copy is the source
for explicit exports; no external checkout is required. Use generated JSON metadata for AOT.
The earlier .NET RC2 runtime inspection found generic `JsonSerializerOptions.GetTypeInfo<T>()`
and `TryGetTypeInfo<T>(out JsonTypeInfo<T>?)`, although the targeting-pack XML omitted them.
Use the actual declarations/assembly when resolving that mismatch. Recorded sources:

- `/usr/local/share/dotnet/shared/Microsoft.NETCore.App/11.0.0-rc.2.26504.105/System.Text.Json.dll`
- `/usr/local/share/dotnet/packs/Microsoft.NETCore.App.Ref/11.0.0-rc.2.26504.105/ref/net11.0/System.Text.Json.xml`

The native TypeScript 7.1.0-beta package recorded gitHead
`3e9236361fa97a9e6c685f4833ac457ae2d8c50a`. Its exports include `typescript/sync`,
`typescript/async`, `typescript/fs`, `typescript/path`, `typescript/proto` and `typescript/ast/*`;
the older 7.0.2 `typescript/unstable/*` paths differ. The root export is version metadata.
Use its actual declarations for API work and its executable resolver for CLI work:

- `/Users/alexandernachtmann/RiderProjects/nami/contracts/node_modules/typescript/package.json`
- `/Users/alexandernachtmann/RiderProjects/nami/contracts/node_modules/typescript/lib/getExePath.js`
- `/Users/alexandernachtmann/RiderProjects/nami/contracts/node_modules/typescript/dist/api/sync/api.d.ts`
- `/Users/alexandernachtmann/RiderProjects/nami/contracts/node_modules/@typescript/typescript-darwin-arm64/lib/tsc`

The MCP package has its own corresponding installation. Use absolute command-output paths when
working across those package directories. `/Users/alexandernachtmann/RiderProjects/nami/eng/scripts/limits.sh`
sets the native compiler's `GOMAXPROCS` from `NAMI_BUILD_WORKERS` alongside `DOTNET_PROCESSOR_COUNT`.

## Existing tooling and recurring local failures

Use the repository's existing commands for the work at hand. Regeneration is available through
`/Users/alexandernachtmann/RiderProjects/nami/eng/build.sh Contracts`; package scripts define their
TypeScript commands. `mise run lint:tsp` builds the local TypeSpec linter and runs no-emit lint.
The naming/identity rules live at
`/Users/alexandernachtmann/RiderProjects/nami/contracts/emitters/nami-lint/src/index.ts`.
Scope boundary-specific naming rules when adding non-wire model families.

The MCP SDK's `fromJsonSchema` path consumes generated tool schemas. The recorded SDK 2.3.1
implementation and Node validator shim are at
`/Users/alexandernachtmann/RiderProjects/nami/src/nami.mcp/node_modules/@modelcontextprotocol/server/dist/index.cjs`
and
`/Users/alexandernachtmann/RiderProjects/nami/src/nami.mcp/node_modules/@modelcontextprotocol/server/dist/shimsNode.mjs`.
Its internal Zod dependency does not require a second hand-maintained Nami schema. Derive any
additional validator output from the same property semantics and its actual consumer.

Retain these observed troubleshooting lessons when their matching task arises:

- Installed `dist` sources were hidden by ignore rules. Search exact package paths with `rg --no-ignore` and bounded
  globs; exclude sourcemaps.
- Missing PyYAML caused the skill validator to fail twice. The local applied invocation used
  `uv run --offline --no-project --with PyYAML python /Users/alexandernachtmann/.codex/skills/.system/skill-creator/scripts/quick_validate.py /Users/alexandernachtmann/RiderProjects/nami/.agents/skills/nami-contract-models`.
- macOS Bash with `set -u` rejected an empty array expansion. The applied wrapper uses
  `${arguments[@]+"${arguments[@]}"}` in `/Users/alexandernachtmann/RiderProjects/nami/eng/inspect.sh`.
- The compile script cleans generated directories first. For exploratory compilation expected to fail, the existing
  inspection runner captures/restores outputs around that operation. Its implementation is
  `/Users/alexandernachtmann/RiderProjects/nami/eng/scripts/inspect_agent.py`; use per-file evidence when diagnosing
  generation drift.
- An ordinary mise execution once installed an unrelated missing tool.
  `/Users/alexandernachtmann/RiderProjects/nami/mise.toml` records the explicit-install configuration; resolve commands
  with the task's mise environment.

For MCP transport work, the recorded Inspector 2.11.0 JS entry is
`/Users/alexandernachtmann/.local/share/mise/installs/npm-modelcontextprotocol-inspector/2.11.0/node_modules/@modelcontextprotocol/inspector/clients/mcpdo/build/mcp-bin.js`.
`mise which mcpdo` resolved a shell shim; execute it as a command or resolve the JS entry for Node.
The working config used `type: "streamable-http"`; `http` was the CLI spelling and failed as a
config discriminator. `--config` and `--catalog` were mutually exclusive. The local integration
uses a private daemon, a generated read-only config, explicit connections, `--stored-auth-only`
and `--elicit off`; retain its in-memory credential handling. Read `pendingAuth` and negotiated
connection details when diagnosing readiness. The implementation is
`/Users/alexandernachtmann/RiderProjects/nami/eng/scripts/mcp_evidence.mjs`.

The recorded Inspector install exception applies specifically to `pino@9.14.0`. Its source-tag
comparison and registry-signature evidence are in
`/Users/alexandernachtmann/.local/share/nami/sources/pino-9.14.0/registry-metadata.json`,
`/Users/alexandernachtmann/.local/share/nami/sources/pino-9.14.0/signature-verification.json`,
`/Users/alexandernachtmann/.local/share/nami/sources/pino-9.14.0/source-comparison.json` and
`/Users/alexandernachtmann/.local/share/nami/sources/pino-9.14.0/upstream/package.json`.
The recorded tag commit is `339f1d6c899fa584324e15c587fbd811664dd07c`. Use those observations only
for that exception; they do not establish an absent publisher-provenance attestation.

For a task involving native codefixes or cleanup planning, read
`/Users/alexandernachtmann/RiderProjects/nami/contracts/agent-control/README.md` for the current
`mise agent -- codefixes`, `tsp-plan`, `plan` and `apply` commands and their ownership boundaries.
The recorded Lean policy is `/Users/alexandernachtmann/RiderProjects/nami/eng/agent-policy/Policy.lean`.
Its guard implications describe the transition policy; keep that scope distinct from application
semantics and the filesystem. Treat type-info output and plans according to their source identity.

## Learning loop

On the second occurrence of a mistake, update its existing case in this skill with the concrete
symptom, established cause, prerequisites, applied fix, source version and observed result.
Reference full local paths and relevant symbols. Record only experience from the actual work
context as an applied solution; keep proposals as target rules until the corresponding work exists. Finding outside proposal work needs the proposal reconsiderd as architecture evolves consider a new prposal. Update markdown files outside your work and notify user which discovery you found. As models have gotten smarter, improving agent architecture is becoming less about what you can add and more about what you can delete.
Replace obsolete assumptions as implementation evolves and remove resolved task instructions.
Maintain this same skill at
`/Users/alexandernachtmann/RiderProjects/nami/.agents/skills/nami-contract-models/SKILL.md`.

## Test harness integration (2026-10-25)

The canonical harness is `/Users/alexandernachtmann/RiderProjects/nami/tests`; use the inventory in `/Users/alexandernachtmann/RiderProjects/nami/docs/integration/test-harness-inventory.csv` when tracing a predecessor file. No Downloads checkout or old product SDK is needed. Product DTOs still come from TypeSpec; OTLP messages and test-only evidence records are not substitute application contracts.

Repeated CA2000 finding on .NET SDK 11.0.100-rc.2.26504.105: allocating an async-disposable `FixtureProject` inside a fluent `await using` expression obscured ownership. In `/Users/alexandernachtmann/RiderProjects/nami/tests/Nami.IntegrationTests/Projects/JsonReflectionGuardTests.cs`, declare `await using var project = new FixtureProject(...)` first and configure it afterward. For the long-lived MCP fixture, `/Users/alexandernachtmann/RiderProjects/nami/tests/Nami.Tests/Infrastructure/McpHarness.cs` assigns the `OwnedProcess` directly to the fixture and disposes it on failed startup as well as normal shutdown. The build validates the ownership; do not suppress CA2000.

JetBrains cleanup must use `--properties=Configuration=Release` with the pinned SDK when consuming the Release-prepared fixture graph. A default Debug cleanup could not resolve the generated `CompilerProbes.dll`; this is a configuration mismatch, not a missing source project.

The 2026-10-25 Debug cleanup also removed `using Nami.Collector.Storage` from `/Users/alexandernachtmann/RiderProjects/nami/src/nami/Hosting/CollectorStorageOptions.cs` because the generated `StorageDefaults` symbol was unavailable in that analysis. The Release build caught CS0103; restoring the namespace import is required. Do not accept unused-import fixes for unresolved generated symbols, and compile after cleanup.

The same incomplete Debug cleanup removed the namespace import for generated `CompilerProbes` references in `/Users/alexandernachtmann/RiderProjects/nami/tests/Nami.IntegrationTests/Compilation/InProcessCompilerTests.cs`. The probe namespace is now `Nami.IntegrationTests.CompilerProbes`; keep the import and prepare Release references before any automated cleanup.

Shared Capture sources compile twice: inside `Nami.Tests`, which has repository global usings, and inside a materialized consumer with only SDK defaults. Cleanup removed `System.Diagnostics` imports as redundant in the first context and broke all three consumer tests in the second. `/Users/alexandernachtmann/RiderProjects/nami/tests/Nami.Tests/Capture/ActivitySnapshot.cs`, `ActivitySnapshotExporter.cs` and `CapturedActivityAssertions.cs` now use explicit type aliases for their Diagnostics types. Preserve these bindings when editing shared sources; `CaptureFixtureTests` must build both managed and NativeAOT consumers.

## RC3 security servicing pin (2026-10-26)

The active repository SDK is `11.0.100-rc.3.26522.148`, selected by both `/Users/alexandernachtmann/RiderProjects/nami/global.json` and `/Users/alexandernachtmann/RiderProjects/nami/mise.toml`. The container SDK/runtime-deps tags in `/Users/alexandernachtmann/RiderProjects/nami/src/nami/Dockerfile` use RC3 as well. The .NET 10 servicing package line retained by the build/runtime package graph is `10.0.14` in `/Users/alexandernachtmann/RiderProjects/nami/Version.props`. Historical RC2 API observations above retain their original version context.

Microsoft release metadata identifies the 2026-10-25 RC3 release as a security update: https://builds.dotnet.microsoft.com/dotnet/release-metadata/11.0/releases.json . Self-contained/NativeAOT artifacts must be rebuilt; selecting a new SDK alone does not update an existing published binary.

## Union completeness in the production build (2026-10-26, RC3)

`/Users/alexandernachtmann/RiderProjects/nami/tests/Nami.IntegrationTests/UnionCompilerTests.cs`
now compiles copies of the actual generated source and consumer files using the pinned SDK,
not the separately packaged Roslyn compiler. Adding a wire case or hierarchy leaf must produce
CS8509 in the unchanged consumer. The original synthetic Roslyn test did not establish this.

On SDK `11.0.100-rc.3.26522.148`, copying the repository `.editorconfig` into these generated-source
projects left CS8509 as a warning, even with `dotnet_diagnostic.CS8509.severity = error`.
The observed fix is the explicit CS8509/CS8655/CS8846 `WarningsAsErrors` entries in
`/Users/alexandernachtmann/RiderProjects/nami/Directory.Build.props`. The tests consume that
production property while keeping `TreatWarningsAsErrors=false`; do not replace it with a
blanket test-only warning promotion. The twelve baseline and added-case compiler checks passed.

`/Users/alexandernachtmann/RiderProjects/nami/contracts/emitters/csharp/src/emitter.ts`
selects typed JSON metadata with an exhaustive switch on each readonly union itself. Its
explicit null arm rejects uninitialized contents. The object-valued `Value` is passed to the
metadata-based serializer only after that selection. Ingestion and storage hierarchy forms are
owned by `contracts/models/ingestion-metrics.tsp` and `contracts/models/storage-json.tsp`;
closed intermediate classes preserve leaf completeness through multi-level inheritance.

The NativeAOT fixture `Publish_ActualUnionWriters_RoundTripsEveryCaseUnderNativeAot` subsequently
published and executed the generated sources: 15 exact JSON roundtrips, rejection of all five
uninitialized union values, rejection of ten missing/unknown discriminator inputs, and reflection
disabled. `EngineCall` requires explicit nullable credential/traceparent/tracestate fields; omitting
them is an invalid fixture payload, not a reason to weaken the contract.
