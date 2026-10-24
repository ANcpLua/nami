# nami implementation checklist

Ten workstreams write in parallel; each owns the paths listed under it and touches nothing else. Tests are
written later, sequentially, by one agent; nobody in this round writes tests. The build gate at the end is
`dotnet build nami.slnx -c Release` plus `weaver registry check`. What one workstream needs from another is
written to `docs/integration/<workstream>.md` (one file per workstream, append-only), never fixed in
someone else's path.

Source of truth for the product: `docs/prd/nami.md` (German). Source of the code: qyl commit `ecdfb564`,
already copied into this tree with `qyl → nami` renamed mechanically (`Qyl.* → Nami.*`, `QYL_ → NAMI_`).
A read-only checkout of qyl is at `/home/claude/ancplua/qyl` for anything the copy lost.

## Current local continuation

The continuation uses the root of the `nami` checkout on `main`, not the qyl reference checkout.
The Linux `/home/claude/...` paths below describe the original cloud session only.

Registry authoring is now `registry/source/`; `registry/manifest.yaml`, `registry/resolved.json`
and `registry/source-pins.json` are generated canonical publication files. This makes the direct
strict Weaver check work while preserving the exact Core/GenAI sources. Run
`bash registry/generate.sh` to regenerate, or `--check` to compare without editing outputs.

The implementation and sequential test phase are complete. The full Release gate passed with
154 tests, zero failures and zero skipped cases, compiled Rust and byte-identical generators.
Both native binaries passed the six-tool/three-signal conformance proof, including snapshot
replay after a native server restart. See `docs/verification.md` for the requirement evidence.

## Toolchain (installed, use these paths)

| Tool | Path | Version |
| --- | --- | --- |
| dotnet | `/home/claude/tools/dotnet/dotnet` | 11.0.100-rc.2.26504.105 |
| weaver | `/home/claude/tools/weaver` | 0.28.0 |
| bun | `/root/.bun/bin/bun` | 1.x |
| node / npm | `/opt/node22/bin` | 22 |
| cargo / rustc | `/root/.cargo/bin` | stable |

`export PATH=/home/claude/tools/dotnet:/home/claude/tools:/root/.bun/bin:/root/.cargo/bin:$PATH` first.

## Shared decisions (every workstream codes against these)

### Layout

```
nami/
├── nami.slnx                       solution (workstream 8 creates it)
├── global.json                     sdk 11.0.100-rc.2.26504.105, rollForward latestFeature, allowPrerelease true
├── Directory.Build.props/.targets, Directory.Packages.props, Version.props
├── registry/                       Weaver custom registry `nami` (1)
│   ├── manifest.yaml
│   ├── nami/*.yaml                 nami.* attributes and entities
│   └── policies/*.rego
├── templates/registry/csharp/      Jinja templates → internal/nami.semconv (2)
├── templates/registry/rust/        Jinja templates → crates/nami-semconv (3)
├── templates/registry/markdown/    Jinja templates → docs/semconv (3)
├── contracts/                      TypeSpec, C# emitter, JSON-Schema emitter, scripts (4)
├── internal/nami.contracts/        generated C# contract types (4)
├── internal/nami.semconv/          generated C# semconv constants, policy, catalog (2)
├── internal/nami.collector.storage.generators/   DuckDB DDL/row source generator (7, unchanged unless needed)
├── internal/nami.instrumentation*/ self-telemetry defaults and their generator (10)
├── src/nami/                       the one binary: host, MCP server, tools (5, 6, 10)
├── src/nami.collector.ingestion/   OTLP decoding, attribute policy, watermarks (7)
├── src/nami.collector.storage/     DuckDB storage, snapshots (7)
├── crates/nami-semconv/            generated Rust crate for tomo (3)
├── eng/                            Fallout build (8)
├── docs/                           PRD, architecture, checklist, integration notes (9)
└── tests/                          empty this round except tests/Shared
```

### Names

- Target framework `net11.0` everywhere; C# `LangVersion` `preview` (C# 15 unions).
- Root namespaces: `Nami` (host), `Nami.Collector.Ingestion`, `Nami.Collector.Storage`, `Nami.Api.Contracts`,
  `Nami.SemanticConventions`.
