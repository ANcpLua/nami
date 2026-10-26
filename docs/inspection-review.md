# Inspection decisions

Review of the informational C# findings from 2026-10-25. These decisions do not disable rules or suppress findings. The current counts remain available through `mise diagnostics` and `eng/inspect.sh --results current`.

## Applied changes

- Renamed local constants and fields with Roslyn symbol binding, including their references; string data and wire names were not renamed.
- Applied the installed MSTest 4.5.1 assertion fixes while preserving null handling, collection cardinality and expected/actual semantics.
- Simplified control flow, used existing type aliases, made genuinely internal implementation details private, and used concrete collection types where all callers supply that type.
- Replaced fixed runtime regular expressions with generated regex methods. The patterns and options are unchanged.
- Made JSON frame flushing and writes asynchronous while retaining the write lock through the complete frame.
- Added direct tests for the three codecs referenced by emitter output: nullable reference values, optional converted values and named non-finite doubles. They cover absent/null/present states, Int64 endpoints, invalid encodings and signed zero.

## Decisions that preserve behavior

| Finding | Decision and evidence |
| --- | --- |
| `MemberCanBePrivate` on `ConfiguredRangeAttribute.Key`, `Minimum`, `Maximum` | Keep public getters for positional attribute arguments. Making them private violates the attribute metadata contract and is rejected by CA1019. These are not ordinary private implementation properties. |
| `CA1056` on capture `SchemaUrl` fields | Keep producer-supplied strings. The capture is evidence, not a URI validator or canonicalizer. `Serialize_ProducerSchemaUrl_PreservesItsOriginalText` verifies the resource, scope and entity-reference fields. |
| `UseCollectionExpression` on `ActivitySnapshotExporter.GetSnapshots` | Keep the explicit `ConcurrentQueue.ToArray` snapshot operation. Do not assume collection-expression lowering has the same concurrent snapshot contract. |
| `UseCollectionExpression` on the exporter source set | Keep the explicit ordinal comparer and constructor's input validation; the capture source names are ordinal identifiers. |
| `AL1313` on storage worker startup | The already-owned native connections require their workers to start and perform cleanup. Scheduling cancellation must not prevent the delegate from ever running. The reader workers already explicitly use `CancellationToken.None`; operational cancellation belongs inside the loops. |
| `MethodHasAsyncOverload` on failed process-start cleanup | Keep unconditional synchronous disposal of the untransferred control pipe in the `finally` block. It closes an owned handle; no payload needs flushing. This also preserves the CA2000 ownership proof. Normal owned-process disposal remains asynchronous. |
| `AL1308` on hexadecimal escape parsing | The JSON reader validates the escape grammar before `ReadHex` receives its four digits. This is not an unvalidated external integer input. |
| `AL1308` on the canonical double exponent | The text comes from invariant round-trip formatting of a finite `double`, not from an incoming payload. Its exponent is a bounded integer. Keep canonicalization explicit. |
| Enum switch completeness suggestions | Keep the deliberate fallback for unsupported wire values. Unknown/future protobuf values, unsupported JSON token kinds and missing exemplar values must retain their established rejection or normalization behavior. Adding unrelated branches is not a new protocol guarantee. |
| `CA1032` on internal domain exceptions | Keep constructors that express the actual failure context and messages. These are internal protocol/storage failures, not general-purpose exception APIs; unused public constructor overloads would add surface without a caller. |
| Primary-constructor suggestion for `OptionalValue<T>` | Keep construction and zero-initialization explicit: a supplied value is defined, while a default value is absent. The existing form makes that contract clear and avoids incidental changes to generated record members. |
| Loop/query and alternate-enumerator suggestions | Keep eager, preallocated loops in ingestion and storage hot paths and where iteration is coupled to a transaction/reader. A shorter query is not evidence of equivalent allocation, enumeration or lifetime behavior. |
| Temporary-variable, extension-block and remaining branch-layout suggestions | Retain useful domain names and local structure where the alternative adds duplicated construction or obscures the parser/validation path. These are optional syntax choices, not unfixed behavior defects. |

## SQL command review (`CA2100`)

The command assignments were traced to their callers. No request value is used as an unescaped SQL fragment.

| Path | SQL text origin | Runtime values |
| --- | --- | --- |
| `DuckDbStore.MetricMigration.cs` | Fixed migration query fragments and generated `MetricObservationRow` insert SQL | Typed row binders and positional parameters |
| `DuckDbStore.MetricObservations.cs` | Generated `MetricPointRow` insert SQL and fixed deletion/projection statements | Typed row binders and positional parameters |
| `DuckDbStore.Metrics.cs` | Generated column lists; private migration calls select fixed table names | Positional parameters for project, series and measurement values |
| `DuckDbStore.Snapshots.cs` | Private snapshot readers receive generated table/column names and fixed ordering; writes use generated row SQL | Positional parameters and typed row binders |
| `DuckDbStore.InsertRowsBatchedAsync` | The statically selected row type generates the insert statement from its declared columns and batch size | Every row value is bound separately |
| `DuckDbStore.ExecutePragma` | Fixed SET statement names | Numeric options are formatted invariantly; string options use `EscapeSqlLiteral` |
| `DuckDbStore.HasRows` | Table names from the generated schema/migration catalog, quoted with `QuoteIdentifier` | No request input is concatenated |
| `DuckDbStore.ExecuteSchemaSql` | Generated DDL or private migration statements; discovered index names are checked against the declared catalog and quoted | No client SQL is accepted |

`EscapeSqlLiteral` doubles single quotes; `QuoteIdentifier` doubles identifier quotes. The schema/row generators remain the source of SQL shape. These review findings stay visible rather than being silenced with a severity override.

## Validation

Use the repository Check gate for the complete generated-contract, registry, managed-test and native-test path. The final InspectCode run must retain the full solution and a stable source/output fingerprint. A successful scoped or unit run is not a substitute for that complete check.
