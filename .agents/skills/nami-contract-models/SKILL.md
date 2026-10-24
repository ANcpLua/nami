---
name: nami-contract-models
description: Evolve Nami TypeSpec models, language emitters, JSON schemas and serialization using locally verified compiler sources. Use for encoded names, required/optional fields, discriminated unions, schema-version views and TypeScript/C# model parity. Also records recurring generator and preview-API failures.
---

# Nami model generation

## Scope and authorization

The user explicitly permits preview, beta, RC, dev, nightly and experimental features,
including .NET 11 RC2/C# 15, TypeScript 7.1 Beta and TypeSpec compiler/mutator APIs.
Choose them for a concrete architectural benefit; stability labels alone are not a reason
to refuse them or ask again. This permission does not mean a permitted version is installed.

Work locally. Read this skill and the relevant fixed source paths below at the beginning
of each model/compiler work segment. Confirm their recorded release/commit before relying
on implementation details. Rediscover paths only when a recorded source is missing or has
changed, then update this same source table. Do not repeat a broad home-directory search.

TypeSpec owns data shapes for both boundaries:

- `/Users/alexandernachtmann/RiderProjects/nami/contracts/models/tools.tsp`: agent ↔ MCP.
- `/Users/alexandernachtmann/RiderProjects/nami/contracts/models/engine.tsp`: TypeScript ↔ C#.

Wire, DTO, domain and persistence projections may differ for an actual consumer or storage
compatibility requirement. Derive differences from the shared model/semantic policy; do not
maintain parallel field-name or schema tables. Weaver remains the semantic/persistence authority.
Preserve authenticated project isolation, exact integer strings, discriminator values,
absent/null distinctions and persisted identities. Generate only artifacts with a consumer.
REST, events, SSE, streams, XML, Protobuf, GraphQL, custom decorators/functions and Alloy-based
emission remain available choices when they fit that consumer. TypeSpec use does not require
introducing an HTTP boundary into the private MCP/engine connection.

## Fixed local sources and observed versions

Baseline recorded on 2026-10-24; Nami source commit
`a2b49d05ecc80209faa8a0a858be4774651ce331`. Working-tree changes must be considered separately.

