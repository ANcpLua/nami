# Build, tests and inspection

Run commands from any working directory by using the script's absolute path, or from the repository
root as shown below. The scripts resolve `nami.slnx`, the SDK in `global.json`, and the installed tools.

| Command | Purpose |
| --- | --- |
| `./eng/test.sh --suite Unit` | Fast in-process contract checks; least console output. |
| `./eng/test.sh --suite Integration` | Real DuckDB, registry/compiler and build-diagnostic checks; failure and skip details. |
| `./eng/test.sh --suite EndToEnd` | Publish the NativeAOT host and exercise its OTLP, HTTP and stdio boundaries; all test results and captured output. |
| `./eng/test.sh` | Build once, prepare the native host, then run Unit → Integration → EndToEnd. |
| `./eng/test.sh --status current` | Show which test is still running, using the MTP diagnostic events. |
| `./eng/build.sh Check --Configuration Release` | Required push gate: contracts, registry, all suites and the native host proof. |
| `./eng/inspect.sh` | Build and inspect the full solution; bounded findings and a recorded workspace fingerprint. |
| `./eng/inspect.sh --project nami.collector.storage` | Inspect one project with the full solution reference graph. |
| `./eng/inspect.sh --file src/nami.collector.ingestion/OtlpConverter.MetricIdentity.cs` | Report findings for one file; build and reference context remain solution-wide. |

The .NET test applications use MSTest 4.5.2 and native Microsoft.Testing.Platform. `tests/Nami.Tests` is a support
library, not a fourth test application. `Nami.UnitTests`, `Nami.IntegrationTests` and
`Nami.EndToEndTests` each own their `testconfig.json`: cooperative test deadlines are 30, 120 and
300 seconds respectively, with existing test-specific overrides retained, including 600 seconds
for the real 500,000-span proof. Unit enables
MSTest's generated discovery. EndToEnd starts the bundled TypeScript MCP entry and published native `nami` engine;
there is no separate hand-written conformance runner.
The test package graph prunes the adapter's optional usage-reporting extension and its Application
Insights dependency. Test discovery, execution and reports remain enabled. Review the prune bound in
`tests/Directory.Packages.props` whenever the MSTest adapter changes its telemetry dependency.
The script also sets the .NET test driver's Minimal/Normal/Detailed output and forwards the suite's
result selection explicitly: the driver's display is separate from the test application's config.

Local engine and test builds select the current SDK runtime identifier, so NuGet copies native
assets for the build host. An explicitly supplied `RuntimeIdentifier` takes precedence. Managed
test assemblies stay `AnyCPU`; the RID selects their apphost and native dependencies. Library
projects outside the test harness remain RID-independent.

Release builds and every build on CI (`CI=true`) treat warnings as errors and fail on NuGet audit
findings (NU1901 to NU1904). `Directory.Build.props` sets this for every project, and
`BuildPolicyTests` checks each C# project of `nami.slnx`. Three codes stay warnings: IL2104 and
IL3053, ILC's NativeAOT summaries of DuckDB.NET.Data's trim and AOT warnings, and NU1900,
vulnerability data that could not be read. Local Debug builds keep warnings as warnings and still
audit.

The shared `Nami.Tests` project uses ingestion's upstream OTLP types but does not reference storage.
Database seeding belongs to `Nami.IntegrationTests/StorageTestData.cs`. Generated BuildEvidence and
Invocation probes reference the support project through ordinary `ProjectReference` items; they
do not copy its complete output directory. Their actual runtime outputs and declared fixture inputs
are collected by `GeneratedFixtures.targets` after the support graph has been built.

`nami.slnx` lists the shared build files as solution items and includes `eng/build/build.csproj`.
Analyzer configurations remain compiler inputs, with explicit virtual links in the project tree.
Local Rider index preferences are separate from solution membership and build dependencies.

