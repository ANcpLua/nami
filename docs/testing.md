# Build, tests and inspection

Run commands from any working directory by using the script's absolute path, or from the repository
root as shown below. The scripts resolve `nami.slnx`, the SDK in `global.json`, and the installed tools.

| Command | Purpose |
| --- | --- |
| `./eng/test.sh --suite Unit` | Fast in-process contract checks; least console output. |
| `./eng/test.sh --suite Integration` | Real DuckDB, registry/compiler and build-diagnostic checks; failure and skip details. |
| `./eng/test.sh --suite EndToEnd` | Publish the NativeAOT host and exercise its OTLP, HTTP and stdio boundaries; all test results and captured output. |
| `./eng/test.sh` | Build once, prepare the native host, then run Unit → Integration → EndToEnd. |
| `./eng/test.sh --status artifacts/test-results/run.…` | Show which test is still running, using the MTP diagnostic events. |
| `./eng/build.sh Check --Configuration Release` | Required push gate: contracts, registry, all suites and the native host proof. |
| `./eng/inspect.sh` | Build and inspect the full solution; bounded findings and a recorded workspace fingerprint. |
| `./eng/inspect.sh --project nami.collector.storage` | Inspect one project with the full solution reference graph. |
| `./eng/inspect.sh --file src/nami.collector.ingestion/OtlpConverter.MetricIdentity.cs` | Report findings for one file; build and reference context remain solution-wide. |

Only MSTest 4.5.1 and native Microsoft.Testing.Platform are used. `tests/Nami.Tests` is a support
library, not a fourth test application. `Nami.UnitTests`, `Nami.IntegrationTests` and
`Nami.EndToEndTests` each own their `testconfig.json`: cooperative test deadlines are 30, 120 and
300 seconds respectively, with existing test-specific overrides retained, including 600 seconds
for the real 500,000-span proof. Unit enables
MSTest's generated discovery. EndToEnd starts the bundled TypeScript MCP entry and published native `nami` engine;
there is no separate hand-written conformance runner.
The script also sets the .NET test driver's Minimal/Normal/Detailed output and forwards the suite's
result selection explicitly: the driver's display is separate from the test application's config.

`nami.slnx` is the authoritative build scope. A `.slnf` is optional for IDE loading; suite selection
does not require one. MSBuild follows the projects' `ProjectReference` dependencies for build
ordering. Repository commands default to two MSBuild workers and `DOTNET_PROCESSOR_COUNT=2`;
`NAMI_BUILD_WORKERS` explicitly overrides the command budget when needed. The test runner separately
orders the three suites and uses two MSTest workers within each suite. The two registry checks that
write shared generated outputs are explicitly serialized.

In Rider, open `nami.slnx` and explicitly index the `contracts` web project for TypeSpec/emitter
editing. IDE project unloading only changes the editor's working set; the CLI still uses the
complete solution. Deleting `.idea` or running `dotnet clean` does not enable web-project indexing.

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
the test's fixture inputs must still be present. EndToEnd also requires `artifacts/publish/nami`
(`nami.exe` on Windows), `artifacts/publish/mcp/main.js`, and Bun. Keep the expression narrow: support and unselected assemblies are rejected.

## Evidence and failure handling

Every test invocation creates a new `artifacts/test-results/run.*` directory. It contains the exact
commands, evaluated project state, per-suite console logs, MTP diagnostic logs, TRX files and a
`summary.json` with the requested suites, actual test names/counts and final exit status. Follow the
path printed by that invocation rather than selecting an older report. The suite manifest
`eng/test-suites.json` records the expected minimums; update them deliberately when cases change.
If a test produces no console lines for 30 seconds, the runner prints the unfinished case names
from those diagnostic logs. `--status` provides the same view without starting another test run.

MTP's nonzero exit codes are preserved, including 8 for a zero-test module and 9 for an unmet explicit
minimum. The wrapper also validates the fresh TRX results, refuses ignored-exit-code configuration,
and rejects missing reports, failed/skipped results or insufficient execution counts. A filtered
run is recorded as filtered and never substitutes for the complete gate.
The restore-state checks and distinct exit codes follow the
[MTP troubleshooting guidance](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-troubleshooting).

Inspection has one supported entry point: `./eng/inspect.sh`. Agents must not call a global `jb`,
pass a `.csproj` directly to InspectCode, or interpret raw SARIF themselves. The repository-local
`.config/dotnet-tools.json` pins JetBrains; `global.json` pins the required SDK. The wrapper restores
and invokes that local tool, checks the selected SDK exactly, ignores personal IDE inspection
settings, and always supplies `nami.slnx`. The JetBrains host runtime and the application SDK are
separate components; the recorded SDK controls compilation.

