# Nami contracts

## Package maintenance

`../eng/typespec.sh --check` checks the npm tag configured in `typespec-packages.json`
(currently `latest`, the stable channel; pass `--tag next` to look at dev builds). `../eng/typespec.sh --apply` installs exact versions and refreshes
the package entry-point reference in
`../.agents/skills/nami-contract-models/references/typespec-packages.json`.
The equivalent repository tasks are `mise run typespec:check` and `mise run typespec:update`.

The maintained set includes the compiler/core emitters and the optional Protobuf,
GraphQL, Streams, SSE, Events, REST and Versioning packages. Required TypeSpec peers
are resolved with them, and their version constraints must agree before installation.
Installing a package does not activate its emitter or change Nami's transport.
Preview package status is separate from the npm tag: `latest` can point to a `0.x`
preview package. Pass `--tag <name>` to check another channel for one run.

The updater fetches one small version document per package, with at most six concurrent
requests, conditional ETag/Last-Modified requests when supported, and one cache entry per
package/tag under `artifacts/typespec/metadata`. `--check --offline` uses that cache and
explicitly reports cached status; it does not establish current online releases.
No background updater or network check is added to ordinary builds.

An update validates the installed package identities, imports all available TypeSpec
entry points together, and runs the repository's contract-parity check. A failure restores
the prior package file, lockfile, reference and installed dependency graph. A release that
changes generated artifacts therefore requires an explicit emitter/contract migration;
the updater does not silently accept changed product contracts. Run the normal repository
build/test gate after an accepted update. The generated reference lists entry points and
their hashes, not a claim that every API is implemented or exercised. Missing secondary
export declarations are recorded explicitly; missing primary entry points fail validation.

## Contract lint

`bun run lint` (`mise run lint:tsp`) is the gate. It builds the emitters and the linter and compiles
`main.tsp` without emitting and with warnings as errors:

```sh
tsp compile main.tsp --no-emit --warn-as-error
```

The ruleset is in `tspconfig.yaml`: `@typespec/http/all`, the compiler's `unused-using` and
`unused-template-parameter`, and `@ancplua/typespec-nami-lint/all` from `emitters/nami-lint`.
That library's rules, each with the reason it exists at the top of its file in `src/rules/`:

| Rule | Reports |
|---|---|
| `wire-name-snake-case`, `http-param-snake-case` | a JSON key or a query/path parameter that is not snake_case |
| `identity-binding`, `identity-edge-typed`, `confusable-identity-documented` | an identity under a second name, type or key |
| `unreachable-declaration` | a declaration that nothing in the contract uses and from which no emitter generates a type; in practice an unbranded scalar |
| `duplicate-model-shape` | two independent models with the same properties |
| `duplicate-member-value` | an enum value or a union variant listed twice |
| `duplicate-wire-name` | a property that takes the JSON key of an inherited property |
| `wire-root-twin` | a flat model and the discriminated union of the same message, both wire roots |
| `storage-column-order` | column orders of a table that are not 0 to n-1 (code fix: renumber) |
| `storage-index-redundant` | an index declared twice, or one that is a leading prefix of another or of the primary key |
| `storage-index-composite` | a non-unique multi-column index, which DuckDB documents as not eligible for index scans |
| `storage-index-budget` | more secondary indexes on a table than `STORAGE_INDEX_BUDGET` in `src/policy.ts` |

Policy (namespaces, root decorators, the index budget) is in `emitters/nami-lint/src/policy.ts`.
The rules read the emitter decorators from the type graph by name and import no emitter code.
Every rule reports the same diagnostics in the same order for the same sources.

A finding is fixed or carried. To carry one, put `#suppress "<rule>" "<cleanup-ledger id or reason>"`
on the declaration.

For a focused run, TypeSpec 1.18.0 can override the ruleset for one compilation. The gate itself
never does:

```sh
tsp compile main.tsp --no-emit --linter-ruleset @ancplua/typespec-nami-lint/storage
tsp compile main.tsp --no-emit --linter-rule '@ancplua/typespec-nami-lint/storage-index-budget={"maxIndexes":12}'
tsp compile main.tsp --no-emit --linter-rule '@ancplua/typespec-nami-lint/unreachable-declaration={"generatedTypes":"report"}'
```

