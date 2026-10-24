# Workstream 4 — contracts

Implemented six TypeSpec tool input/result pairs in `Nami.Api.Contracts.Tools`, common
`Freshness`, `ToolReadInput`, `ToolResult` and `ToolError`, source-generated C# serialization,
and twelve normalized tool JSON Schemas. Removed the old write/session/runner/streaming/analysis
surfaces and metric-query public models. Original OTLP attribute, trace, log and metric data
projections remain for ingestion/storage. `/health` is the only documented HTTP route.

Integration decisions:

- `snapshot_id` and `cursor` are optional common input fields; `snapshot_id` is required and
  `next_cursor` optional on results. This makes PRD snapshot idempotence and continuation explicit.
- `fields` projections use generated `Projected*` item models with optional properties, so a
  projected result validates against its declared output schema.
- `example_trace_id` is optional: uncorrelated log errors must not fabricate a trace identity.
- `Common.Attribute.stability` optionally exposes the registry's stable/development/alpha/beta/release_candidate/deprecated/unknown
  classification. All existing attribute members are unchanged.
- `max_bytes` accepts 1024..262144 bytes, defaults to 32768. Input strings and fields counts are bounded.
- The official JSON Schema emitter omits wire-name/integer encoding transformations. Runtime schemas
  are normalized from the same TypeSpec program's OpenAPI output, after verifying the official
  emitter exported all twelve tool types; see `contracts/README.md`.

The host calls `ContractTypes.Analysis(toolName)` for exact schema names and `ContractJson` for
validation/serialization. Tool names are generated from the six TypeSpec input/result pairs.
All generated C# types are in `internal/nami.contracts/Nami/Api/Contracts/Tools.cs` and `Common.cs`.

Validation:

- `bun run build` in `contracts/`: passes, including five existing generator helper checks.
- Repeated generation: all generated files byte-identical.
- Final net11 Release build of `Nami.Api.Contracts.csproj`: succeeds with zero warnings and
  zero errors. Uses `JsonSerializerOptions.GetTypeInfo<T>()`; binlog captured under `artifacts/`.
- Parent solution build is the authoritative integrated compile; no new tests authored in
  this implementation round.

## S1–S9 follow-up

- S1: Tagged unions now emit native C#15 `union` declarations. Mixed `AttributeValue` emits a
  compiler-checked `closed class`; an interface-only hierarchy did not meet exhaustiveness.
  The installed RC2 compiler supports `closed class` and native unions, but rejects `closed interface`.
- S2: Nami attribute and metric model projections use the declared `kind` discriminator.
  Native OTLP JSON/protobuf is unchanged; internal persisted storage retains its own `type`
  representation and maps to `kind` at the tool boundary. No new success envelope was added:
  the checklist's direct tool-result shape and ToolError remain intact.
- S7: Numeric integer validation now checks exact token scale/exponent, before floating-point
  bounds. Near-2^53 fractions and underflowed fractions are rejected.

Additional direct RC2 compiler/runtime evidence, captured in `/private/tmp/nami-union-proof`:

- The complete generated contract sources compile with System.Text.Json source generation.
- An exhaustive consumer over all native MetricPointData cases and closed AttributeValue cases
  compiles with warnings treated as errors and no default arm.
- Omitting MetricSummaryPointData fails with CS8509 naming that missing case.
- Adding AddedMetricCase to a scratch copy of the generated union causes the unchanged consumer
  to fail with CS8509 naming AddedMetricCase.
- Runtime checks reject `9007199254740990.5`, `1e-1000`, `-1e-1000` and out-of-range integers;
  accept the safe integer endpoints, `1e3`, `100.00` and `0e-9999`.
- Runtime checks preserve `9007199254740993` as a tagged string-encoded int64 through a native
  union round-trip; preserve the `kind: int` discriminator; reject an empty union.

Permanent regression tests still belong to the planned sequential test phase. This follow-up
supplies behavior and compiler evidence; it does not claim the full S1–S9 integration tests exist.