A normal run builds the solution in Release with two workers and passes `--jobs=2` to InspectCode.
`--configuration Debug` is explicit.
`--no-build` only accepts a successful build previously prepared by this wrapper with identical
source files, toolchain, configuration and build outputs. A stale or missing preparation fails with
an actionable message. It does not infer success from old DLLs or SARIF files. A common checkout
lock serializes `eng/build.sh`, `eng/test.sh` and `eng/inspect.sh`, including nested gate commands;
`eng/test.sh --status ...` remains available while tests run. Direct IDE/external commands do not
participate in this lock. Source and output hashes before/after inspection detect concurrent edits
or external builds; such a run is invalid, even if InspectCode reported success.

Each inspection prints at most 20 findings by default, with rule, file, line, column and message.
`--limit` accepts 1–200. `--project NAME` selects an exact solution project; `--file PATH` selects a
file or directory and can be repeated. Only a complete solution scope supports a solution-wide
claim. `--rule ID` and `--level warning` filter the displayed findings, not the recorded scope or
exit status. Never describe zero displayed matches as a clean solution.

Use the run path printed by the command to ask follow-up questions without rerunning InspectCode:

```bash
./eng/inspect.sh --results artifacts/inspection/run.ABC123 --rule UnusedMember.Global --limit 10
./eng/inspect.sh --results artifacts/inspection/run.ABC123 --offset 20
```

`ABC123` above stands for the exact run directory printed by your invocation. Replaying a run after
source changes prints `STALE` and exits 2. No implicit “latest report” is used. Each run contains a
compact `summary.json` (context, source hash, status, totals and failures), `findings.jsonl`
(normalized findings), the exact `command.json`, and raw logs/SARIF for debugging the wrapper.
Normal agent work consumes the bounded CLI response and summary, not the raw files.

Exit 0 means a valid clean scoped analysis; 1 means valid findings; 2 means invalid setup, stale
state or incomplete analysis. Nonzero build/tool exit codes are preserved. Read the named status
alongside the code: `SETUP_FAILED`, `BUILD_FAILED`, `ANALYSIS_FAILED` and `STALE` are never clean.
Missing references, Roslyn/generator load failures, compiler errors, missing reports and failed
SARIF invocations invalidate a result even when the tool process exits 0.

Build diagnostics are printed even on a successful command. `PASS WITH DIAGNOSTICS` preserves the
process result while making compiler, linker and tool messages visible. The quiet build also
streams warnings, test counts and the active-test status while the gate is running. Unique binary logs under
`artifacts/binlogs` retain the build graph and import evidence.
# TypeScript/contracts and MCP inspection

These are separate from the MSTest suites and the default .NET InspectCode lane:

```bash
./eng/inspect.sh --target typescript
./eng/inspect.sh --target mcp
./eng/inspect.sh --target mcp --no-build --transport stdio --era modern
```

The TypeScript lane restores frozen lockfiles, checks the native compiler selected by each
package, regenerates contracts and refuses changed generated artifacts, then builds the MCP
bundle. It preserves TypeScript 7.1.0-beta and the TypeSpec version in the package manifests.
The MCP lane additionally publishes the native engine and uses pinned `mcpdo` 2.11.0 against
fresh local state. By default it covers stdio and HTTP in modern and legacy modes, checks all
six advertised tool schemas/descriptions/annotations against generated data, and validates a
`sources_status` read result and its contract revision. It does not claim hosted authentication,
all tool business behavior, or complete MCP conformance; existing E2E suites own those business checks.

`.config/agent-tools.json` pins Bun, Node and mcpdo. Install that exact inspector package when
mcpdo is missing: `npm install --global @modelcontextprotocol/inspector@2.11.0`. A mismatched
tool returns `SETUP_FAILED`; no unpinned fallback is used. `MCPDO_PATH` can identify the installed
JavaScript CLI entry explicitly. The runner creates a private daemon and read-only connection
config, never reads personal catalogs, and tears down its own connections and HTTP host.

`--no-build` requires preparation by the same target/configuration with unchanged inputs,
tool identities and outputs. Ignored TypeScript inputs inside the source roots are fingerprinted
too. Each run writes `summary.json`, `findings.jsonl`, per-command logs/exit codes and generated
artifact manifests under `artifacts/inspection/agent/run.*`. Compiler, schema and protocol
findings have distinct lanes; native build warnings remain in `buildDiagnostics`.

```bash
./eng/inspect.sh --target mcp --results artifacts/inspection/agent/run.EXAMPLE --limit 10
./eng/inspect.sh --target typescript --results artifacts/inspection/agent/run.EXAMPLE --rule TS2322
```

Use the actual run path printed by the entry point. Queries reject changed source/output
fingerprints as `STALE`. Exit 0 is a completed clean lane, 1 is findings, 2 is setup/build/analysis/
protocol failure or stale evidence, and 130 is cancellation. A successful compile is not a
successful protocol check. Pending authorization/elicitation is never counted as a connection.
