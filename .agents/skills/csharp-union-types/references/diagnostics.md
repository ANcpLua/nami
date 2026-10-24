# Compiler diagnostics and repairs

This file is the local reference for the skill. No website or external repository is needed to use it. The examples and observed diagnostic codes are pinned to SDK `11.0.100-rc.2.26504.105`; the verification report records the actual result.

## Switches, patterns and null

| ID | Meaning | Repair | Local check |
| --- | --- | --- | --- |
| CS8509 | A switch expression leaves a possible case uncovered. | Add the missing case arm. Avoid a catch-all if future case additions must remain visible. | `missing_case_is_error_when_promoted` |
| CS8655 | A switch expression leaves tracked null contents uncovered. | Add a null arm or establish the required non-null state before matching. | `missing_null_is_error_when_promoted` |
| CS8846 | A guard can fail, leaving values uncovered. | Cover the remaining values with an appropriate case arm. | `guarded_coverage_is_error_when_promoted` |
| CS8510 | An earlier arm already covers this arm, or it is impossible. | Remove/reorder overlapping arms or correct the impossible pattern. | `duplicate_arm_is_unreachable` |
| CS8121 | The pattern type cannot match the input's possible types. | Match an actual compatible case type. A wrapper self-type test is not a presence check. | `self_type_rejected` |

CS8509, CS8655 and CS8846 are warnings unless promoted; CS8510 and CS8121 are errors. Switch **statements** do not receive the same completeness guarantee as switch expressions.

```ini
[*.cs]
dotnet_diagnostic.CS8509.severity = error
dotnet_diagnostic.CS8655.severity = error
dotnet_diagnostic.CS8846.severity = error
```

The same promotion can be expressed through a project's `WarningsAsErrors`. Preserve the existing setting rather than replacing its other codes.

### Default contents are a separate runtime concern

A generated union is a struct. `default(Pet)` contains null even when `Pet` only lists nonnullable types. Field/array initialization can produce that value. If all case types are nonnullable, analysis can still begin with the contents marked non-null, so no missing-null warning is guaranteed.

A nullable case such as `Cat?` makes the default tracked contents state maybe-null. Construction propagates the incoming case's null state. Successful `HasValue`/`TryGetValue` queries refine the contents state. An earlier null test can therefore change later diagnostics. None of this changes definite-assignment rules: an unassigned local cannot be read.

Use an explicit null arm whenever default contents can reach a switch. It covers null without swallowing new case types. Choose the null outcome according to the domain: reject invalid uninitialized state, or represent meaningful absence deliberately. Never assume adding a discard arm improves completeness checking.

## Union declaration requirements

| ID | Meaning | Repair | Local check |
| --- | --- | --- | --- |
| CS9370 | The declaration supplies no case type. | Supply at least one intended alternative. | `declaration_requires_case` |
| CS9371 | A case cannot convert to object through a reference or boxing conversion. | Replace the unsupported case, such as a ref struct, with a suitable representation. | `declaration_rejects_nonboxable_case` |
| CS9373 | The declaration adds instance fields, auto-properties or field-like events. | Keep payload state in the case types; use computed members or static state where appropriate. | `declaration_rejects_instance_field` |
| CS9374 | An explicitly declared public single-parameter constructor occupies the generated case-constructor shape. | Remove it, use a factory or choose an allowed non-public constructor design. | `declaration_rejects_public_case_constructor` |
| CS9375 | A declared constructor has no required this initializer. | Supply a valid `this(...)` chain to a synthesized or explicitly declared constructor. A typed factory often avoids overload ambiguity. | `declaration_requires_initializer` |
| CS9385 | A custom union exposes no creation member. | Supply suitable public case constructors, or static Create members on its union member provider. | `custom_union_requires_creation` |
| CS9386 | The provider has no suitable Value contract. | Expose an instance object/object? Value property with a public getter on the provider contract. | `provider_requires_value` |
| CS9387 | A keyword union uses a member provider interface. | Remove that provider or use a custom attributed class/struct when provider-based construction is required. | `declaration_rejects_provider` |

### Precise RC2 examples matter

```csharp
public union Empty; // CS9370
```

On this compiler, `public union Empty();` produces parser error **CS1031** first. Describing the semantic rule as “a union needs cases” is correct; assigning CS9370 to every spelling of an empty declaration is not.

For CS9387, the tested declaration actually implements its nested provider interface. Merely declaring an unused nested interface did not reproduce the error. The concrete source is included in `verification/Diagnostics/Cases.cs`.

## Closed-hierarchy restrictions

These were missing from the earlier local catalog. They belong beside the union rules because a closed class/record is often the appropriate alternative.

| ID | Meaning | Repair | Local check |
| --- | --- | --- | --- |
| CS9380 | A type or alias uses the prohibited name closed. | Choose another identifier. | `closed_name_is_reserved` |
| CS9381 | A closed type is also sealed or static. | Remove the conflicting modifier, or choose an ordinary sealed/static type if no hierarchy is intended. | `closed_cannot_be_sealed` |
| CS9382 | A type directly derives from a closed base declared in another assembly. | Put the permitted hierarchy in its owning assembly or deliberately use an open inheritance design. | `closed_rejects_external_derivation` |
| CS9383 | A subtype's generic parameter is absent from the closed base-type reference. | Make the relationship account for that parameter, or remove the unnecessary generic parameter. | `closed_generic_parameter_must_be_bound` |
| CS9384 | A closed type is also explicitly abstract. | Remove abstract; closed already implies it. | `closed_is_already_abstract` |
| CS9395 | IsClosedTypeAttribute.DerivedTypes has an incompatible shape. | Its property, when supplied, must be an instance Type[] property with public get and set accessors and no parameters. | `closed_metadata_requires_type_array` |

```csharp
public closed class ResultState { }
public sealed class Ready : ResultState { }
// Both belong to the same assembly.
```

Closing a base restricts **direct** derivation across assemblies. It does not automatically seal a descendant. A nonsealed child can remain an external extension point. Seal concrete children when the set of leaf types must remain fixed.

A new case is only diagnosed in consumers that are recompiled. An old consumer assembly can throw when passed a newly added case even if the new library compiled successfully. A source-level completeness guarantee is not binary-compatibility protection.

## What these diagnostics do not prove

There is no automatic boxing warning promised here. A generated union boxes value-type cases; presence of `TryGetValue` alone does not prove a whole operation allocation-free. Measure the operation or inspect its generated code.

Successful compilation also does not prove JSON roundtrips or NativeAOT compatibility. Use the local runtime checks for their documented scope and the consuming application's own tests for additional behavior.

## Evidence

The diagnostic IDs and messages are verified against the pinned compiler, not a documentation page. Run `scripts/verify.py` for current emitted diagnostics and compiler version; reports are stored outside the instruction tree. The remediation text is explained by the local source examples; changing those examples requires rerunning the verifier. Runtime claims require the recorded runtime result, not just the absence of a compiler error.
