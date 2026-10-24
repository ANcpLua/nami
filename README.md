# nami

nami is an agent-operated application debugging stack: compile-time instrumentation, OpenTelemetry,
DuckDB-backed analysis and six read-only MCP tools. Agents use it through a Claude.ai connector
and/or an OpenAI plugin with bundled MCP integration; local clients are also supported. There is
no dashboard or human-facing analysis UI. Tomo is an example Rust developer whose application an
agent investigates, not the only supported application.

The approved target is **TypeScript/Bun MCP + C#/.NET 11 NativeAOT engine**, connected locally by
private streams/pipes. TypeSpec generates the shared C#/TypeScript wire contracts and JSON Schemas;
Weaver supplies semantic definitions and persistence rules. A common release need not be one native file.
See the [PRD](docs/prd/nami.md), [architecture](docs/architecture.yaml) and
[migration checklist](docs/checklist.md).

**Current implementation:** the commands below describe the existing C# MCP host. The TypeScript
host, private transport, connector bundles and full instrumentation proofs are pending migration.
The runtime instructions below are not evidence that the new target architecture is implemented.

## Current host: two surfaces

| Surface | Used by | What it is |
| --- | --- | --- |
| OTLP | Instrumented OpenTelemetry producers | OTLP/HTTP (protobuf or JSON) and OTLP/gRPC. No custom wire format, so no SDK. |
| MCP | Agents | Six read-only tools over stdio or Streamable HTTP; MCP `2026-07-28`, stateless, no protocol session. |

One process serves both. `nami --stdio` is for a client that starts nami itself, such as Claude
Code; `nami` serves MCP over Streamable HTTP. Either way the OTLP ports are open.

Stdio reads the `default` project. For another project, use Streamable HTTP with its project-bound read credential.

## Connect an agent

Publish the binary into a directory of its own; it loads the DuckDB library that lands beside it.
NativeAOT needs the platform's native toolchain (on Linux, `clang` and `zlib1g-dev`).

```bash
dotnet publish src/nami/nami.csproj -c Release -o artifacts/nami
```

**stdio.** Claude Code starts nami and speaks MCP over its stdin and stdout:

```bash
claude mcp add --env NAMI_OTLP_AUTH_MODE=Unsecured --transport stdio nami -- "$PWD/artifacts/nami/nami" --stdio
```

**Streamable HTTP.** Run nami yourself; MCP is at `http://127.0.0.1:5100/mcp`:

```bash
NAMI_OTLP_AUTH_MODE=Unsecured artifacts/nami/nami
claude mcp add --transport http nami http://127.0.0.1:5100/mcp
```

nami listens on loopback unless `NAMI_BIND_ADDRESS` says otherwise. `NAMI_OTLP_AUTH_MODE=Unsecured`
lets local producers send without a key; outside the Development environment the default is
`ApiKey`, which needs project keys (below). The store is the DuckDB file `NAMI_DATA_PATH`, by
default `nami.duckdb` in the working directory. Each nami process binds the OTLP ports and holds
its store open, so for several MCP clients at once, run one `nami` and connect them all over
Streamable HTTP. `/health` on the MCP port reports readiness. Hosted, the same binary runs as the
Railway service `nami` ([architecture](docs/architecture.yaml)).

## Send telemetry

Any OpenTelemetry SDK exports to nami with its standard settings:

| Transport | Endpoint | SDK settings |
| --- | --- | --- |
| OTLP/HTTP, protobuf or JSON | `http://127.0.0.1:4318` (`/v1/traces`, `/v1/logs`, `/v1/metrics`) | `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4318`, `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf` |
| OTLP/gRPC | `127.0.0.1:4317` | `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317`, `OTEL_EXPORTER_OTLP_PROTOCOL=grpc` |

All three land in the same rows. What the tools can answer comes from the resource and the records:

- `service.name` is what every tool's `service` argument filters on. A resource without it is
  counted as a problem of its project, not dropped.
- `nami.project.id` names the project the telemetry is stored under; without it, `default`. No
  tool argument, header or `_meta` field selects a project.