The last one is an audit. A model, enum or union of a namespace mapped with `@csharpNamespace` gets a
generated C# and TypeScript type that hand-written code may use, which the TypeSpec program cannot see,
so the gate counts it as reached. The audit lists the ones no tool, wire root, route, storage row or
projection reaches; look for a hand-written use of each before removing it.

`bun run test:lint` runs the rule tests in `emitters/nami-lint/test` (also part of `bun run build`).

## Contract generation

The TypeSpec sources define Nami's six read-only tool inputs and results, the health report, the private engine
protocol,
and the OpenTelemetry projections used by ingestion and storage. No REST analysis route or
write-tool contract is emitted. The only HTTP route described here is `/health`.

Run from this directory:

```sh
bun install
bun run build
```

The build validates the existing generator helper tests, builds the local C# and TypeScript emitters and linter,
compiles TypeSpec with warnings as errors, and emits these committed artifacts:

- `../internal/nami.contracts/Nami/`: C# models, source-generated serializers, portable validation
  schemas, and the semantic contract revision.
- `../src/nami.mcp/src/contracts/generated/`: TypeScript model types (`api.ts`), JSON types (`json.ts`) and generated
  naming conversions (`serialization.ts`), the normalized schemas consumed
  by MCP, tool catalog, shared protocol limits, configuration names and contract revision.
- `json-schema/<tool>.input.json` and `<tool>.result.json`: the twelve self-contained wire schemas
  consumed by the MCP host and by downstream type generators.
- `json-schema/raw/nami-tools`: the official `@typespec/json-schema` declaration bundle.
- `openapi/nami.openapi.json`: the common TypeSpec program's wire projection and `/health` contract.

The official JSON Schema emitter currently uses source member names and does not apply
`@encodedName("application/json", ...)` or wire integer encodings. `emit-schemas.mjs` checks that
all twelve tool declarations are present in that bundle, then produces the runtime schemas from
the OpenAPI emitter's wire representation. The normalizer preserves recursive `$defs`, flattens
inheritance, seals objects, and retains integer precision and every supported validation bound.
The C# validator and MCP host consume those same normalized schemas.

Model properties keep their TypeSpec names in TypeScript and PascalCase in C#. `@encodedName`
owns the JSON spelling. For example, `maxBytes` becomes `MaxBytes` in C# and `max_bytes` in JSON.
The generated `ToolReadInputJson.toJson`/`fromJson` convert those representations, including nested
models, arrays, maps and discriminated unions. They do not validate untrusted input; the generated
JSON Schema does that before decoding. Direct JSON passthrough in MCP uses the JSON types, so it
does not decode and re-encode data needlessly. The private engine envelope uses the generated codec.

The present schema has one unversioned view and a content-derived revision, not an invented v1/v2
history. Adding versioned views requires resolving the selected `getVersioningMutators` snapshot
consistently for every emitter; `@versioned` alone does not add that behavior to the JSON-Schema
emitter. Do not confuse a package version or the wire fingerprint with a schema-version selector.

`models/engine.tsp` owns private request/result shapes and stream limits.
`models/tools.tsp` owns the tool shapes. `common/telemetry-query.tsp` owns `Freshness`, shared
read arguments and snapshot/cursor continuation; `common/errors.tsp` owns `ToolError`. `fields` can omit unrequested
fields from returned items, so results use explicitly generated `Projected*` model types. The full
non-projected models remain available for constructing complete results.

Generated files are never edited directly. The contract revision hashes the canonical wire schema:
documentation-only edits do not change it. Repeating the build without source changes must leave
all generated files byte-identical.

The C# emitter uses the compiler-supported C# 15 `union` declaration for tagged unions and
`closed class` for mixed primitive/object attribute values. Consumers get compile-time exhaustive
switch analysis. Native union JSON uses explicit converters: wire discriminator names remain those
from TypeSpec (`kind` for the Nami projections), with no implicit envelope. Empty
native unions are rejected during serialization. Native OTLP JSON/protobuf keeps its producer protocol; only the Nami
model projection
uses `kind`. Persisted storage keeps its separate internal representation.

Integer validation examines the original JSON number token, including its exponent and decimal
scale, before bounds checks. A fraction cannot be accepted merely because a binary64 conversion
rounded it to an integer.