- The binary is `nami` (`src/nami/nami.csproj`, `AssemblyName` `nami`, `PublishAot` true). `nami` with no
  arguments starts OTLP (HTTP 127.0.0.1:4318, gRPC 127.0.0.1:4317) and MCP over Streamable HTTP at
  `/mcp` on 127.0.0.1:5100; `nami --stdio` starts OTLP plus MCP over stdio. Environment keys are
  `NAMI_*` (one constant per key in `src/nami.collector.ingestion/Configuration/CollectorConfigurationKeys.cs`).
- Tool wire names (snake_case, exactly these): `sources_status`, `errors_recent`, `traces_get`,
  `traces_slowest`, `logs_search`, `changes_recent`. Every tool: `readOnlyHint: true`,
  `idempotentHint: true`, `destructiveHint: false`, `openWorldHint: false`.
- Project scoping key in the resource: `nami.project.id`. Other nami keys: `nami.attribute.namespace`,
  `nami.attribute.key`, `nami.gen_ai.tool.call.arguments_hash`. Registry annotations: `nami.persisted`,
  `nami.token_exempt`. No `nami.*` literal outside generated files and the registry.

### Contract types (TypeSpec in `contracts/models/tools.tsp`, C# in `internal/nami.contracts`)

Common:

```
model Freshness {            // on every result
  watermark: Timestamp;      // ingest complete up to here, per project and signal (min over the signals read)
  complete: boolean;         // window end <= watermark
  examined: Count | null;
  matched: Count | null;
  returned: Count | null;
  truncated: boolean;
}
model ToolReadInput {        // spread into every input
  service?: string;          // service.name filter
  since?: TimeSpec;          // "-15m" | RFC 3339; default "-60m"
  fields?: string[];         // projection; unknown field → ToolError field/allowed
  max_bytes?: int32;         // default 32768, max 262144; applies to structuredContent
}
```

Per tool (inputs spread `ToolReadInput`; results carry `freshness: Freshness`):

| Tool | Extra input | Result |
| --- | --- | --- |
| `sources_status` | — | `SourcesStatusResult { services: SourceStatus[] }`; `SourceStatus { service, signals: SignalStatus[] }`; `SignalStatus { signal: "traces"\|"metrics"\|"logs", kind: "active"\|"silent", last_seen?: Timestamp, silent_for?: Duration }` |
| `errors_recent` | — | `ErrorsRecentResult { groups: ErrorGroup[] }`; `ErrorGroup { error_type, operation, first_seen, last_seen, count, example_trace_id, source: "log_event"\|"span_event"\|"span_status" }` |
| `traces_get` | `trace_id: TraceId` (required) | `TracesGetResult { trace: Trace }` projected to `fields` |
| `traces_slowest` | `operation?: string`, `limit?: int32` (default 10, max 100) | `TracesSlowestResult { traces: SlowTrace[] }`; `SlowTrace { trace_id, operation, duration_ms, started_at, status }` |
| `logs_search` | `query: string` (required, literal match) | `LogsSearchResult { records: LogHit[] }`; `LogHit { timestamp, severity, body, trace_id?, span_id?, service }` |
| `changes_recent` | — | `ChangesRecentResult { changes: Change[] }`; `Change { observed_at, service, kind: "revision"\|"version"\|"environment"\|"pipeline", from?: string, to: string, source_key }` |

Errors: `ToolError { code, message, retryable, field?, allowed?, hint?, resource_type?, resource_id? }`
(kept from qyl `contracts/common/errors.tsp`). `DataUnavailable` for profiles/baggage is a `ToolError`
with `code: "data_unavailable"`.

C# namespaces for the generated types: `Nami.Api.Contracts.Tools` (inputs/results), `Nami.Api.Contracts.Common`
(Freshness, ToolError, Timestamp, Count, TimeSpec). Serialization only through `ContractJson` and the
generated `JsonSerializerContext`.

