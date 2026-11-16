# nami

nami is an agent-operated application debugging stack: compile-time instrumentation,
configured observation lifetimes, its own OpenTelemetry collector, DuckDB-backed analysis
and six read-only MCP tools. The product is a remote Claude connector
([decision 0001](docs/decisions/0001-remote-connector-and-who-pays.md)). The sign-in that Claude on
the web, in the desktop app and on the phone needs
([decision 0002](docs/decisions/0002-sign-in-is-built-into-nami.md)) is not built yet: today nami
serves MCP clients, such as Claude Code, that start it over stdio or connect over Streamable HTTP,
with a project key unless the local `Unsecured` mode is on. There is no dashboard or human-facing
analysis UI. Tomo is an example developer whose application an agent investigates, not the only
supported application.

The runtime is **TypeScript/Bun MCP + C#/.NET 11 NativeAOT engine**, connected locally by
private streams/pipes. TypeSpec generates the shared C#/TypeScript wire contracts and JSON Schemas;
Weaver supplies semantic definitions and persistence rules. A common release need not be one native file.
See the [PRD](docs/prd/nami.md), [architecture](docs/architecture.yaml) and
[migration checklist](docs/checklist.md). The permanent decisions are in
[docs/decisions](docs/decisions/README.md).

The transport migration is implemented; the EndToEnd suite runs through the TypeScript host
and published native engine. Compile-time instrumentation of supported applications and
observation lifetimes are required product scope; their application path is not wired in yet.
Nami's existing self-instrumentation covers its own operations and does not replace that scope.
Nami has no product SDK or public CLI; launcher arguments and engineering scripts operate the
service and its tests. The binding I1–I18 and D1–D6 requirements and their limits are in the
[request/response review](docs/prd/nami-request-response-review.md); implementation and actual
test evidence, limits and remaining work are mapped per requirement in the
[I1–I18 / D1–D6 evidence matrix](docs/integration/requirement-evidence.md). The checklist
summarizes progress, including the outstanding evidence for the Claude connector.

## Two surfaces

| Surface | Used by | What it is |
| --- | --- | --- |
| OTLP | Instrumented OpenTelemetry producers | Standard OTLP/HTTP (protobuf or JSON) and OTLP/gRPC; ingestion requires no Nami-specific producer SDK. |
| MCP | Agents | Six read-only tools over stdio or Streamable HTTP; MCP `2026-07-28`, stateless, no protocol session. |

The TypeScript supervisor starts the native engine and owns its private, bounded JSONL streams.
MCP uses stdio or Streamable HTTP; the engine exposes OTLP. No internal REST analysis endpoint is needed.

Stdio carries no credential: it works only with `NAMI_OTLP_AUTH_MODE=Unsecured` and reads the `default` project.
With `NAMI_OTLP_AUTH_MODE=ApiKey`, the default outside Development, the host refuses `--stdio` at start; use
Streamable HTTP with a project-bound read credential.

## Connect an agent

Publish the native engine and bundled TypeScript entry point together. Bun is required at runtime;
the container includes it. The engine loads the DuckDB library beside it and does not require an
installed .NET runtime. Publishing needs the platform's native toolchain (on Linux, `clang` and `zlib1g-dev`).

nami runs natively on macOS and Linux. On Windows it runs in WSL2 (the Linux build) or as the Linux
container under Docker Desktop; there is no native Windows build.

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
- Telemetry is stored as it was sent: keys, types, containers, event fields and content. What a
  sender captures is decided at the source, not by nami. Ingest changes a record in two ways only:
  the credential stage, and size bounds.
- The size bounds are nami's own receiver policy. One attribute value keeps 256 KiB as nami stores
  it, arrays and maps counted whole: a longer string is cut between characters and ends in
  `…[truncated]`; a larger array, map or bytes value is dropped. A span, event, link, log record and
  scope keep 128 attributes; a value nested deeper than 16 becomes an empty value; one record keeps
  1 MiB of attributes. What a bound removes is added to the record's dropped attributes count. A
  record over a limit keeps its session, usage and cost attributes first and then its smallest
  attributes, whatever order they were sent in. A span status message keeps 1024 bytes.
  `docs/prd/nami.md`, row R6, has the policy in full, the settings and the five points where it
  differs from the attribute limits OpenTelemetry defines for SDKs.
