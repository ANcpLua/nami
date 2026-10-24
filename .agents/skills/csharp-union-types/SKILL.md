---
name: csharp-union-types
description: "Design, review and implement C# 15 unions and closed hierarchies in .NET 11 code. Use for union declarations, custom [Union] types, exhaustive matching, default/null handling and union serialization. Do not upgrade earlier frameworks or replace working inheritance merely because unions exist."
---

# C# 15 unions for AI coding agents

Use unions for a finite set of alternatives whose types need not share inheritance. Use a `closed` class or record when existing alternatives share behavior or polymorphic serialization. Preserve open inheritance when callers must extend it.

## Agent execution

- Apply the latest user request and the consuming repository's instructions. Inspect existing edits before changing code; do not overwrite unrelated work or rewrite commit authorship.
- Read only the local reference sections needed for the task. Adapt the checked examples to the project's actual types, contracts and target framework.
- Make the smallest complete change. Verify changed behavior; do not rerun unchanged probes merely to repeat an explanation. Existing reports describe their recorded run, not later untested edits.
- Commit and push when authorized. Do not watch CI or wait for publication unless requested.
- Report the concrete improvement, relevant verification and delivery status briefly. Distinguish completed work from triggered CI or publication.

## Use the bundled knowledge

The skill is self-contained. For ordinary use, read only the needed local reference sections; do not fetch documentation, another repository or run all probes just to answer a covered question. The pinned compiler output and executed local examples supply the evidence. Run relevant checks when changing code or validating a version-specific claim.

## Establish the compiler and contract

1. Inspect the owning project, `global.json`, inherited build properties and installed SDK. Do not change framework or language version without an upgrade request.
2. On verified SDK `11.0.100-rc.2.26504.105`, unions compile with `LangVersion=15.0` or the `net11.0` default. Earlier previews required `preview`; do not apply that requirement to every .NET 11 SDK. Existing `preview` settings can remain.
   Distinguish the SDK compiler, the active language version and any separately referenced `Microsoft.CodeAnalysis.CSharp` package. An older Roslyn package or IDE language service can lack `LanguageVersion.CSharp15` even when the SDK builds C# 15 successfully. Use `#error version` temporarily to identify the compiler/language used by a build; inspect package versions separately for in-process compilation.
3. Identify the authoritative model and serializer. For TypeSpec or another generator, edit that source and regenerate. Never edit generated C# by hand. Separate wire and persistence models only for an explicit compatibility or storage requirement.
4. Before changing serialization, record the discriminator, null representation, numeric encoding and canonical ordering. Preserve persisted bytes and hashes unless a migration is intended.

## Implementation rules

- Switch on the union itself, not its object-valued `Value`. Prefer a **switch expression** covering every case and explicit `null => throw ...` when default values can arrive. A catch-all `_` or `var` masks future cases. An external JSON discriminator is an open input and still needs unknown-value rejection.
- Completeness diagnostics are warnings. Verify that `CS8509`, `CS8655` and `CS8846` fail the actual project build when completeness is required. A Roslyn test forcing warnings to errors does not establish production policy.
- Switch **statements** lack that completeness guarantee. Removing their default arm can silently omit work, including serialization.
- `default(U)` can contain null even when all cases are non-nullable. `U?` additionally has a nullable wrapper. Do not silently map an invalid default to a normal business state.
- Typed patterns match contents. Untyped property/positional patterns and `var` match the wrapper. `u is { }` is not a contents-presence check on a struct union; use `u is not null`. On RC2, `u is U` is not a valid general check.
- A declared union is a struct with object-backed storage and boxes value-type cases. Choose custom `[Union]` storage only for a concrete requirement. `HasValue`/`TryGetValue` enable non-boxing matching but do not eliminate allocations elsewhere. Measure performance claims.
- Use `readonly union` for immutable wrappers. Record-class cases still allocate; changing those cases to record structs merely moves their allocation to boxing in generated object storage. Custom nullable scalar cases must preserve zero/false as present values and map null/default consistently across every access member.
- Avoid overlapping generic success/error cases unless their conversion and matching behavior is intentional. `Task<Case>` does not convert to `Task<Union>`; give generic factories the intended type explicitly.
- For generic results, prefer distinct cases such as `Result<T>(Success<T>, Failure)` when success and error payload types could coincide. The success payload remains typed as `T`; this does not change the generated union's object-backed storage.
- For functional composition, use immutable cases and pure callbacks with `Map` (transform success), `Bind` (compose fallible functions), and `Match` (consume either case). Preserve failure values and skip success callbacks after failure. Keep expected domain errors as `Failure`; do not catch arbitrary callback exceptions or silently normalize an invalid default. See [functional.cs](examples/functional.cs). These operators do not guarantee callback purity or allocation freedom.

## Serialization

A union declaration does not invent a JSON discriminator. Successful serialization is not proof of deserialization. Multiple object-shaped cases require an unambiguous contract and a discriminator-aware converter/classifier, or a suitable closed hierarchy with configured polymorphism. Do not classify by the first property that happens to be present.

Preserve schema-owned tags. Check every case, null/default, unknown and missing tags, and structurally similar payloads. Source-generated JSON working under JIT is not NativeAOT proof; publish and execute the relevant application for that claim.

## Verification

Read [the reference](references/union-reference.md), [diagnostic catalog](references/diagnostics.md) and [verification guide](references/verification.md). The examples in `examples/unions.cs` are compiled by the verifier.

`scripts/verify.py` uses only the bundled probe source, works in a temporary copy, and checks compiler diagnostics and runtime output, including expected failures. No Git/submodule or source download is involved. It does not run web servers or establish EF, RDG, NativeAOT or application-wide correctness. After application edits, also run the repository's own build and relevant tests.

Compiler diagnostics and executed probes are the technical verification. A separate Roslyn analyzer is optional for project-specific policies, not required for this skill. Report the SDK, checks run and remaining limits; a successful compilation alone does not prove runtime behavior.