### Generated semconv API (`internal/nami.semconv`, namespace `Nami.SemanticConventions`)

```csharp
public static class RegistryPolicy  { bool IsPersisted(string key); bool IsTokenExempt(string key); }
public static class AttributeStability { Stability Of(string key); }  // enum Stability { Stable, Development, Deprecated, Unknown }
public static class DeprecatedAttributes { string? SuccessorOf(string key); } // null = drop
public static class RegistryInfo { const string CoreSchemaUrl, GenAiSchemaUrl, NamiSchemaUrl; const string WeaverVersion; }
// plus one static class per namespace with string constants, e.g. HttpAttributes.RequestMethod = "http.request.method",
// NamiAttributes.ProjectId = "nami.project.id", and nested value classes for enums (HttpAttributes.RequestMethodValues.Get).
```

Metric and span definitions: `Metrics.HttpServerRequestDuration { Name, Unit, Instrument }`,
`Spans.HttpServer { Kind, RequiredAttributes }` — generated, same project.

### Analysis API (`src/nami/Tools`)

Every tool is a pure function over a snapshot plus a thin shell:

```csharp
internal static class SourcesStatusTool
{
    public static Result<SourcesStatusResult, ToolError> Run(ISnapshot snapshot, SourcesStatusInput input, ToolClock clock);
}
```

`ISnapshot` (in `src/nami.collector.storage/Snapshots/ISnapshot.cs`, workstream 7) exposes read-only queries
over one consistent DuckDB snapshot: spans, logs, metrics, resources, watermarks, last-seen; the same
snapshot id gives the same answer. `Result<T, E>` lives in `src/nami/Tools/Result.cs` (workstream 6).

## Workstreams

### 1. Registry — owns `registry/`, `weaver.yaml` (root)

- [x] `registry/source/manifest.yaml`: name `nami`, `stability: development`, `schema_url`
      `https://ancplua.github.io/nami/schemas/0.1.0`, dependencies core `v1.44.0`
      (`https://opentelemetry.io/schemas/1.44.0`, `https://github.com/open-telemetry/semantic-conventions.git@v1.44.0[model]`)
      and genai `9ffc7e00c639be7712e544e3e54331e885dccf84`
      (`https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev`, `...semantic-conventions-genai.git@9ffc7e00c639be7712e544e3e54331e885dccf84[model]`).
- [x] `registry/source/nami/attributes.yaml`: `nami.project.id`, `nami.attribute.namespace`, `nami.attribute.key`,
      `nami.gen_ai.tool.call.arguments_hash` with briefs, types, stability development, and annotations
      `nami.persisted` / `nami.token_exempt`.
- [x] `registry/source/nami/entities.yaml`: `entity.nami.project` (identity `nami.project.id`).
- [x] Imports of the core and genai groups nami stores (http, db, exception, gen_ai, mcp, session, vcs, cicd,
      deployment, service, telemetry).
- [x] `registry/policies/*.rego`: every `nami.*` key must carry a brief and a stability.
- [x] `weaver registry check --future -r registry` exits 0 (run with the tool path above); write the exact
      command and output summary to `registry/README.md`.
- [x] If the nested dependency (nami → genai → core) resolves core twice, record it in
      `docs/integration/1-registry.md` and import core only through genai.
- [x] `registry/CHANGELOG.md` with the two pins and the resolved commit ids.

### 2. C# templates — owns `templates/registry/csharp/`, `internal/nami.semconv/`

- [x] `templates/registry/csharp/weaver.yaml` + `*.j2` producing the API under "Generated semconv API"
      into `internal/nami.semconv/Generated/*.g.cs`; `Nami.SemanticConventions.csproj` (net11.0, no package refs).
- [x] Learn the v2 context shape from `weaver registry json-schema -j materialized-registry-v2`.
- [x] Until workstream 1 is green, generate against core alone (`-r https://github.com/open-telemetry/semantic-conventions.git@v1.44.0[model]`)
      to validate the templates; the final run uses `-r registry --v2`.