- `vcs.ref.head.revision`, `service.version`, `deployment.environment.name` and `cicd.*` on the
  resource are what `changes_recent` reports when they change.
- An exception counts whether it arrives as a log record with event name `exception` or as a span
  event named `exception` (the `tracing-opentelemetry` path); `errors_recent` groups both together.

Supported Rust applications can take attribute names from the generated `crates/nami-semconv` crate,
so renamed constants fail at compilation rather than silently diverging from the catalog. With `NAMI_OTLP_AUTH_MODE=ApiKey`, every
OTLP request carries a project key in the `x-otlp-api-key` header; `NAMI_OTLP_PROJECT_KEYS` maps
projects to keys, and a resource whose `nami.project.id` names another project is refused.

## Tools

Six read-only tools for agents investigating supported applications:

| Tool | Answers | Extra arguments |
| --- | --- | --- |
| `sources_status` | Is it sending at all? `last_seen` per signal (traces, metrics, logs); a signal with nothing received is `silent`, with `silent_for`. | |
| `errors_recent` | Errors grouped by type and operation, with `first_seen`, `last_seen`, `count` and one example `trace_id`; no stack traces. | |
| `traces_get` | One trace, projected to `fields`. | `trace_id` |
| `traces_slowest` | The slow tail, not the average: the slowest traces, optionally of one operation. | `operation`, `limit` (default 10, at most 100) |
| `logs_search` | Log records that contain `query` exactly as typed: no regex, no case folding. | `query` |
| `changes_recent` | Revisions, versions, environments and pipeline runs that changed in the window. | |

Every tool also takes `service`, `since` (`-15m` or RFC 3339, default `-60m`), `fields` and
`max_bytes` (default 32768, at most 262144). The window is `[since, now)`. An unknown field is
refused with the list of allowed ones. Every tool is annotated `readOnlyHint: true`,
`idempotentHint: true`, `destructiveHint: false` and `openWorldHint: false`: the same call on the
same snapshot gives the same bytes, and `tools/list` is sorted and cacheable (`ttlMs`,
`cacheScope`). Input and output schemas are the JSON Schema emitted from the TypeSpec in
`contracts/`.

A result is one object in three places: `content[0]` is a headline naming the top rows, their ids
and the tool to call next; `content[1]` is the result as JSON; `structuredContent` is the same
object, and `max_bytes` bounds it. A refusal is an `isError` result carrying a `ToolError`:
`code`, `message`, `retryable`, and where they apply `field`, `allowed`, `hint`, `resource_type`
and `resource_id`. Profiles and baggage answer `data_unavailable`. A `traceparent` in `_meta`
makes the tool's span a child of the caller's span.

Every result carries the freshness block:

```yaml
freshness:
  watermark: 2026-10-26T21:43:12Z   # ingest is complete up to here, for the project and the signals read
  complete: false                  # true when the window ends at or before the watermark
  examined: 184220                 # records read; null when unknown
  matched: 12                      # records that matched; null when the read was cut short
  returned: 10                     # items in this result
  truncated: true                  # something was left out; next_cursor continues
```

Empty and `complete: true` is a finding. Empty and `complete: false` means ask again after the
ingest lag; it never means "no errors". A window that ends past the watermark is never complete.

## Repository

| Path | What it is |
| --- | --- |
| `src/nami` | The binary: one Kestrel host with the OTLP endpoints and MCP over stdio and Streamable HTTP, the six tools (`Tools/`, `Analysis/`), self-telemetry (`Telemetry/`) and the `Dockerfile`. |
| `src/nami.collector.ingestion` | OTLP decoding over HTTP and gRPC, attribute normalization against the registry, sanitizer limits, watermarks. |
| `src/nami.collector.storage` | DuckDB storage per project and the read-only snapshots the tools run on. |
| `registry/` | The canonical Weaver registry and policies; editable definitions and pinned imports live in `registry/source/`. |
| `templates/registry/` | Jinja templates that generate C# (`internal/nami.semconv`), Rust (`crates/nami-semconv`) and Markdown (`docs/semconv/`) from the registry. |
| `contracts/` | TypeSpec for every tool input and result. It generates the C# types in `internal/nami.contracts` and the JSON Schema in `contracts/json-schema/`; the target additionally restores generated TypeScript types. |
| `internal/` | Generated contracts and conventions, nami's self-telemetry defaults, and the source generators. |
| `eng/` | The Fallout build. |
| `tests/` | Shared test support plus MSTest Unit, Integration and EndToEnd suites; EndToEnd drives the NativeAOT host. |
| `docs/` | The PRD, the architecture, the implementation checklist and the integration notes. |
| `claude/debugging-tomo.md` | Agent debugging workflow and the corrected Rust-developer example. |
| `.railway/railway.ts` | The Railway service `nami`. |

