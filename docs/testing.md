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
| `./eng/inspect.sh` | Full solution analysis; fresh SARIF and tool log, with findings reflected in the exit status. |
| `./eng/inspect.sh --no-build --project Nami.UnitTests` | Focused analysis after a matching build; it does not prove the whole solution is clean. |

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

Inspection writes fresh results under `artifacts/inspection/run.*`. With a successful tool process,
exit 0 means no findings, 1 means findings, and 2 means incomplete/invalid analysis; a failing tool
or build retains its own code. The tool log is checked for loader and generator failures as well
as the SARIF invocation status. Generated outputs, package sources, DLLs and build directories are
excluded from findings. They can still be loaded to resolve symbols and run source generators;
an `Analyzing ...g.cs` line alone does not mean the file is included twice in compilation.

Build diagnostics are printed even on a successful command. `PASS WITH DIAGNOSTICS` preserves the
process result while making compiler, linker and tool messages visible. The quiet build also
streams warnings, test counts and the active-test status while the gate is running. Unique binary logs under
`artifacts/binlogs` retain the build graph and import evidence.