- [x] Deterministic output: sorted keys, no timestamps, LF line endings; running generation twice is byte-identical.
- [x] `dotnet build internal/nami.semconv/Nami.SemanticConventions.csproj` compiles.
- [x] Write the generation command to `internal/nami.semconv/README.md`.

### 3. Rust and markdown templates — owns `templates/registry/rust/`, `templates/registry/markdown/`, `crates/`, `docs/semconv/`

- [x] `crates/nami-semconv`: `Cargo.toml` (edition 2021, no deps), generated `src/lib.rs` with `pub const`
      string keys per namespace (`pub mod http { pub const REQUEST_METHOD: &str = "http.request.method"; }`),
      metric names/units, and `pub mod nami`.
- [x] `cargo build` in the crate succeeds.
- [x] Markdown templates render one page per namespace into `docs/semconv/` with stability per attribute.
- [x] Same determinism rule as workstream 2.

### 4. Contracts — owns `contracts/`, `internal/nami.contracts/`

- [x] Remove every model and route nami does not have: the 24 qyl tools, writes (annotations, saved queries,
      failure status), sessions, runner, streaming, metrics query, health routes other than `/health`.
- [x] Add `contracts/models/tools.tsp` with the shapes under "Contract types"; keep `common/errors.tsp`,
      `common/types.tsp`, `common/telemetry-query.tsp` (rename `ToolCoverage` → `Freshness` with the new fields),
      `otel/*.tsp`.
- [x] Keep the C# emitter and the lint emitter; the ts-types emitter is deleted. Add a JSON-Schema emission
      (`@typespec/json-schema`) for the six tool inputs/outputs into `contracts/json-schema/`.
- [x] `bun install && bun run build` (or the script names in `contracts/package.json`) regenerates
      `internal/nami.contracts` and the JSON schemas; commit the outputs.
- [x] `dotnet build internal/nami.contracts/Nami.Api.Contracts.csproj` compiles.

### 5. Host and MCP server — owns `src/nami/Program.cs`, `src/nami/Mcp/`, `src/nami/Hosting/`, `src/nami/nami.csproj`

- [x] `nami.csproj`: net11.0, `PublishAot`, references ingestion, storage, contracts, semconv; package
      `ModelContextProtocol` (2.x) and `ModelContextProtocol.AspNetCore` — find the current versions on NuGet.
- [x] One Kestrel host: OTLP HTTP + gRPC endpoints from qyl's ingestion, MCP Streamable HTTP at `/mcp` in
      stateless mode (`HttpServerSessionMode.Stateless`), `--stdio` for stdio transport.
- [x] Tool registration without reflection: a static table built from the contracts (name, description,
      input JSON schema from `contracts/json-schema/`, annotations); no `WithTools<T>()`.
- [x] Result shaping: `content[0]` headline text, `content[1]` the JSON, `structuredContent` the same object,
      `max_bytes` applied to `structuredContent` with `truncated` and `next_cursor`.
- [x] `tools/list` deterministic order, `ttlMs` and `cacheScope` set.
- [x] `_meta.traceparent` extracted into the tool `Activity`; missing `_meta` starts a root.
- [x] Protocol errors as JSON-RPC errors; `ToolError` as `isError` results.

### 6. Tools — owns `src/nami/Tools/`, `src/nami/Analysis/` (adapt qyl's AnalysisEngine, delete what nami does not use)

- [x] `Result.cs`, `ToolClock`, `TimeSpec` parsing (`-15m`, RFC 3339) into an absolute half-open window
      `[start, now)` capped at the watermark.
- [x] Six pure tools under "Analysis API", built from qyl's `AnalysisEngine.*`:
      `sources_status` ← `check_telemetry_health`; `errors_recent` ← `triage` + `get_failure_context`;
      `traces_get` ← `get_trace`; `traces_slowest` ← `list_traces` sorted by duration + `aggregate_telemetry`;
      `logs_search` ← `search_logs` (literal match only); `changes_recent` ← `list_releases` + `ci_log`
      from resource attribute changes (`vcs.ref.head.revision`, `service.version`, `deployment.environment.name`, `cicd.*`).
