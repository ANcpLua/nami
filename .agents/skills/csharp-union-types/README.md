# C# 15 unions: skill, compiler proofs and polyfills

A C# 15 skill for AI coding agents, backed by local compiler diagnostics and executed probes. Use the bundled guidance to implement and review code; run only the checks relevant to the requested change.

The baseline is **.NET SDK 11.0.100-rc.2.26504.105**. Findings are version-specific. `union` compiles with C# 15.0 on this SDK; earlier preview instructions requiring `LangVersion=preview` must not be applied universally.

## Contents

- [SKILL.md](SKILL.md): concise implementation/review guidance.
- [Language reference](references/union-reference.md) and [diagnostic catalog](references/diagnostics.md).
- [Verification guide](references/verification.md): compiler/runtime checks and their limits.
- [Examples](examples/unions.cs): readonly unions with compiled and executed checks for cases, defaults and scalar matching.
- [Functional composition](examples/functional.cs): pure `Map`/`Bind`/`Match` operators and a parsing/validation pipeline with errors as values.
- [Polyfills](polyfills/README.md): source-only `UnionAttribute` and `IUnion` for targets before .NET 11.
- Bundled probes in `probes/`, with the required license notices. No external repository is required.
- Runtime reports are transient external diagnostic state; no captured run is bundled with the instructions.

## Run

Install Python 3.10+ and the exact SDK in `global.json`, then:

```sh
git clone https://github.com/ANcpLua/csharp-union-types.git
cd csharp-union-types
python3 scripts/verify.py
```

An ordinary directory copy or ZIP also works. The verifier copies the supplied local inputs into a temporary directory, builds sequentially with at most two workers, checks expected diagnostic IDs, runs executable probes, and exits nonzero on missing or failed evidence. Normal skill use needs only the local Markdown references; no browsing or verifier run is required to read the guidance.

Verification covers union and closed-hierarchy diagnostics, runtime matching/JSON behavior, and legacy/modern polyfill boundaries. The current verifier result is authoritative only for its recorded inputs; historical runs are not instruction sources.

## Important boundaries

Functional composition keeps expected errors in the result:

```csharp
var message = FunctionalPipeline.ParseInteger("21")
    .Bind(FunctionalPipeline.RequirePositive)
    .Map(static value => (long)value * 2)
    .Match(
        static value => $"Doubled: {value}",
        static failure => $"Rejected: {failure.Reason}");
```

`Map` transforms success, `Bind` chains fallible functions, and `Match` consumes either case.
After failure, success callbacks are skipped. Purity depends on the supplied functions; the
operators do not hide their exceptions or make mutable payloads immutable.

- Exhaustiveness is checked for **switch expressions**, not switch statements. Missing-case/null/guard warnings must be promoted if they must reject the build.
- A union can contain null at runtime even when its case types are nonnullable. Handle `default(U)` deliberately; a successful compilation does not prove runtime safety.
- A null arm preserves missing-case diagnostics. A catch-all arm masks future cases.
- A compiler does not prove JSON roundtrips, allocation counts or NativeAOT compatibility. The selected runtime probes cover specific JSON/default behaviors; no whole-framework or NativeAOT claim is made.
- Polyfills supply compiler-recognized types. They do not upgrade the compiler or backport .NET 11 serialization.

CI executes the same script and uploads its report. It does not publish NuGet packages.

## Credits and license

Author and maintainer: Alexander Nachtmann. The package is written for AI coding agents and built from the local files and probes he supplied and selected. Included third-party code retains its existing copyright and license notices. There are no remote-source lookups, commit comparisons or source-hash checks.