- No token or password is stored. Every string nami keeps (attribute keys and values on every
  carrier, log bodies, names, status messages) first passes one credential stage, whatever key or
  field carries it. The stage replaces what it recognises with `[REDACTED]` and leaves the rest of
  the string: user information, query and fragment of a URL, each with `[REDACTED]` in its place;
  the value assigned to a credential name (`password=`, `token:`, `api_key=`, `secret=`,
  `Authorization:`); the value of a command-line flag with such a name (`--password x`,
  `--api-key=x`) and the password of `curl -u`, `mysql -p…` and `sshpass -p`; `Bearer` and `Basic`
  values; JSON Web Tokens; tokens with a known prefix (`sk-`, `ghp_`, `github_pat_`, `glpat-`,
  `xoxb-`, `xoxp-`, `AKIA`); and the whole value of an attribute or JSON member whose own name is a
  credential name (`http.request.header.authorization`, `"password": ...`), a number included. A user
  name or user id is no credential and stays. A string that is a JSON
  document is redacted leaf by leaf and stays JSON. The stage recognises shapes: a credential of no
  such shape is not found, so it lowers the risk and is no guarantee. It runs at ingest, so
  `logs_search` cannot find text it removed.

### What one export request may hold

nami stores telemetry as it was sent. What it bounds is what one request may cost it (nami receiver policy; compare
the SDK limits in the OpenTelemetry Common Specification). A request over a bound of the first table is refused
whole and nothing of it is stored: OTLP/HTTP answers `413` with an OTLP `Status` that names the bound, OTLP/gRPC
answers `RESOURCE_EXHAUSTED` without `RetryInfo`. By OTLP an exporter does not send such a request again. Protobuf that no OTLP exporter writes (a group,
a tag or length longer than it needs to be) is refused with `400` or `INVALID_ARGUMENT`, and fields OTLP does not
define are read past and not kept.

| Bound of one request | Default | Setting |
| --- | --- | --- |
| Bytes on the wire, compressed or not | 16 MiB | `NAMI_INGEST_MAX_REQUEST_BYTES` |
| Bytes after decompression | 16 MiB | `NAMI_INGEST_MAX_DECOMPRESSED_BYTES` |
| Protobuf messages of every type: containers, records, attributes and their values | 50,000 | `NAMI_INGEST_MAX_REQUEST_MESSAGES` |
| Records, the messages stored as rows of their own: spans, log records, metric data points, exemplars | 3,000 | `NAMI_INGEST_MAX_REQUEST_RECORDS` |
| Scalar elements: over protobuf the elements of repeated scalar fields such as histogram bucket counts, over JSON every string, number, boolean and null | 1,000,000 | `NAMI_INGEST_MAX_REQUEST_ELEMENTS` |

nami ingests one request at a time (`NAMI_INGEST_MAX_CONCURRENT_REQUESTS`, default 1). A request waits for its
turn before its body is read, five seconds at most (`NAMI_INGEST_MAX_WAIT_SECONDS`); then it is turned away with
an answer an exporter retries: `503` with `Retry-After`, or gRPC `UNAVAILABLE`. One request at the record bound
costs nami about 100 MiB of memory while it is stored, so the slots times that is what ingest may take.

An OpenTelemetry SDK's default batch of 512 records is inside the bounds: Claude Code's exports of 15 Nov 2026
hold about 56 messages per span, 57 per log record and 34 per metric data point, so 512 records are about 30,000
messages. An OpenTelemetry Collector in front of nami batches 8,192 records by default, which is over both
bounds, and nami does not split a request. Cap the Collector's batch: `send_batch_max_size: 512` in its batch
processor (or `sending_queue.batch.max_size` of the exporter) keeps every request inside the defaults; with a
larger batch, raise `NAMI_INGEST_MAX_REQUEST_RECORDS` and `NAMI_INGEST_MAX_REQUEST_MESSAGES` for the memory you
give nami.

Inside an accepted request, an attribute value or log body is bounded on its own, and what a bound removes is
counted, not hidden:

| Bound of one value | Default | Setting | Over the bound |
| --- | --- | --- | --- |
| Encoded size of an array or key-value list, and five bytes for each value it holds at any level | 256 KiB | `NAMI_INGEST_MAX_VALUE_BYTES` | the attribute is dropped whole and counted in the record's `dropped_attributes_count` |
| Nesting of arrays and key-value lists | 16 | `NAMI_INGEST_MAX_VALUE_NESTING` | the value below the bound becomes an empty value; the attribute stays |

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
| `src/nami.collector.ingestion` | OTLP decoding over HTTP and gRPC, the credential stage and the size bounds (the two things ingest does to a record), watermarks. |
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

`mise install` installs the pinned toolchain: the .NET SDK `11.0.100` (.NET 11,
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
notice of ajv-formats, whose `duration` pattern `contracts/common/types.tsp` adapts for `IsoDuration`.