`nami.slnx` includes the .NET graph and `src/nami.mcp/nami.mcp.esproj`. The JavaScript SDK version
is pinned in `global.json`; an explicit `<Build />` enables the otherwise non-building JavaScript
solution entry. The project delegates compilation to the existing Bun `package.json`
script. Its restore target uses `bun install --frozen-lockfile`, invalidated by project/package/
lockfile/toolchain changes or removal of `node_modules`. npm installation is disabled.
The solution build now builds MCP directly. `eng/build/Build.cs` also exposes `Mcp` for a standalone
adapter build; `Compile` does not invoke it separately. `PublishNative` includes `mcp/main.js` and `mcp/healthcheck.js`.
The repository entry points resolve the pinned Bun executable. Direct MSBuild/IDE builds need Bun
on PATH or an explicit `BunExecutable` property. C# version-source generation excludes `.esproj`.
A `.slnf` is optional for IDE loading; suite selection does not require one.
MSBuild follows the projects' `ProjectReference` dependencies for build
ordering. Repository commands default to two MSBuild workers and `DOTNET_PROCESSOR_COUNT=2`;
`NAMI_BUILD_WORKERS` explicitly overrides the command budget when needed. The test runner separately
orders the three suites and uses two MSTest workers within each suite. The two registry checks that
write shared generated outputs are explicitly serialized.

In Rider, open `nami.slnx` and explicitly index the `contracts` web project for TypeSpec/emitter
editing. IDE project unloading only changes the editor's working set; the CLI still uses the
complete solution. Deleting `.idea` or running `dotnet clean` does not enable web-project indexing.
A Debug-only IDE build graph lacks the Release-generated symbols, so its cleanup removes imports
that are still needed. Prepare Release and use `--properties=Configuration=Release`; never accept an
unused-import removal that is justified only by an unresolved generated reference.

No checkout outside this repository is required.
The [I1–I18 / D1–D6 evidence matrix](integration/requirement-evidence.md) distinguishes actual
product proofs, test-harness proofs and the remaining product scope. The test counts the gate must
see are in [`eng/test-baseline.json`](../eng/test-baseline.json) only.

## Toolchain versions

The compilers the product is built with, and where each version is set. `mise.toml` installs
Bun 1.4.4, Node.js 24.22.3 and Weaver 0.28.0 for the repository commands, and Node.js is pinned
there only. Bun and Weaver are pinned in more places, which move together with `mise.toml`:

- Bun: the image of the container's MCP build stage (`oven/bun:1.4.4`, `src/nami/Dockerfile:3`),
  and its type definitions (`@types/bun` in `src/nami.mcp/package.json`).
- Weaver: `WEAVER_VERSION` in `registry/generate.py`, which refuses any other `weaver --version`;
  the `weaver` entry of `registry/source-pins.json`; and the installed-tool path in
  `tests/Nami.IntegrationTests/RegistryGateTests.cs`.

| Item | Version | Set in |
| --- | --- | --- |
| .NET SDK | 11.0.100 | `global.json` (`rollForward: disable`) and `mise.toml`, which must agree; the container builds with `mcr.microsoft.com/dotnet/sdk:11.0.100` and runs on `runtime-deps:11.0.0-resolute-chiseled`, the chiseled Ubuntu 26.04 image of the runtime of `DotNetRuntimeVersion` in `Version.props` (the build and runtime stages of `src/nami/Dockerfile`); every base image is pinned by the digest of its multi-architecture index, which `test_toolchain_pins_agree` requires |
| C# language version | 15.0, the SDK's default for `net11.0`; the `netstandard2.0` DuckDB generator sets `latest`, because its target framework would otherwise default to 7.3 | no `LangVersion` in `Directory.Build.props`; `internal/nami.collector.storage.generators/nami.collector.storage.generators.csproj:5` |
| Roslyn inside the SDK | 5.12.0-1.26529.118 | ships with SDK 11.0.100 (`csc -version`); it compiles every project and runs the DuckDB generator |
| Roslyn the DuckDB generator compiles against | 5.9.0 | `RoslynVersion` in `Version.props`, the `Microsoft.CodeAnalysis.CSharp` reference of `internal/nami.collector.storage.generators` |
| Target framework, product, tests and build | `net11.0` | `Directory.Build.props`; `eng/build/build.csproj` |
| Target framework, DuckDB generator | `netstandard2.0` | `internal/nami.collector.storage.generators` |
| TypeScript | 7.1.0-beta | `src/nami.mcp/package.json` and `contracts/package.json`, with their `bun.lock` files |

The generator is built against an older Roslyn API than the compiler that loads it, so it can use
only what Roslyn 5.9.0 offers.

