# nami

nami is an agent-operated application debugging stack: compile-time instrumentation,
configured observation lifetimes, its own OpenTelemetry collector, DuckDB-backed analysis
and six read-only MCP tools. Agents use it through a Claude.ai connector
and/or an OpenAI plugin with bundled MCP integration; local clients are also supported. There is
no dashboard or human-facing analysis UI. Tomo(not yet build rust app) is an example developer whose application an
agent investigates, not the only supported application.

The runtime is **TypeScript/Bun MCP + C#/.NET 11 NativeAOT engine**, connected locally by
private streams/pipes. TypeSpec generates the shared C#/TypeScript wire contracts and JSON Schemas;
Weaver supplies semantic definitions and persistence rules. A common release need not be one native file.
See the [PRD](docs/prd/nami.md), [architecture](docs/architecture.yaml) and
[migration checklist](docs/checklist.md).

The transport migration is implemented; the EndToEnd suite runs through the TypeScript host
and published native engine. Compile-time instrumentation of supported applications and
observation lifetimes are required product scope; their application path is not wired in yet.
Nami's existing self-instrumentation covers its own operations and does not replace that scope.
Nami has no product SDK or public CLI; launcher arguments and engineering scripts operate the
service and its tests. The binding I1–I18 and D1–D6 requirements and their limits are in the
[request/response review](docs/prd/nami-request-response-review.md); implementation and actual
test evidence, limits and remaining work are mapped per requirement in the
[I1–I18 / D1–D6 evidence matrix](docs/integration/requirement-evidence.md). The checklist
summarizes progress, including the outstanding Claude.ai/OpenAI distribution evidence.

## Two surfaces

| Surface | Used by | What it is |
| --- | --- | --- |
| OTLP | Instrumented OpenTelemetry producers | Standard OTLP/HTTP (protobuf or JSON) and OTLP/gRPC; ingestion requires no Nami-specific producer SDK. |
| MCP | Agents | Six read-only tools over stdio or Streamable HTTP; MCP `2026-07-28`, stateless, no protocol session. |

The TypeScript supervisor starts the native engine and owns its private, bounded JSONL streams.
MCP uses stdio or Streamable HTTP; the engine exposes OTLP. No internal REST analysis endpoint is needed.

Stdio reads the `default` project. For another project, use Streamable HTTP with its project-bound read credential.

## Connect an agent

Publish the native engine and bundled TypeScript entry point together. Bun is required at runtime;
the container includes it. The engine loads the DuckDB library beside it and does not require an
installed .NET runtime. Publishing needs the platform's native toolchain (on Linux, `clang` and `zlib1g-dev`).

```bash
./eng/build.sh PublishNative --Configuration Release
```

**stdio.** Claude Code starts nami and speaks MCP over its stdin and stdout:

```bash
claude mcp add --env NAMI_OTLP_AUTH_MODE=Unsecured --transport stdio nami -- bun "$PWD/artifacts/publish/mcp/main.js" --engine "$PWD/artifacts/publish/nami" --stdio
```

**Streamable HTTP.** Run nami yourself; MCP is at `http://127.0.0.1:5100/mcp`:

```bash
NAMI_OTLP_AUTH_MODE=Unsecured bun artifacts/publish/mcp/main.js --engine "$PWD/artifacts/publish/nami"
claude mcp add --transport http nami http://127.0.0.1:5100/mcp
```

nami listens on loopback unless `NAMI_BIND_ADDRESS` says otherwise. `NAMI_OTLP_AUTH_MODE=Unsecured`
lets local producers send without a key; outside the Development environment the default is
`ApiKey`, which needs project keys (below). The store is the DuckDB file `NAMI_DATA_PATH`, by
default `nami.duckdb` in the working directory. Each nami process binds the OTLP ports and holds
its store open, so for several MCP clients at once, run one supervisor and connect them over
Streamable HTTP. `/health` on the MCP port reports native-engine readiness. A non-loopback MCP
listener requires `NAMI_PUBLIC_URL`; on Railway its public domain is used automatically. The
container runs the same two-process stack ([architecture](docs/architecture.yaml)).

## Send telemetry

Any OpenTelemetry SDK exports to nami with its standard settings:

| Transport | Endpoint | SDK settings |
| --- | --- | --- |
| OTLP/HTTP, protobuf or JSON | `http://127.0.0.1:4318` (`/v1/traces`, `/v1/logs`, `/v1/metrics`) | `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4318`, `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf` |
| OTLP/gRPC | `127.0.0.1:4317` | `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317`, `OTEL_EXPORTER_OTLP_PROTOCOL=grpc` |

All three land in the same rows. What the tools can answer comes from the resource and the records:

- `service.name` is what every tool's `service` argument filters on. A resource without it is
  counted as a problem of its project, not dropped.
- In authenticated mode the credential selects the project; a conflicting `nami.project.id`
  resource value is refused. Tool arguments and unverified metadata never select another project.
- `vcs.ref.head.revision`, `service.version`, `deployment.environment.name` and `cicd.*` on the
  resource are what `changes_recent` reports when they change.
- An exception counts whether it arrives as a log record with event name `exception` or as a span
  event named `exception`; `errors_recent` groups both together.