| Absolute path | Source state and relevant content |
| --- | --- |
| `/Users/alexandernachtmann/RiderProjects/nami/global.json` | SDK `11.0.100-rc.2.26504.105`; inspect SDK and MSBuild pins here. |
| `/Users/alexandernachtmann/RiderProjects/nami/contracts/package.json` | Active TypeSpec compiler/HTTP/OpenAPI/JSON-Schema `1.17.0`, TypeScript `7.0.2`; the permission to use newer releases does not change these pins. |
| `/Users/alexandernachtmann/RiderProjects/nami/contracts/node_modules/@typespec/compiler/dist/src/lib/encoded-names.d.ts` | Compiler `1.17.0`, `resolveEncodedName`, lines 31–33. The installed signature includes `program`. |
| `/Users/alexandernachtmann/RiderProjects/nami/contracts/node_modules/@typespec/compiler/dist/src/core/features.js` | Compiler `1.17.0`, `compilerFeatures` and `isCompilerFeatureEnabled`; feature ownership follows the declaring project/library. |
| `/Users/alexandernachtmann/RiderProjects/nami/contracts/node_modules/@typespec/compiler/dist/src/config/config-schema.js` | Compiler `1.17.0`, `kind`/`features` configuration keys. |
| `/Users/alexandernachtmann/RiderProjects/nami/contracts/node_modules/@typespec/json-schema/dist/src/json-schema-emitter.js` | JSON-Schema `1.17.0`, `modelDeclaration`, `modelPropertyLiteral`; required properties use `prop.name` (lines 104, 129). |
| `/Users/alexandernachtmann/RiderProjects/nami/contracts/node_modules/@typespec/openapi3/dist/src/openapi.js` | OpenAPI3 `1.17.0`, `getOpenAPI3`, `getOpenAPI`, `getVersionSnapshotDocument` (around lines 338–400); this emitter explicitly applies version mutators. |
| `/Users/alexandernachtmann/.local/share/nami/sources/typespec-preview-1.18.0-dev.8/node_modules/@typespec/compiler/dist/src/experimental/mutators.d.ts` | Locally cached compiler `1.18.0-dev.8`; `mutateSubgraphWithNamespace`, re-exported as `unsafe_mutateSubgraphWithNamespace`. |
| `/Users/alexandernachtmann/.local/share/nami/sources/typespec-preview-1.18.0-dev.8/node_modules/@typespec/versioning/dist/src/mutator.d.ts` | Locally cached versioning `0.88.0-dev.2`; `VersionedMutators`, `TransientVersioningMutator`, `getVersioningMutators`. |
| `/Users/alexandernachtmann/.local/share/nami/sources/typespec-preview-1.18.0-dev.8/node_modules/@typespec/versioning/dist/src/mutator.js` | Same release, `createVersionMutator`: rename, availability, property type and optionality are applied to clones. |
| `/Users/alexandernachtmann/.local/share/nami/sources/typespec-preview-1.18.0-dev.8/node_modules/@typespec/json-schema/dist/src/json-schema-emitter.js` | JSON-Schema `1.18.0-dev.1`; the inspected preview still builds required names from `prop.name`; upgrading alone does not fix the encoded-name mismatch. |
| `/Users/alexandernachtmann/RiderProjects/nami/contracts/node_modules/typescript/package.json` | Native compiler package `7.0.2`, gitHead `2bd066d87f5bafd315be9f40889d0a60b9e58e0b`; actual installed version, not 7.1 Beta. |
| `/Users/alexandernachtmann/RiderProjects/nami/contracts/node_modules/typescript/lib/getExePath.js` | Same commit, `getExePath`; resolves the platform native binary. A source checkout instead uses `built/local/tsgo`. |
| `/Users/alexandernachtmann/RiderProjects/nami/contracts/node_modules/typescript/dist/api/sync/api.d.ts` | Same commit, installed unstable synchronous compiler API. Read its actual declarations before using it; do not assume the old JS compiler API. |
| `/Users/alexandernachtmann/RiderProjects/nami/contracts/emitters/ts-types/src/index.ts` | Nami baseline, `renderInterface` and both model/JSON outputs. |
| `/Users/alexandernachtmann/RiderProjects/nami/contracts/emitters/ts-types/src/serialization.ts` | Nami baseline, `renderSerialization`; recursive generated codecs. |
| `/Users/alexandernachtmann/RiderProjects/nami/contracts/emitters/csharp/src/emitter.ts` | Nami baseline, property emission around lines 340–362 and typed JSON metadata generation around line 124. |
| `/Users/alexandernachtmann/RiderProjects/nami/src/nami.mcp/src/engine.ts` | Nami baseline, `EngineRequestJson.toJson` and response codec `fromJson`; actual codec consumers. |
| `/usr/local/share/dotnet/shared/Microsoft.NETCore.App/11.0.0-rc.2.26504.105/System.Text.Json.dll` | Loaded .NET 11 RC2 assembly; runtime metadata confirms generic `GetTypeInfo<T>` and `TryGetTypeInfo<T>`. |
| `/usr/local/share/dotnet/packs/Microsoft.NETCore.App.Ref/11.0.0-rc.2.26504.105/ref/net11.0/System.Text.Json.xml` | Same targeting-pack release; observed XML lists non-generic metadata methods but omits the generic methods. XML absence is not proof of API absence. |
| `/Users/alexandernachtmann/RiderProjects/csharp-union-types/SKILL.md` | Separate skill repository HEAD `8cc7b770168a484780080e00b284dcdfa287291c`, with additional pre-existing working changes; use for C# 15 union semantics. |

The package snapshots are release artifacts, not Git clones. No TypeSpec/TypeScript-Go
upstream clone or TypeScript 7.1 Beta was found in the inspected local locations. The user
has been asked for the clone paths. Do not invent a clone path or substitute a web source.
When supplied, replace this missing-source note with the exact checkout HEAD and relevant
source files; retain active project package versions separately.

## Applied fix: code names are not JSON names

**Recurring symptom:** TypeScript model properties unexpectedly become `max_bytes`, or
`JSON.stringify({ maxBytes: 1024 })` is assumed to send `max_bytes`. This confusion occurred
in the Nami migration and subsequent correction. An interface cannot rename runtime keys.

**Applied solution:** retain `property.name` for the model, resolve the encoded name for the
wire model/schema, and generate the conversion. In the installed compiler:

```ts
resolveEncodedName(program: Program, target: Type & { name: string }, mimeType: string): string;
const codeName = property.name;
const jsonName = resolveEncodedName(program, property, "application/json");
```

The same TypeSpec property produces TypeScript `maxBytes`, C# `MaxBytes` with
`[JsonPropertyName("max_bytes")]`, and JSON-Schema `properties.max_bytes`. Its optionality,
not the spelling, decides whether the JSON property belongs in `required`.

The implemented Nami codec gives this observed round trip:

```ts
const model = { maxBytes: 1024, snapshotId: "snapshot" };
const json = ToolReadInputJson.toJson(model);
// { max_bytes: 1024, snapshot_id: "snapshot" }
const restored = ToolReadInputJson.fromJson(json);
// { maxBytes: 1024, snapshotId: "snapshot" }
```

`api.ts`, `json.ts`, `serialization.ts` and schemas are generated. Edit their emitters and
TypeSpec sources, never the generated files. Validate untrusted JSON before decoding;
codecs perform naming conversion, not input validation. Keep direct JSON passthrough when
there is no application-model consumer. Nami's private envelope uses the codecs; MCP tool
payload passthrough deliberately uses wire types.