## Project roles and generated fixtures

The four .NET test projects have different responsibilities:

| Project | Responsibility |
| --- | --- |
| `Nami.Tests` | Shared assertions, telemetry capture, process ownership, in-memory compilation and project/fixture infrastructure. It supplies the other projects with helpers. |
| `Nami.UnitTests` | Fast in-process behavior, contract and codec checks, including absent/null values and exact integer representations. |
| `Nami.IntegrationTests` | Real DuckDB, OTLP conversion, identity rules, registry/compiler checks, package consumption and isolated SDK builds. It also exercises static PE inspection. |
| `Nami.EndToEndTests` | The published native engine and the actual TypeScript/Bun MCP adapter, including HTTP/stdio, authentication, snapshots and restarts. |

Nami's MCP implementation is TypeScript under `src/nami.mcp`; the C# executable implements the
private engine. Testing generated .NET consumer projects does not introduce a product SDK or CLI.

Name a new test for the behavior it proves, and seal new concrete test classes.

### Per-invocation consumer projects

[`FixtureProjectDefinition`](../tests/Nami.Tests/FixtureProjectDefinition.cs) holds project properties,
source text, package references and custom targets in memory. Its project document can be inspected
before creating a consumer workspace. [`FixtureProject`](../tests/Nami.Tests/FixtureProject.cs)
creates that workspace when `Materialize()` or an SDK operation is requested. Real `build`,
`restore`, `pack` and `publish` operations require these temporary physical files.

[`UsesFixture<TFixture>`](../tests/Nami.Tests/ProjectFixture.cs) creates and disposes a separate
`FixtureProject` for each test invocation, including each data row. The test accesses it through
`FixtureContext.Current<TFixture>()`. Tests that create a project directly own its lifetime with
`await using`. The fixture writes its staged SDK pin, explicit NuGet configuration and local
`Directory.Build.props`/`targets` files; ambient parent build imports are not part of the consumer.
Its process runner isolates NuGet state and clears inherited CI/runner settings unless the test
explicitly supplies them.

Materialization compares the actual bytes before writing project, configuration and source
files. Unchanged inputs keep their timestamps; missing or altered files are restored from the
current definition. This lets repeated SDK operations use their ordinary incremental build state.

`CaptureFixture` adds the staged sources from `Nami.Tests/Capture` and their package dependencies
to the consumer. The consumer does not reference the test assembly. The
[capture fixture tests](../tests/Nami.IntegrationTests/Projects/CaptureFixtureTests.cs) verify
per-row isolation, cleanup and a published NativeAOT consumer without MSTest or MTP dependencies.

[`PackageFeedFixture`](../tests/Nami.Tests/PackageFeedFixture.cs) owns a temporary NuGet feed for
generated producers and consumers. Reusing an identical package identity and inputs reuses the
published package; changing the inputs under that identity is rejected. Disposing the feed removes
its packages and package/HTTP caches. The first explicitly supplied feed owns both caches,
including when it contains no local packages. Other scratch/plugin directories remain private
to each consumer. Keep the feed alive for the consumers that use it.

Pass `TestContext` explicitly to capture [`FixtureEvidence`](../tests/Nami.Tests/FixtureEvidence.cs)
outside the disposable consumer workspace: `invocation.xml`, `inputs.zip`, `properties.xml`, `output.log`
and, for SDK operations, `build.binlog`. These become MSTest result attachments. Initial/final
MSBuild property snapshots are separated by target framework, and repeated operations receive
separate evidence directories. Diagnose failed builds from this evidence after workspace cleanup.
`FixtureDiagnostics.None` disables this capture even with a context. Without a context, the
fixture does not look up ambient test state and produces no evidence archive, input manifest or
binlog. Exit code, stdout/stderr and requested MSBuild properties remain available. The MSTest
fixture attribute is the framework boundary that passes the current invocation context explicitly.

Intentionally failing invocation probes attach their entire child result directory as a ZIP.
Their parent assertions parse the TRX from that archive; child failures remain observable without
being merged into the host suite as additional test results.