With `NAMI_OTLP_AUTH_MODE=ApiKey`, every
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
| `src/nami` | Native OTLP/analysis engine, private stream transport, self-telemetry and container definition. |
| `src/nami.mcp` | TypeScript/Bun MCP server, generated tool manifest, authentication and native-engine supervisor; `nami.mcp.esproj` is an enabled build project in `nami.slnx`. |
| `src/nami.collector.ingestion` | OTLP decoding over HTTP and gRPC, attribute normalization against the registry, sanitizer limits, watermarks. |
| `src/nami.collector.storage` | DuckDB storage per project and the read-only snapshots the tools run on. |
| `registry/` | The canonical Weaver registry and policies; editable definitions and pinned imports live in `registry/source/`. |
| `templates/registry/` | Jinja templates that generate C# (`internal/nami.semconv`) and Markdown (`docs/semconv/`) from the registry. |
| `contracts/` | TypeSpec definitions and emitters for Nami ingestion models, stored JSON, DuckDB rows, configuration, tools and the private engine protocol. Outputs include C# contracts and consumer-specific types/mappings, wire JSON Schemas, and TypeScript types, codecs and tool metadata. |
| `internal/` | Generated contracts and conventions, nami's self-telemetry defaults, and the source generators. |
| `eng/` | The Fallout build. |
| `tests/` | Shared test support plus MSTest Unit, Integration and EndToEnd suites; EndToEnd drives the NativeAOT host. |
| `docs/` | The PRD, the architecture, the implementation checklist and the integration notes. |
| `.railway/railway.ts` | The Railway service `nami`. |

Generated C# lives in `internal/nami.contracts` and the relevant consumer projects, including
`src/nami.collector.ingestion/Generated` and `src/nami.collector.storage/Generated`.
TypeScript contracts live in `src/nami.mcp/src/contracts/generated`; wire schemas live in
`contracts/json-schema/`. For persistence, TypeSpec emits C# rows with DuckDB metadata, then
the Roslyn generator in `internal/nami.collector.storage.generators` produces DDL, binders
and readers. Identity algorithms, query execution and other runtime behavior have their own
implementations; the [model inventory](contracts/model-migration.csv) records migration status.

## Build

`mise install` installs the pinned toolchain: the .NET SDK `11.0.100-rc.3.26522.148` (.NET 11 RC3,
pinned in `global.json` too), Weaver `0.28.0`, Bun and Node. The build is Fallout, and
`./eng/build.sh --help` lists every target.

`nami.slnx` builds eleven .NET projects and `src/nami.mcp/nami.mcp.esproj`. The JavaScript SDK
is pinned in `global.json`; the MCP project restores with `bun install --frozen-lockfile`
when its inputs change and invokes the existing `bun run build`. `Compile` builds MCP through
the solution once; the separate `Mcp` target delegates to the same `.esproj`.

| Command | Does |
| --- | --- |
| `./eng/build.sh Check --Configuration Release` | The gate before every push, and in CI on every push and pull request: Registry, contract parity, compilation, MSTest suites and the published NativeAOT conformance proof. |
| `./eng/build.sh Registry` | Runs `weaver registry check --future -r registry`, regenerates the C# and Markdown outputs, and fails if they differ from the commit. |
| `./eng/build.sh Contracts` | Regenerates C#/TypeScript contracts, shared metadata and `contracts/json-schema/` from TypeSpec; commit them with the TypeSpec change. |
| `./eng/build.sh Compile` | Builds `nami.slnx`, including the .NET projects and TypeScript/Bun MCP `.esproj`. |
| `./eng/test.sh --suite Unit` | Runs the fast MSTest contract suite. `Integration`, `EndToEnd` and `All` select the other scopes. |
| `./eng/inspect.sh` | Builds and inspects `nami.slnx` with pinned tools and bounded findings; retains only the current external result. |
| `./eng/build.sh InspectCode --Configuration Release` | Runs the same inspection entry point; findings and invalid analysis remain distinct. |
| `./eng/build.sh Publish` | Runs the full gate and produces the native engine and bundled MCP entry in `artifacts/publish`. |

Start with `mise diagnostics` for the current build, test and inspection status. Executables
resolve from `mise.toml`; validation does not auto-install missing tools. For details use
`./eng/inspect.sh --results current`, or select `--target typescript` / `--target mcp`.
The wrappers keep one current result per lane outside the checkout and remove old workspace
reports. Do not read historical SARIF, TRX, logs or binlogs into an agent context.

`--no-build` requires a matching prepared build. Source/output drift, missing references and
incomplete analysis are rejected. Build success is distinct from inspection findings.
See [testing and inspection](docs/testing.md) for the command contract and current-state rules.

Generated files are committed and never edited by hand: change the registry, a template or the
TypeSpec, and regenerate.

## Documentation

- [Product spec](docs/prd/nami.md) (German): purpose, architecture, the invariants, decisions and risks
- [Architecture](docs/architecture.yaml): TypeScript MCP, native engine, one shared contract
- [Semantic conventions](docs/semconv/): generated from the registry, one page per namespace
- [Implementation checklist](docs/checklist.md) and [integration notes](docs/integration/README.md)
- [I1–I18 / D1–D6 evidence matrix](docs/integration/requirement-evidence.md): implementation, concrete test evidence, limits and remaining work per requirement

## License

nami is free, under the [MIT license](LICENSE). `contracts/THIRD-PARTY-NOTICES.txt` carries the MIT
notice of ajv-formats, whose URI grammar `contracts/common/types.tsp` adapts.