The official JSON-Schema declaration output did not provide the required wire names in
either inspected release. Nami therefore normalizes the OpenAPI wire projection in
`/Users/alexandernachtmann/RiderProjects/nami/contracts/scripts/emit-schemas.mjs`. Do not delete
that stage merely because `@encodedName` exists. Replacing it requires actual output parity.

## Verified experiment: select a version view before emitting

**Recurring misconception:** `@versioned` automatically projects every emitter, or a package
version/fingerprint selects a model version. Revisited during the migration and naming fix.

The locally executed preview recipe is:

```ts
import type { Namespace, Program } from "@typespec/compiler";
import { getVersioningMutators } from "@typespec/versioning";
import { unsafe_mutateSubgraphWithNamespace } from "@typespec/compiler/experimental";

function selectView(program: Program, source: Namespace, requestedVersion?: string): Namespace {
  const plans = getVersioningMutators(program, source);
  if (!plans) {
    if (requestedVersion !== undefined) throw new Error("The schema has no versioned view");
    return source;
  }
  if (plans.kind === "transient" && requestedVersion !== undefined)
    throw new Error("The root has no declared version");
  const recipe = plans.kind === "transient" ? plans.mutator
    : plans.snapshots.find(x => x.version.value === requestedVersion)?.mutator;
  if (!recipe) throw new Error("Requested schema version is unavailable");
  const view = unsafe_mutateSubgraphWithNamespace(program, [recipe], source);
  if (view.type.kind !== "Namespace") throw new Error("Expected a namespace view");
  return view.type;
}
// Resolve once and pass the returned namespace to all consumers of that view.
```

Executed source and observations:

```typespec
import "@typespec/versioning";
using TypeSpec.Versioning;
@versioned(Versions) namespace ModelProbe;
enum Versions { v1, v2 }
model ReadLimits {
  @renamedFrom(Versions.v2, "max_bytes") maxBytes: int32;
  @added(Versions.v2) traceLabel?: string;
}
```

With the versioning library imported, v1 contained required `max_bytes`; v2 contained
required `maxBytes` and optional `traceLabel`. The original namespace still contained
`maxBytes` and `traceLabel` after both mutations. `@encodedName("application/json",
"max_bytes")`, if fixed on that property, continues to select that JSON spelling; renaming
the source property alone does not override the encoding decorator.

This proves the local mutation API, not Nami multi-version delivery. Nami currently has one
unversioned schema view. When adding versions, resolve one explicit selection centrally and
feed every language/schema projection that view; do not independently select "latest" in each
emitter, mutate the original namespace, or silently fall back to another version.

## Feature flags and .NET metadata

The inspected TypeSpec compiler supports `declaration-expressions`, `union-extends`,
`auto-decorators`, `function-declarations` and `type-info-provider`. For project-owned syntax:

```yaml
kind: project
features:
  - declaration-expressions
  - union-extends
```

Enable only flags needed by the chosen implementation. A library's feature syntax is
controlled by that library's own configuration. The earlier inline-base `union extends`
attempt was rejected; the accepted form uses a named model base. Do not respond by dropping
the required common shape, or by claiming this flag changes discriminator serialization.

Runtime reflection on the pinned .NET assembly confirmed these signatures:

```csharp
JsonTypeInfo<T> JsonSerializerOptions.GetTypeInfo<T>();
bool JsonSerializerOptions.TryGetTypeInfo<T>(out JsonTypeInfo<T>? typeInfo);
```

Nami uses `GeneratedContractJsonContext.Default.Options.GetTypeInfo<T>()`. Use generated
metadata for NativeAOT, not a new reflection resolver. The local XML reference did not list
these generic members; inspect actual assembly metadata or compile a narrow example before
declaring them unavailable. Their absence from that XML is observed; its cause is not established.

## Validation and learning loop

Use `/Users/alexandernachtmann/RiderProjects/nami/eng/build.sh Contracts` for regeneration,
the package's `check` script for TypeScript, and the repository's Release gate before a push.
Follow the current `/Users/alexandernachtmann/RiderProjects/nami/AGENTS.md` for inspection,
locking and test entry points. No new tests were authorized in the current cleanup task;
the documented API examples were executed locally without adding test-suite cases.

The system Python twice failed to run the skill validator with
`ModuleNotFoundError: No module named 'yaml'`. The applied local fix uses the cached PyYAML
environment; changing the validator or downloading another package is unnecessary here:

```bash
uv run --offline --no-project --with PyYAML python \
  /Users/alexandernachtmann/.codex/skills/.system/skill-creator/scripts/quick_validate.py \
  /Users/alexandernachtmann/RiderProjects/nami/.agents/skills/nami-contract-models
```

On the second occurrence of the same mistake, update the matching case above with the
symptom, cause if established, prerequisites, applied fix, source version and observable
result. Preserve the intended semantics while iterating. Do not record a speculative workaround
as a proven solution or create another contradictory checklist for the same case.