The in-process compiler requires an explicit reference set. Nami's compiler tests use the
framework references resolved by the actual SDK build and recorded in `fixture-defaults.xml`.
Test support, compiler tests and compiler probes use Roslyn from the pinned SDK. They do not
load a bundled .NET 10 reference package or silently add framework references to a compilation.
Create one compilation and reference set per target framework.

Repository test runs own these files through
[`artifact_lifetime.py`](../eng/scripts/artifact_lifetime.py). A passing run removes
its detailed payload. A failed run does the same by default. Only an explicit
`--retain-failure-minutes 60` on `eng/test.sh` keeps a sealed `evidence.zip` for
investigation; 120 minutes from completion is the hard maximum. The small current
summary remains available through `mise diagnostics`. Reading files cannot extend
the deadline, and there is no renewal or unlimited-retention command.

All previous-boot evidence expires after a restart, even if time remained. An
unfinished run is removed after its owner crashes. A live run lease protects work
only before its hard deadline; it cannot override expiry or reboot. Malformed
metadata in the exclusively owned scratch root is removed, not retained as an
exception. `python3 eng/scripts/artifact_lifetime.py sweep --dry-run` shows decisions.

There is no scheduled background cleanup. The test entry point sweeps before and
after execution; `python3 eng/scripts/artifact_lifetime.py sweep` can also be called
explicitly. With optional
retention, expired files remain on disk until that next test run or manual sweep.
Nothing in this policy deletes build outputs, publish directories or folders
outside its dedicated scratch root. Direct IDE test execution retains its existing per-fixture disposal;
the external run-level expiry policy belongs to the repository runner.

When several tests need one resource, use `SharedFixture<T>` for the group,
initialize it once in `ClassInitialize`, and dispose it in `ClassCleanup`. Its
factory runs once, including with parallel consumers. Tests obtain the resource
with `GetAsync`; the group disposes it after its last test. `SharedFixtureTests`
executes ten data rows against one materialized file and verifies its deletion
after the group. Keep per-test `FixtureProject` ownership when independence is
part of the scenario; do not reset shared state between steps that intentionally
test one sequence.

Build-time probes have a separate lifetime. [`GeneratedFixtures.targets`](../tests/GeneratedFixtures.targets)
materializes their `.csproj.template` files below the owning project's intermediate
`generated-fixtures` directory, builds and stages their payloads, and removes them on `Clean`.
These probes are build dependencies, not additional primary test projects in the solution.

## TypeSpec generation and verification

Change model definitions or emitters, then regenerate before building consumers:

```bash
./eng/build.sh Contracts
./eng/contracts.sh verify
./eng/test.sh
```

`eng/test.sh` builds the .NET projects; it does not replace the contract-generation step.
The contract package builds the local emitters and runs its Node.js generator, codec and linter-rule
tests (`contracts/scripts/lib`, `contracts/emitters/nami-lint/test`) and checks the `#suppress`
directives before invoking TypeSpec and the schema/revision scripts. The active TypeSpec compiler and native TypeScript
compiler are pinned in [`contracts/package.json`](../contracts/package.json); the MCP package has
its own package manifest and lockfile.

