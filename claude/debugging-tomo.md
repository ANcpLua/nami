# What Claude needs from nami to debug tomo

tomo is a Rust service. Claude debugs it through nami, and nami is nothing but an OTLP target plus MCP tools.
No SDK, no product REST API, no nami API: the tools are the API.

## 1. The two surfaces, and nothing else

| Surface | Who uses it | What it is |
|---|---|---|
| OTLP ingest | tomo (and any other producer) | The open standard: OTLP/HTTP protobuf and OTLP/gRPC. No custom wire format, so no SDK to write |
| MCP tools | Claude | The only interface for questions. Results are a one-line headline plus `structuredContent` |

Claude needs no CLI of its own. Locally, one binary starts both surfaces on localhost (OTLP port plus MCP over stdio for
Claude Code); hosted, the same binary serves OTLP and Streamable HTTP. Starting it is one command; everything after that
is a tool call.

## 2. How tomo gets instrumented (no auto-instrumentation)

- Rust instrumentation is explicit and compile-time by nature: `tracing` spans, `#[instrument]` on functions, and the
  `tracing-opentelemetry` layer exporting OTLP to nami. Nothing rewrites code at runtime, so nothing breaks under release
  builds.
- Attribute names come from a crate generated out of nami's resolved registry (Weaver, Rust templates). tomo never types
  `"http.route"` by hand; it uses the generated constant, so a renamed convention is a compile error in tomo.

## 3. The tools Claude wants, in the order it uses them

1. `sources.status(service)`: is tomo sending at all? `last_seen` per signal. Silence is a finding, not "no errors".
2. `errors.recent(service, since)`: errors grouped by type and operation, with first seen, count and one example trace id.
3. `traces.get(trace_id, fields?)`: one trace, projected to the fields asked for.
4. `traces.slowest(service, operation?, since)`: the slow tail, not the average.
5. `logs.search(query, service, since)`: literal text search; quotes, backslashes and non-ASCII match as typed.
6. `changes.recent(service, since)`: deploys and commits in the window, so "since when" has an answer.

Every read tool takes `fields`, `max_bytes` and relative times (`-15m`), and every result carries the freshness block:

```yaml
freshness:
  watermark: 2026-10-26T21:43:12Z   # complete up to here
  complete: false                  # the window is not closed yet
  scanned_rows: 184220
```

Empty and complete means trust it; empty and incomplete means retry after the lag; no `last_seen` means report silence.

## 4. State

- MCP 2026-07-28 is stateless. nami never relies on a protocol session.
- Whatever must persist (snapshots for fan-out, watermarks) is nami's own state, valid on every instance.
- Every tool is safe to repeat: the same call gives the same answer for the same snapshot.

## 5. Spec-first, in functional terms

- A specification of algebraic data types: records (product types) and unions (sum types), strictly typed and
  independent of any language.
- A pure function from specification to artifacts: the same input gives byte-identical output, every time. The gate
  regenerates and compares.
- A human writes the spec; the agent derives everything else from it and cannot add a shape the spec does not have.

### The spec language: TypeScript, not TypeSpec

The spec is a TypeScript module that holds data only: schemas, no behavior.

- Records are `readonly` object types; unions are discriminated on one `kind` field.
- Written with Zod 4, so one declaration gives the TypeScript type (`z.infer`) and the JSON Schema
  (`z.toJSONSchema`).
- The JSON Schema is the language-independent artifact: MCP tool input and output schemas, and the Rust types for
  tomo through `typify`.
- The generated JSON Schema files are committed; the gate regenerates them and fails on any byte difference.

## 6. Functional core correctness in TypeScript

The core is pure; effects live only at the edge.

| Core (pure) | Shell (effects) |
|---|---|
| Normalizing incoming data onto the conventions | OTLP receivers |
| Grouping errors, ranking traces, computing freshness | DuckDB reads and writes |
| Building the tool result from data | MCP transport, OAuth, fetching sources |

Rules the compiler and linter enforce:

- `tsconfig`: `strict`, `exactOptionalPropertyTypes`, `noUncheckedIndexedAccess`, `noFallthroughCasesInSwitch`.
- Every `switch` over a union ends with an `assertNever(value: never)` call, so a new union case is a compile error at
  every place that does not handle it.
- No `any`, no classes and no mutation in the core: data is `readonly`, functions return new values.
- Errors in the core are values: a `Result` union (`{ kind: "ok", value }` or `{ kind: "error", code, field, hint }`),
  never a thrown exception. The shell turns them into MCP errors.
- Pure functions get property tests (fast-check): rules checked on thousands of generated inputs, not five examples.

## 7. Dates

- Sunday reset: the Rust marathon for tomo.
- 12 December: what 2808-vibing needs.