- [x] Freshness on every result; silence is a finding (`kind: "silent"`, `silent_for`).
- [x] Errors grouped from both sources: log records with `event_name == "exception"` and span events named
      `exception`, plus span status `ERROR` without either.
- [x] No raw dump: `errors_recent` carries no stacktrace; full spans only via `traces_get` with `fields`.
- [x] Delete the qyl analysis operations nami does not expose; keep shared helpers.

### 7. Ingestion and storage — owns `src/nami.collector.ingestion/`, `src/nami.collector.storage/`, `internal/nami.collector.storage.generators/`

- [x] Replace the NuGet semconv references (`Nami.Telemetry.SemanticConventions*`) with a project reference
      to `internal/nami.semconv`; replace `CollectorSemanticAttributeCatalog.g.cs` with the generated
      `RegistryPolicy` / `AttributeStability` / `DeprecatedAttributes` (delete the old generated file and the
      `eng/config/collector-semantic-policy.json` dependency; keep the sanitizer limits 128 Ki / 32 KiB).
- [x] Watermark per (project, signal), monotone, persisted; `last_seen` per (project, service, signal) from
      receipt time, not record time.
- [x] Deprecated keys rewritten to their successor on ingest (`DeprecatedAttributes.SuccessorOf`), original
      key kept only in `nami.attribute.key` discovery rows.
- [x] Stability mark stored next to development keys (column or side table) so tools can report it.
- [x] `ISnapshot` under "Analysis API" with the queries the six tools need; snapshot id stable for the same data.
- [x] Remove write paths (annotations, saved queries, failure status, delete) and the REST analysis routes.
- [x] `dotnet build` of both projects compiles (semconv stub acceptable until workstream 2 lands).

### 8. Build and CI — owns `eng/`, `nami.slnx`, `global.json`, `Directory.*.props/targets`, `Version.props`, `.github/`, `mise.toml`

- [x] `nami.slnx` listing the projects above; `global.json` as in "Layout".
- [x] `Version.props`: `NamiVersion` 0.1.0; drop every qyl-only version property (Api.Sdk, Cli, Orchestration,
      Workload, Telemetry packages); keep DuckDB 1.5.6, Grpc, Protobuf, OpenTelemetry, Fallout, MSTest versions.
- [x] Fallout targets: `Registry` (weaver check + generate + `git diff --exit-code`), `Contracts`,
      `Compile`, `Check` (= Registry + Contracts + Compile; tests join later), `Publish` (NativeAOT).
      Delete ApiSdk, Cli, Mcp, Auth0, release targets.
- [x] `.github/workflows/check.yml`: install .NET 11 RC2 (`dotnet-install.sh --channel 11.0 --quality preview`),
      weaver 0.28.0 from GitHub releases, bun; run `./eng/build.sh Check --Configuration Release`.
- [x] `mise.toml` pins dotnet 11.0.100-rc.2.26504.105, bun, node, weaver.

### 9. Docs — owns `README.md`, `AGENTS.md`, `docs/architecture.yaml`, `docs/integration/README.md`, `claude/`

- [x] `README.md`: what nami is (from the PRD, in English), connect (stdio / Streamable HTTP), send telemetry
      (OTLP ports), the six tools, build and gate, license.
- [x] `AGENTS.md`: layout, gate command, environment keys, the rule "decisions are made by the building
      agent and recorded in `docs/prd/nami.md`; objections go in a comment".
- [x] `docs/architecture.yaml` rewritten for one binary, two surfaces, one contract; delete qyl deployment
      details; add Railway service `nami` (one service, two ports).
- [x] `claude/debugging-tomo.md`: fix the tool names to snake_case and note that the spec language is
      TypeSpec (decision 1 in the PRD); leave the rest.

### 10. Self-telemetry and deployment — owns `src/nami/Telemetry/`, `internal/nami.instrumentation*/`, `src/nami/Dockerfile`, `.railway/`

- [x] nami's own spans/metrics/logs use the `mcp.*` conventions from the genai registry
      (`mcp.method.name`, `gen_ai.tool.name`, `mcp.session.id` absent in stateless mode) and export into its
      own store (loopback OTLP) under `nami.project.id = "nami"`.