`mise run lint:tsp` runs the contract lint gate on its own. It compiles without emitting and with
warnings as errors, then fails on any `#suppress` directive that no longer hides a finding. The push
gate enforces the same: `VerifyContracts` runs `eng/contracts.sh verify`, whose `bun run compile`
runs that suppression check before it regenerates anything. The rules, the suppression policy and
the commands are in [`contracts/README.md`](../contracts/README.md#contract-lint).

The model pipeline is:

```text
TypeSpec definitions + output profiles
  -> C# ingestion/storage models and structural mappings
  -> C# rows with DuckDB attributes -> Roslyn generator -> DDL, binders and readers
  -> C# API/engine models and source-generated JSON metadata
  -> TypeScript models, wire codecs, tool associations and JSON schemas
```

[`emit-config.tsp`](../contracts/emit-config.tsp) selects consumer projects, namespaces,
declaration forms, JSON contexts and structural projections. The generated files have distinct owners:

| Location | Consumer |
| --- | --- |
| `src/nami.collector.ingestion/Generated` | Typed values, resource/scope context, metric variants, ingestion records and configuration keys. |
| `src/nami.collector.storage/Generated` | Stored JSON/document forms, ordered DuckDB rows and ingestion/storage/API mappings. |
| `internal/nami.contracts/Nami` | API and private-engine contracts, JSON metadata and tool field-selection metadata. |
| `src/nami.mcp/src/contracts/generated` | TypeScript application/wire types, codecs, schemas, tool associations and shared defaults/configuration metadata. |
| `contracts/json-schema` and `contracts/openapi` | Generated schema artifacts used by validation and contract verification. |

Edit the TypeSpec definitions and emitters rather than these generated files. The
[model manifest](../contracts/generated/model-manifest.json) records emitted representations and
their consumers; the [migration inventory](../contracts/model-migration.csv) tracks the original
model candidates. [`consumer-files.json`](../contracts/generated/consumer-files.json) records the
owned generated C# paths used by cleanup. The contract verifier and inspection runner preserve
the generated consumer directories and metadata during verification or failed generation.

The checks protect the following boundaries:

| Concern | Verification |
| --- | --- |
| Property names, presence, exact bounds and nested codecs | The contract package's generator tests; `EncodedPresenceTests` and `EncodedContainerTests` cover C# missing/null states, Int64 endpoints and invalid representations. |
| Recursive stored/API values | `GeneratedModelTests` copies values and map keys named `type`/`kind`. |
| Metric identity and original observations | `MetricIdentityTests` distinguishes equal measurements from different observations; the existing ingestion/storage suites exercise the generated mappings. |
| DuckDB rows and persisted documents | Integration tests exercise the existing Roslyn-generated SQL helpers, storage, snapshots and schema behavior. Keep column order and identity rules explicit when changing models. |
| Union and closed-hierarchy completeness | `UnionCompilerTests` builds actual generated models and consumer files with the pinned SDK. Added-case tests must fail in those consumers with CS8509; the baseline must compile. The fixture imports the production `WarningsAsErrors` list while leaving blanket warning promotion off. |
| Deprecated semantic-convention definitions | `DeprecatedDefinitions_AndOnlyThey_CarryObsolete` compares `registry/resolved.json` with the generated library: every deprecated attribute key, metric and span has `[Obsolete]` on its constants, and nothing else has it. `Build_UseOfDeprecatedRegistryConstant_FailsUnderProductionPolicy` builds a consumer with the production `WarningsAsErrors` list, which contains CS0618: a use of a deprecated constant fails the build and the error names the successor. Code that must read an old key on purpose keeps it under a narrow `#pragma warning disable CS0618` with a one-line reason. |
| Native union codecs | `Publish_ActualUnionWriters_RoundTripsEveryCaseUnderNativeAot` publishes and executes the actual generated codecs, checking every wire variant, invalid defaults, missing/unknown tags and disabled reflection. |
| Real native serialization and the MCP boundary | `NativeConformanceTests` exercises the published host, resource-entity identity and snapshot replay across restarts; transport tests validate the complete `tools/list` golden. |
| Generation failure and package resolution | `ContractStatusTests` verifies restoration of generated outputs; `InspectionPackageTests` verifies local workspace links and exact external package pins. |

The native resource-entity regression covers a C# 15 lowering failure that ordinary compilation
did not detect. Keep native execution in the verification path when changing generated union
handling or JSON metadata. Preserve the distinct absent/null states, decimal-text integer encoding
and source-generated serialization when extending the codecs.

Canonical ordering, number normalization and identity-field selection remain named algorithms in
the collector. A change to their hash inputs needs explicit identity tests; regenerating models
alone does not prove persistence compatibility.

## Focused and prepared runs

Use `--configuration Debug` when needed. A filter deliberately lowers the expected minimum to one:

```bash
./eng/test.sh --suite Integration --filter 'FullyQualifiedName~BuildDiagnosticsTests'
```

`--no-build` implies no restore. Before a project-based prepared run, the script verifies
`IsTestingPlatformApplication=true`, the evaluated output path, `project.assets.json`, generated
NuGet props/targets and the package files used by that project. A missing restore/import is a setup
failure with its path, not a successful empty test run. `--no-restore` with a build is appropriate
only when the same project, configuration, runtime and output layout have already been restored.

For a test stage containing built modules but lacking project restore state, use module mode:

```bash
./eng/test.sh --suite Unit --test-modules 'artifacts/bin/Nami.UnitTests/release/Nami.UnitTests.dll'
```

Module mode skips project evaluation. Its adjacent `testconfig.json`, runtime dependencies and
the test's fixture inputs must still be present. EndToEnd also requires `artifacts/publish/nami`,
`artifacts/publish/mcp/main.js`, and Bun. Keep the expression narrow: support and unselected assemblies are rejected.

## Test lanes

A test lane is a named group of tests that the gate does not run, such as tests written before their
feature exists or tests that need a live nami. Its `lanes` entry in
`eng/test-suites.json` names the suite the tests live in, the MSTest category that selects them, the lane's
own floor and its own timeout; `eng/test-baseline.json` records each lane's count beside the suites. A normal
run of a suite passes `--filter TestCategory!=<category>` for every lane in that suite and keeps the suite's
floor, so the floors count gate tests only; a user `--filter` is combined with that exclusion. A lane runs
alone against its own floor:

```bash
./eng/test.sh --lane m2-acceptance
./eng/test.sh --lane live-nami
```

Each lane has a Fallout target (`M2Acceptance`, `LiveNami`) that `Check` does not depend on. `check.yml` runs
`m2-acceptance` on manual start (`workflow_dispatch` with `lane`); `live-nami` has no CI job.

| Lane | Category | Tests | Leaves the lane |
| --- | --- | --- | --- |
| `m2-acceptance` | `M2Acceptance` | M2's acceptance tests in `tests/Nami.EndToEndTests/Sessions`, written from M2's acceptance criteria before the tools exist: `sessions_search` and `sessions_get` on the scrubbed Claude Code capture (`tests/Nami.IntegrationTests/Fixtures/ClaudeCodeOtlp/signed-in`), the claim check with planted gate runs, and no content in either tool's answer | When M2 lands and the lane passes: delete the entry, and the EndToEnd floor and baseline grow by its count |
| `live-nami` | `LiveNami` | The live check against a running nami that agent sessions export to (`NAMI_LIVE_MCP_URL`, `NAMI_LIVE_READ_KEY`, `NAMI_LIVE_AGENTS`, `NAMI_LIVE_SINCE`) | Stays: no runner reaches that nami |

The wire names the acceptance tests read are in `Sessions/SessionsContract.cs` only. The capture's bodies are
posted exactly as Claude Code sent them, each checked against `manifest.json`; synthetic variants (planted gate
runs, content sentinels) are decoded copies built in memory by `Nami.Tests/Infrastructure/ClaudeCodeCapture.cs`.
The capture is from 15 Nov 2026, so these tests pass absolute windows and start nami with `NAMI_RETENTION_DAYS=0`.
Two classes need no new tool and run in the gate: `ContentAtIngestTests` (EndToEnd: prompt, response and tool
content is in the stopped engine's store as the session sent it) and `ClaudeCodeCaptureTests` (Integration: the planted
gate runs and the content sentinels are what the acceptance tests say they are).

## Measurements outside the gate

A measurement that depends on the machine is a script, not a test: the gate never asserts resident memory or
wall time. [`a3_rss.py`](../eng/scripts/a3_rss.py) measures what the export of engine audit A3 costs the engine
(one span whose attribute is an array of four million empty values: 7.6 MiB as protobuf, 11.4 MiB as JSON). It
starts the engine as its own process for every run, reads `VmHWM` from `/proc/<pid>/status` when the engine is
idle after one ordinary export and again after the A3 export has been stored, and asks `traces_get` what was
kept and what was counted as dropped. Run it on a quiet machine against a Release build, three runs per encoding
(protobuf over HTTP, JSON over HTTP, gRPC through `curl --http2-prior-knowledge`):

```bash
python3 eng/scripts/a3_rss.py --engine artifacts/bin/nami/release/nami.dll --runs 3 --markdown a3.md
```

[`ingest_bounds_rss.py`](../eng/scripts/ingest_bounds_rss.py) measures the whole path the same way for requests
at the bounds of one request: a chosen number of protobuf messages in one of several compositions (minimal spans,
empty log records, number data points that are one observation or each its own, empty wrappers, spans of the
density Claude Code sends, a fixed number of spans that fill the message bound), sent by one or several clients at
the same moment. It reports the peak over idle, the answers and the stored rows per run. The defaults of the
record bound and of the ingest slots come from it:

```bash
python3 eng/scripts/ingest_bounds_rss.py --engine artifacts/bin/nami/release/nami.dll \
    --messages 50000 --compositions "3000 spans filling the messages" --runs 3 --clients 3
```

What the gate holds instead is the cause, counted in allocated bytes on the test's own thread:
`ValueBoundsTests.AnAttributeOfAMillionEmptyValues_IsNeverBuilt` and the allocation tests of `IngressLimitTests`;
and the bounds themselves: `RequestMessageBoundTests` and `IngestGateTests`.

## Current diagnostics: one entry, no report history in the workspace

The reviewed C# suggestions and the reasons for intentionally retaining particular implementations are recorded in [inspection-review.md](inspection-review.md). Rules remain enabled; a reviewed informational finding is not hidden from the current report.

### Contract generation signals

`./eng/contracts.sh verify` (also `mise contracts:verify`) checks two consecutive generations,
compares their bytes, and restores the original generated files before returning. Use
`./eng/contracts.sh status --limit 20 --offset 0` for bounded details without regenerating.
`mise diagnostics` includes the current contract result. Source changes invalidate a recorded
verification result. `./eng/build.sh Contracts` deliberately keeps a reproducible new generation.
Verification includes the generated ingestion/storage sources and `contracts/generated` metadata,
as well as the API, TypeScript and schema outputs.

| Direct contract command exit | Signal | Meaning |
| --- | --- | --- |
| 0 | `CURRENT` / `GENERATED` | Files were current, or requested generation completed reproducibly. |
| 1 | `GENERATION_REQUIRED` | Reproducible output differs from the working files. Run Contracts, review and commit. |
| 2 | `GENERATION_FAILED` / `SETUP_FAILED` / `STALE` | The check did not establish a valid current result. The original generator exit is recorded separately. |
| 3 | `NONDETERMINISTIC` | Consecutive generations differ; fix the generator. |

The enclosing Fallout build can return its own nonzero exit; the contracts lane retains the
specific signal and direct exit above. Reports and generation logs remain in external current
diagnostic state, never in `.agents` or source directories.

Wire changes compare the canonical generated schema with Git HEAD: `UNCHANGED`, `ADDITIONS`,
`REMOVALS`, `MODIFICATIONS`, `MIXED`, or `UNAVAILABLE` without a baseline. They include changed
JSON pointers and before/after values. Documentation is excluded using the same canonicalization
as the contract revision; a field literally named `description` is still contract data. A schema
addition may tighten validation. Compatibility and quality are therefore `NOT_ASSESSED`, not
automatic "improvement" or "regression" verdicts. Changed wire shape is review information;
stale generated files and unstable/failed generation fail verification.

This schema comparison and the private protocol's `ContractRevision` have different scopes.
[`wire-manifest.json`](../contracts/generated/wire-manifest.json) selects the agreed wire roots;
their self-contained schemas include the needed dependencies. The revision hashes those roots,
not every internal ingestion or storage representation. Keep the generated C# and TypeScript
revision in sync, and review changes to the `tools/list` golden against the intended schema change.

The repository helpers require Python 3.11+ and mise.

Start with `mise diagnostics` (or `bash ./eng/diagnostics.sh`). It displays the current build,
test and inspection statuses without installing tools or reading raw reports into the agent
context. Agents must use this entry and bounded queries; they must not search old logs/SARIF/TRX
or copy raw diagnostic files into prompts.

Diagnostic state is outside the repository, under the user state directory, keyed by the
checkout's canonical path. Each lane has exactly one `current` summary. A new run replaces that
lane's preceding summary with `RUNNING`; it cannot show an older success as the new run.
Test payloads have their own leased directory and bounded evidence lifetime described above.
`NAMI_DIAGNOSTICS_HOME` can select another external state root; an in-repository path is rejected.
Build outputs remain in `artifacts/bin`, `artifacts/obj` and `artifacts/publish`.
Generated MSTest temporary folders beside those binaries are removed after verification;
the diagnostic workflow fixtures use external scratch space and clean it after every case.

Build/test/inspect entry points remove the legacy workspace diagnostic directories automatically:
`artifacts/inspection`, `artifacts/test-results`, `artifacts/binlogs`, `artifacts/logs` and generated
Fallout logs. These are physically removed, not merely ignored. Instruction trees contain guidance
and source examples, never captured run results. Raw evidence stays in external
state while its operation runs. Test payloads follow the explicit bounded retention
rule above; other diagnostic lanes replace their previous record.

The three inspection lanes share the same entry point:

```bash
./eng/inspect.sh --project nami.collector.storage
./eng/inspect.sh --file src/nami.collector.ingestion/OtlpConverter.MetricIdentity.cs
./eng/inspect.sh --target typescript
./eng/inspect.sh --target mcp
```

The C# lane loads `nami.slnx` even for a project/file scope. SDK and executable versions resolve
from `mise.toml`; the SDK must also match `global.json`. JetBrains is pinned in the local
`.config/dotnet-tools.json`. Missing tools require explicit installation; validation does not
silently install another toolchain. Personal IDE settings do not select the inspection policy.

A normal C# run builds the full solution in Release and gives MSBuild and InspectCode two workers.
`--configuration Debug` is explicit. `--no-build` only accepts the wrapper's matching preparation:
source, toolchain, configuration and output fingerprints must agree. The native TypeScript compiler
and TypeSpec checks retain their own compiler findings; the MCP lane separately validates the
published engine/adapter through mcpdo. MCP evidence covers modern/legacy negotiation, stdio/HTTP,
the generated tool catalog and a read call, not hosted OAuth or all business behavior.

Each response prints at most 20 findings by default (`--limit` accepts 1–200). Ask follow-up questions
without rerunning tools:

```bash
./eng/inspect.sh --results current --rule UnusedMember.Global --limit 10
./eng/inspect.sh --results current --offset 20
./eng/inspect.sh --target typescript --results current --rule TS2322
./eng/inspect.sh --target mcp --results current --limit 10
./eng/test.sh --status current
```

There are no historical run paths to choose or infer. Current results still carry their exact
source/configuration fingerprints. After edits they report `STALE`; a new result requires a new
run. An output filter does not narrow the recorded scope or change the result's exit status.
Only a full solution inspection supports a solution-wide claim.
Tracked documentation changes can invalidate this fingerprint too. `STALE` means the record no
longer describes the current checkout; it does not by itself mean a test has newly failed. Keep
the checkout stable during verification and do not rewrite a recorded fingerprint to match later edits.

Exit 0 means a valid clean scoped analysis; 1 means valid findings; 2 means invalid setup, stale
state or incomplete analysis. Nonzero build/tool exit codes are preserved. `SETUP_FAILED`,
`BUILD_FAILED`, `ANALYSIS_FAILED`, `PROTOCOL_FAILED`, `CANCELLED` and `STALE` are never clean.
Missing references, Roslyn/generator failures, compiler errors, missing reports and failed SARIF
invocations invalidate a result even if the underlying tool returned zero.

All entry points share a checkout lock, including nested gate commands. Read-only status remains
available. Direct IDE actions cannot be locked by these scripts, so before/after fingerprints
also reject concurrent source or output mutations. Do not bypass a busy lock.

Tests remain native MSTest/MTP. Fresh TRX is validated by the runner before it records the current
summary. A zero-test, failed or skipped run cannot pass; filtered runs cannot replace the full gate.
The one exception holds on a machine that is not Linux: a test skipped with a reason that ends in
`Linux only` (it reads `/proc`, or needs `/dev/shm`) counts there as run, for the skip rule and for
the suite floor. On Linux every skip fails the run.
Build success, inspection findings and analysis validity are distinct facts in every summary.

## Agent control verification

`mise agent -- proof` checks the Lean transition policy. `AgentControlTests` in the existing
Integration suite verifies the actual filesystem and TypeSpec adapters. The policy is pinned by
`lean-toolchain`; install that exact toolchain explicitly through elan when absent. The CI job
installs it before the gate. The user-facing workflow and proof boundary are documented in
`contracts/agent-control/README.md`. Compiler/assembler/linker tools retain their own semantics;
the controller coordinates them and never treats a successful state transition as a proof of
arbitrary generated machine code.
