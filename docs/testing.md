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

## Current diagnostics: one entry, no report history in the workspace

The repository helpers require Python 3.11+ and mise.

Start with `mise diagnostics` (or `bash ./eng/diagnostics.sh`). It displays the current build,
test and inspection statuses without installing tools or reading raw reports into the agent
context. Agents must use this entry and bounded queries; they must not search old logs/SARIF/TRX
or copy raw diagnostic files into prompts.

Diagnostic state is outside the repository, under the user state directory, keyed by the
checkout's canonical path. Each lane has exactly one `current` directory. A new run removes that
lane's preceding result before writing `RUNNING`; it cannot show an older success as the new run.
`NAMI_DIAGNOSTICS_HOME` can select another external state root; an in-repository path is rejected.
Build outputs remain in `artifacts/bin`, `artifacts/obj` and `artifacts/publish`.
Generated MSTest temporary folders beside those binaries are removed after verification;
the diagnostic workflow fixtures use external scratch space and clean it after every case.

Build/test/inspect entry points remove the legacy workspace diagnostic directories automatically:
`artifacts/inspection`, `artifacts/test-results`, `artifacts/binlogs`, `artifacts/logs` and generated
Fallout logs. These are physically removed, not merely ignored. Instruction trees contain guidance
and source examples, never captured run results. Raw evidence for the current run stays in external
state for tool parsers; older runs are replaced rather than archived.

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
Build success, inspection findings and analysis validity are distinct facts in every summary.