- [x] nami's own errors: span event plus `ILogger` record with `exception.*`.
- [x] `src/nami/Dockerfile`: NativeAOT publish, two exposed ports (4318 OTLP HTTP, 5100 MCP), `/health`.
- [x] `.railway/railway.ts`: one service `nami`, volume `/data`, healthcheck `/health`.
- [x] `internal/nami.instrumentation*`: keep what the host uses, delete the rest.

## Integration (done by the orchestrator after all ten report)

- [x] `dotnet build nami.slnx -c Release` green; fix cross-workstream mismatches from `docs/integration/*.md`.
- [x] `weaver registry check --future -r registry` green; regenerate; `git diff --exit-code`.
- [x] One commit on `main`: "nami: first implementation from the PRD".

## Handoff: status and how to resume

This section lets anyone — a fresh Claude session, Codex on a laptop, a human — continue without the
conversation that produced this file. Everything above is the plan; this is the state.

### Commits on `main` so far

| Commit | What |
| --- | --- |
| `e025215` | `claude/debugging-tomo.md`: tomo's requirements towards nami (Opus-written) |
| `93b68ec` | Scaffold: qyl `ecdfb564` collector, contracts, build, props copied and renamed `qyl → nami`; `docs/prd/nami.md`; this checklist |
| `c259e79` | `registry/manifest.yaml`, `registry/source/nami/attributes.yaml`, `registry/source/nami/entities.yaml` (workstream 1, partial) |
| `db7b587` | `README.md` draft (workstream 9, partial) |

Everything else in the workstream sections above is unstarted unless a later commit says otherwise.
Progress is visible in the tree, not in this file: a workstream is done when its "owns" paths exist and
its listed commands pass.

### Resume on any machine

```bash
git clone https://github.com/ANcpLua/nami && cd nami
# toolchain: .NET 11 RC2 (dotnet-install.sh --channel 11.0 --quality preview), weaver 0.28.0
# (https://github.com/open-telemetry/weaver/releases/tag/v0.28.0), bun 1.x, node 22, cargo stable
git clone --depth 1 https://github.com/ANcpLua/qyl /tmp/qyl   # read-only reference, commit ecdfb564
```

Then start one agent per workstream with the prompt below, all in parallel, each confined to its "owns"
paths. No tests in this round. The orchestrator (whoever starts the agents) integrates afterwards:
`dotnet build nami.slnx -c Release`, `weaver registry check --future -r registry`, fix what
`docs/integration/*.md` reports, one commit.

### Agent prompt (common preamble, then the workstream line)

> You are workstream N of ten parallel agents implementing `nami` in the repo at `<repo path>`. Read
> `docs/checklist.md` first (all of it), then your section "### N. <title>". The product spec is
> `docs/prd/nami.md` (German). A read-only checkout of qyl at commit `ecdfb564` is at `<qyl path>` for
> anything the copy lost. Rules: you own only the paths listed under your section; do not edit any other
> path; do not write tests; use the toolchain paths from the checklist. Anything another workstream must
> change for you goes into `docs/integration/N-<name>.md` (create it). Try to keep your own projects
> compiling; where other workstreams' types are missing, stub minimally inside your own paths and say so in
> your integration file. Budget about 30 minutes. Finish by reporting in at most 15 lines: what exists,
> which exact commands pass, open issues.

Workstream-specific additions (append to the preamble):

1. Registry: Weaver docs for custom registries are the `manifest.yaml` section of the Weaver documentation
   ("Define your own telemetry schema"). Work until `weaver registry check --future -r registry` exits 0;
   write the passing command and a diagnostics summary to `registry/README.md`.
2. C# templates: learn the v2 context with `weaver registry json-schema -j materialized-registry-v2`;
   until the registry is green, generate against core alone
   (`-r https://github.com/open-telemetry/semantic-conventions.git@v1.44.0[model]`); the generated API is
   exactly the one under "Generated semconv API"; deterministic output; `dotnet build` the semconv project.
