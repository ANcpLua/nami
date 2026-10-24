# Nami contracts

The TypeSpec sources define Nami's six read-only tool inputs and results, the health report, the private engine protocol,
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
- `../src/nami.mcp/src/contracts/generated/`: TypeScript model types (`api.ts`), JSON types
  (`json.ts`) and generated naming conversions (`serialization.ts`), the normalized schemas consumed
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
