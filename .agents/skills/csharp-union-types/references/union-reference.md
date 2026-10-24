# Union reference — .NET 11 RC2

Baseline: SDK `11.0.100-rc.2.26504.105`. See [verification.md](verification.md) for source and reproducible checks. Recheck implementation-dependent behavior when changing SDKs.

## Declaration and conversion

`union U(A, B);` creates a struct containing an `object?` value and implementing `IUnion`. Value-type cases are boxed. Case types may include classes, structs, interfaces, type parameters and nullable types.

A body permits computed properties and methods but no additional instance fields, auto-properties or field-like events. Public single-parameter constructors are reserved for case creation. Other constructors must chain to a generated constructor. A conditional between two case types does not necessarily resolve a constructor overload; prefer a factory with an explicit union return type. [unions.cs](../examples/unions.cs) includes one.

Conversions work where the target union is known. They do not chain through another union or an arbitrary user-defined conversion. `Task.FromResult<Outcome>(new Accepted(...))` works; inferred `Task<Accepted>` is not `Task<Outcome>`. Custom implicit operators can shadow union conversion.

Overlapping cases need not make a declaration invalid. Better-conversion rules may select a case; unrelated applicable interfaces can make conversion ambiguous. Runtime matching sees the contained object, not a separate case tag.

Generic declarations retain their case types: `Result<int>(Success<int>, Failure)` accepts
`Success<int>`, not `Success<string>`. Distinct success/error cases keep their roles separate
when payload types coincide. The example `Result<T>` has runtime checks for `int`, `string`,
failure and default; compiler checks reject a wrong closed case (`CS0029`) and an omitted
failure arm (`CS8509`, promoted to an error). Generic type safety does not imply unboxed storage.

## Functional composition

`examples/functional.cs` composes pure functions with `Map`, `Bind` and `Match`. Map transforms
only a successful payload; Bind calls a function returning another Result; both propagate the
same Failure object without invoking the success callback. Match evaluates just the selected
branch. Expected parse/validation failures remain values. Null delegates and uninitialized unions
are programming errors; callback exceptions propagate unchanged. There is no implicit exception
capture, I/O or mutable shared state. Callbacks must themselves be pure for the composition to
be pure. The example uses explicit invariant culture and widens before multiplication.

The verifier checks representative map identity/composition and bind identity/associativity laws
for success and failure, alongside explicit expected outputs and skipped-callback checks. This
does not imply deep immutability of arbitrary payloads or allocation-free generic composition.

## Patterns

| Pattern | Input examined |
| --- | --- |
| `Accepted a`, `Accepted { Message: ... }` | Contents |
| Constant, relational, `null` / `not null` | Contents, with applicable null wrappers/references |
| Untyped property/positional pattern | Wrapper members |
| List pattern | Wrapper count/indexing members; a list case alone is insufficient |
| `var x`, `_` | Wrapper |

`u is { }` does not check whether a struct union contains a value. `not null and var x` still binds the wrapper. On RC2, type patterns inspect compatible case types; interfaces on the wrapper are not automatically interfaces of its contents. `u is U` normally produces `CS8121`.

## Completeness and null

Only switch **expressions** receive exhaustiveness analysis. For unions and closed hierarchies, promote these diagnostics when missing cases must fail the build:

```xml
<WarningsAsErrors>$(WarningsAsErrors);CS8509;CS8655;CS8846</WarningsAsErrors>
```

They cover missing cases, missing null handling and coverage dependent on guards. Preserve existing warning settings and inspect effective project properties.

`default(U)` has null contents. Non-nullable cases can still make analysis assume non-null contents on this SDK; no missing-null diagnostic is guaranteed. Add an explicit null arm when defaults can arrive. It handles the default hole without swallowing future cases. Do not silently translate an invalid default into a normal business state.

A `U?` has both nullable-wrapper absence and possible null contents. Preserve that distinction when it matters to the domain.

## Custom storage

`[Union]` with the basic creation/access pattern lets classes or structs participate. Public single-parameter constructors define cases; `Value` exposes contents. A public nested `IUnionMembers` interface can instead provide `Create` factories and access members. Compiler lookup then uses that provider.

Optional `HasValue`/`TryGetValue(out Case)` enable typed matching without object-valued access. Constructors/storage must avoid boxing too. Four behavioral invariants are assumed, not proved: sound contents, stable creation, equivalent behavior for overlapping conversions, and consistent accessors.

A record-class provider can recurse accidentally through its synthesized copy constructor. Execute factory examples before adopting them. Upstream `QLang2` variants `CLASS_PROVIDER` and `CAST_FIX` are safe checks; `REC_RECURSE` intentionally crashes and is excluded from the verifier.

## Closed hierarchies and JSON

`closed` is implicitly abstract and restricts direct subclasses to the assembly. Do not add `abstract` as well. It does not automatically seal descendants; seal concrete cases when external extension is unwanted. Closing an existing storage hierarchy may preserve behavior with less disruption than converting it to a struct union.

Bare union JSON adds no discriminator. Two empty cases can both serialize to `{}`, making reconstruction impossible without another tag. Structural classification can also be ambiguous for overlapping property sets. Unknown fields must not downgrade malformed input into another valid case.

Prefer schema-owned discriminators and generated converters. `JsonPolymorphic`/`JsonDerivedType` configure class hierarchy serialization. Do not replace stable persisted tags with inferred CLR names. Source-generated metadata must be checked through the actual options/context. `QSrcGen` proves a JIT source-generation path, not NativeAOT.

## Continuous analyzer checks

A Roslyn analyzer can use `ITypeSymbol.UnionCaseTypes` to find unions and flag catch-all arms, switching on object-valued `Value`, missing default guards under a chosen policy, and reliance on switch statements for completeness. Scope rules to the intended operation; deliberate `Value` access and defaults at parse boundaries remain valid.

An analyzer cannot generally prove byte-identical JSON, AOT execution or allocation counts. Use the relevant runtime/application checks for those claims; do not substitute a clean compiler result for behavior that was not executed.