3. Rust and markdown: same v2 context and core-only fallback; the crate has no dependencies and must
   `cargo build`; one markdown page per namespace with stability per attribute.
4. Contracts: cut to the six tools under "Contract types"; `Freshness` replaces `ToolCoverage`; keep
   `ToolError`, the OTel projections, `ContractJson`, `Timestamp`, `OptionalValue`, `JsonInteger`; keep the
   C# and lint emitters, drop ts-types; emit JSON Schema per tool into `contracts/json-schema/<tool>.input.json`
   and `<tool>.result.json`; regenerate `internal/nami.contracts` and `dotnet build` it. Produce the C#
   types early; workstreams 5 and 6 code against them.
5. Host and MCP: official C# MCP SDK v2 (`ModelContextProtocol`, `ModelContextProtocol.AspNetCore`,
   docs https://csharp.sdk.modelcontextprotocol.io/v2/); stateless Streamable HTTP at `/mcp` in the same
   Kestrel host as OTLP, `--stdio` for stdio; tool table from the JSON schemas, no `WithTools<T>()`; result
   shaping, `max_bytes`, sorted `tools/list` with `ttlMs`/`cacheScope`, `_meta.traceparent`; delete REST
   analysis and write endpoints.
6. Tools: six pure tools per "Analysis API", derived from qyl's `AnalysisEngine.*` as mapped in section 6;
   `Result.cs`, `ToolClock`, `TimeSpec` (`-15m`, RFC 3339 → `[start, now)` capped at the watermark);
   freshness on every result; silence is a finding; errors from log event, span event and span status;
   no stacktrace in `errors_recent`; literal `logs_search`.
7. Ingestion and storage: ProjectReference to `internal/nami.semconv` instead of the semconv NuGet packages;
   delete the old generated catalog and its policy JSON; watermark per (project, signal) and `last_seen`
   from receipt time; deprecated-key rewrite; stability mark; `ISnapshot` with the queries the six tools
   need; remove write paths and REST routes; keep both projects compiling.
8. Build and CI: `nami.slnx`, `global.json` (11.0.100-rc.2.26504.105), `Version.props` with only nami's
   versions, Fallout targets Registry/Contracts/Compile/Check/Publish, `.github/workflows/check.yml`,
   `mise.toml`; delete qyl-only targets; `./eng/build.sh --help` lists the targets.
9. Docs: `README.md`, `AGENTS.md`, `docs/architecture.yaml` (one binary, two surfaces, one Railway
   service), `docs/integration/README.md`; `claude/debugging-tomo.md` tool names to snake_case plus a note
   that the spec language is TypeSpec (PRD decision 1).
10. Self-telemetry and deployment: `mcp.*` conventions for nami's own spans/metrics/logs exported over
    loopback OTLP under `nami.project.id = "nami"`; own errors as span event plus `ILogger` record with
    `exception.*`; `src/nami/Dockerfile` (NativeAOT, ports 4318/4317/5100, `/health`); `.railway/railway.ts`
    with one service `nami`; trim `internal/nami.instrumentation*` to what the host uses.

### After this round

Tests are written sequentially by one agent from the PRD's test catalog (`docs/prd/nami.md`, section
"Testmethoden-Katalog"): `Nami.Registry.Tests`, `Nami.Collector.Tests`, `Nami.Conformance`. That agent
also replaces every stub left in `src/nami/Tools/Pending/` and any `#if NAMI_SEMCONV_STUB` fallback.

### Round 1 ended at the weekly usage limit

Round 1 ran from 01:26 to about 02:20 on 2026-10-24 under a platform incident (elevated errors, status page
`h101vcmhnfky`) and then hit the account's weekly limit (HTTP 429; the API confirmed the agents ran as
`claude-opus-5-5`). The limit resets on 2026-10-25 at 15:00 Europe/Vienna. The ten agents keep their context in
the Claude session `claude-74`; after the reset they can be resumed with a message each, or round 1 can be
relaunched from the prompts above with any agent runtime. Everything written so far is on `main` (last commit
of round 1: `2863f76`).