## Build

`mise install` installs the pinned toolchain: the .NET SDK `11.0.100-rc.2.26504.105` (.NET 11 RC2,
pinned in `global.json` too), Weaver `0.28.0`, Rust `1.99.0`, Bun and Node. The build is Fallout, and
`./eng/build.sh --help` lists every target.

| Command | Does |
| --- | --- |
| `./eng/build.sh Check --Configuration Release` | The gate before every push, and in CI on every push and pull request: Registry, contract parity, compilation, MSTest suites and the published NativeAOT conformance proof. |
| `./eng/build.sh Registry` | Runs `weaver registry check --future -r registry`, regenerates the C#, Rust and Markdown outputs, and fails if they differ from the commit. |
| `./eng/build.sh Contracts` | Regenerates `internal/nami.contracts` and `contracts/json-schema/` from the TypeSpec; commit them with the TypeSpec change. |
| `./eng/build.sh Compile` | Builds `nami.slnx`. |
| `./eng/test.sh --suite Unit` | Runs the fast MSTest contract suite. `Integration`, `EndToEnd` and `All` select the other scopes. |
| `./eng/inspect.sh` | Builds and inspects all of `nami.slnx` in Release, prints diagnostic counts and every rule with a source location, and retains the full SARIF and tool log under `artifacts/inspection/`. |
| `./eng/build.sh InspectCode --Configuration Release` | Runs the same inspection after the Fallout compile target. Findings fail this target; tool failures and incomplete reports fail separately. |
| `./eng/build.sh Publish` | Runs the full gate and produces the NativeAOT binary in `artifacts/publish`. |

Inspection uses the installed JetBrains `jb` tool and the SDK selected by `global.json`; no shell PATH
prefix is needed on the development Mac. Use `./eng/inspect.sh --configuration Debug` for Debug or
`--no-build` only after a successful build of the same configuration. Every run writes to a new
directory, so a stale SARIF cannot look like a successful inspection. When the tool exits successfully,
the wrapper returns 0 for no findings, 1 for findings, or 2 for an incomplete or invalid analysis.
A nonzero build or tool exit code is preserved and identified in the output.
Analyzer-load and source-generator failures in the tool log invalidate a nominally successful SARIF.
The `Check` gate retains its OTLP, DuckDB and NativeAOT proofs through the three MSTest suites;
inspection is a separate explicit target while the current inspection findings are resolved.

Normal build output keeps compiler warnings visible, even on success (`PASS WITH DIAGNOSTICS`).
Build and publish binlogs use unique names under `artifacts/binlogs/`.
See [testing and inspection](docs/testing.md) for prepared/module runs, expected test counts and
the per-run evidence directories.

Generated files are committed and never edited by hand: change the registry, a template or the
TypeSpec, and regenerate.

## Documentation

- [Product spec](docs/prd/nami.md) (German): purpose, architecture, the invariants, decisions and risks
- [Architecture](docs/architecture.yaml): TypeScript MCP, native engine, one shared contract
- [Agent debugging workflow](claude/debugging-tomo.md)
- [Semantic conventions](docs/semconv/): generated from the registry, one page per namespace
- [Implementation checklist](docs/checklist.md) and [integration notes](docs/integration/README.md)

## License

nami is free, under the [MIT license](LICENSE). `contracts/THIRD-PARTY-NOTICES.txt` carries the MIT
notice of ajv-formats, whose URI grammar `contracts/common/types.tsp` adapts.
