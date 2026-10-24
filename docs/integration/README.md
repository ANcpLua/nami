# Nami integration decisions

The product requirements are `docs/prd/nami.md`; `docs/checklist.md` owns implementation scope.

## 2026-10-24: repository-local C# union skill

The [C# union skill](../../.agents/skills/csharp-union-types/SKILL.md) is installed locally from
`ANcpLua/csharp-union-types` revision `bb7b90d55f9d2bb039ab853c1d5fffc406b94d9e`. Its references,
examples, verifier, probes, polyfills and license notices are bundled without nested Git metadata.
It covers typed/generic unions, default/null handling and functional Result composition. The
bundled report records 36 successful checks against .NET 11 RC2; that source-repository evidence
is separate from Nami's own build, OTLP, DuckDB and NativeAOT verification.

## 2026-10-24: TypeScript 7.1 Beta

Both contract emitters and the MCP host now pin `typescript` to `7.1.0-beta`, with matching
Bun lockfiles. The installed compiler resolves to the native ARM64 package and reports
`Version 7.1.0-beta`; its embedded revision matches package gitHead
`3e9236361fa97a9e6c685f4833ac457ae2d8c50a`. Compiler API exports now use `typescript/sync`
and related paths instead of `typescript/unstable/*`. The current Nami build uses the CLI,
so no compiler API imports needed changing.

The shared entry-point CPU budget now also sets `GOMAXPROCS`. Contract generation and MCP
build passed with the beta; generated schemas and wire revision are unchanged. The complete
Release gate passed on the isolated upgrade: 26 Unit, 142 Integration and 38 EndToEnd cases
(206 passed, zero failed/skipped), plus NativeAOT publish, registry and contract parity. The
existing DuckDB IL2104/IL3053 and Apple toolchain diagnostics remain. No new test cases were added.

## 2026-10-24: local compiler sources and persistent model knowledge

Preview/beta/RC/dev/nightly and experimental features are explicitly authorized when they serve
the architecture. The owning [model skill](../../.agents/skills/nami-contract-models/SKILL.md)
records absolute local source paths, package/commit versions, executable naming/version-view
recipes and recurring mistakes. It is available through the repository skill directory and a
local Codex skill link. AGENTS.md routes model/compiler work to it.

Local execution confirmed version-mutator snapshots, preservation of the original namespace,
the installed three-argument `resolveEncodedName` signature, and the actual generic JSON metadata
methods in .NET 11 RC2. These observations do not claim production multi-version support. Nami
uses TypeSpec 1.17.0; the subsequent compiler upgrade pins native TypeScript 7.1.0-beta.
The user-mentioned TypeSpec/TypeScript-Go clone paths were not found in the inspected local
locations and were requested; no replacement clone, online lookup or speculative upgrade was made.
The previously downloaded TypeSpec preview packages were retained at a permanent local source path.

## 2026-10-24: reproducible agent inspection

`eng/inspect.sh` owns inspection setup, build context, scope and result interpretation. JetBrains
is pinned as a local tool; the selected SDK must equal `global.json`. Every scope retains the full
solution graph. The default response is bounded and follow-up queries use normalized run artifacts
through the same command. Raw SARIF is wrapper input, not an agent reasoning requirement.

Prepared builds and recorded results carry source fingerprints; reuse rejects changed sources,
toolchains or outputs. Concurrent build/test/inspect commands share a checkout lock, and inspection
rejects edits or external output changes during a run. This cannot prevent manual IDE actions,
but those actions cannot silently produce a valid “clean” result after detected drift. Personal
IDE settings are excluded from the command-line inspection context.

The reported `ArrayValue` errors were a separate source mismatch: metric identity requires the
OTLP protobuf array, not the Nami contract case. That call site now names the protobuf type
explicitly. Inspector host runtime version and the .NET application SDK remain distinct facts.

## 2026-10-24: implemented TypeScript boundary and cleanup

TypeScript MCP now owns the six generated tools and supervises the native engine through private
bounded JSONL streams. The C# MCP server/dependency was removed. Types, schemas, tool descriptions,
protocol limits, configuration names and revision are generated together. The existing 38 EndToEnd
cases pass through the new boundary, including authentication, self-telemetry and snapshot restart.
This does not complete the separate instrumentation catalog or hosted connector delivery.

Dead session/trace-page/metric-query APIs and their unused models were removed; stored OTLP data,
identities and migrations remain. DuckDB.NET was updated to 1.5.6.1; typed native reads replace boxed
numeric conversion. The supplied time-fix patch was applied, with stdio readiness using the actual
engine/MCP handshake. JSON content parity is structural across the language boundary; the reviewed
manifest change is limited to deriving the six descriptions from existing TypeSpec documentation.
See [the saved request and working findings](agent-refactor.md) for scope and verification status.

The full Release gate passed all 188 existing cases (26 Unit, 124 Integration, 38 EndToEnd),
plus NativeAOT publish, registry and generated-contract parity. The full subsequent inspection
completed with zero errors, zero warnings and 262 notes; notes still make its wrapper exit 1.
DuckDB trim/AOT and local Apple toolchain publish diagnostics remain visible. No new test cases
were added. Maintained production/generator source decreased from 16,834 to 15,844 lines against
the saved working-tree baseline; generated artifacts and tests are excluded from that measure.
The final isolated check includes the generated TypeScript model/JSON naming codecs; its schemas
and wire revision remain unchanged by that correction. Model names, JSON names and C# names all
come from the same TypeSpec property, with no hand-maintained mapping table.

## 2026-10-24: revised product scope and architecture

The user's correction supersedes the former single-C#-binary target. MCP belongs to TypeScript/Bun;
OTLP, DuckDB and analysis remain in the C# NativeAOT engine. TypeSpec remains the shared contract
and must generate both C# and TypeScript artifacts. A private long-lived stream/pipe connects the
local processes; neither an internal HTTP analysis API nor a single native file is mandatory.

Tomo is an example Rust developer whose application an agent investigates. The product is not tied
to a service named tomo or exclusively to Rust or Claude Code. Product integrations are a Claude.ai
connector and/or OpenAI plugin with bundled MCP; local agent clients remain supported. The product
has no dashboard or human-facing analysis UI.

Compile-time interception, model manifests and XML-configured observation lifetime are now explicit
requirements. The PRD contains every supplied instrumentation method under I1–I18 and transport/
distribution proofs under D1–D6. C# interception is confined to supported compiled call sites;
other languages need their own instrumentation path. New interceptors require a build. Stateful
observations use explicit project-bound IDs and do not turn MCP 2026-07-28 into a session protocol.

Resolved contradictions: TypeScript output is retained; semantic cross-language equality does not
require JSON property-order equality; Stable-only selection is distinct from allowing Development
models in the registry; instrumented metric calls are validated through aggregates, not one exported
point per call. Public and private project selection derives from authentication. No implicit
fifteen-minute lifetime, CLR-profiler requirement or transparent interception of Rust is claimed.

This is a documentation/acceptance change. The existing C# MCP host and previous proof results are
the pre-migration baseline, not evidence that TypeScript MCP, IPC or the instrumentation catalog is
implemented. The checklist records the outstanding work explicitly. Existing unrelated working-tree
changes are outside this documentation update.

## Earlier implementation decisions (historical baseline)

The entries below describe the previous C# host. Where they conflict with the revised PRD above,
the revised product requirements and the migration checklist take precedence.

- The host is `src/nami/nami.csproj`, assembly `nami`. No arguments serves stateless MCP HTTP;
  `--stdio` serves MCP on stdin/stdout. Both modes expose OTLP and `/health`. This follows the
  checklist's explicit executable behavior; the PRD's shorter local-start paragraph was ambiguous.
- Nami-projected discriminated attribute and metric unions use the PRD's `kind` field and closed
  C# 15 unions. Native OTLP JSON/protobuf encoding and internal persisted storage tags are unchanged.
  Tool results remain direct typed objects, not a new envelope.
- A returned `snapshot_id` identifies an immutable, expiring DuckDB snapshot; a cursor binds to that
  snapshot, tool and argument hash. The schema declares these continuation fields explicitly.
- Self-export uses an ephemeral process-local ingest credential accepted only from loopback and
  scoped to the `nami` project. User credentials retain their configured project and roles.
- The managed Fallout host uses `UseAppHost=false`: its 10.4.0 entry-assembly check otherwise rejects
  the .NET 11 native apphost's command-line path. The application itself remains NativeAOT.
- Registry materialization and upstream-format diagnostics are documented in `1-registry.md`.
- Generated wire contract decisions and compiler proofs are documented in `4-contracts.md`.
- Storage/snapshot and tool behavior are documented in `6-tools.md` and `7-storage.md`.
- `eng/inspect.sh` is the shell entry point for the complete `nami.slnx` inspection. It uses the
  installed JetBrains tool, pins SDK selection through `global.json`, and creates a fresh run
  directory for SARIF and logs. The report validator rejects unsuccessful/missing invocations and
  analyzer-load or source-generator failures even when InspectCode exits zero. It lists every
  diagnostic rule and its count/location; findings return 1, incomplete analysis returns 2.
  The reference telemetry test repository's CPU cap and tool pin are not requirements for Nami.
  Inspection is an explicit target; `Check` retains the real OTLP/DuckDB/AOT proof and verifies
  diagnostic reporting through MSTest integration cases. Successful builds expose
  compiler warnings rather than reducing them to an unqualified `PASS`.


- The requested MSTest-only policy supersedes the earlier PRD choice of a separate console
  conformance executable. `Nami.Tests` owns shared support; Unit/Integration/EndToEnd own the
  executable MSTest projects and their output/deadline profiles. EndToEnd starts the published
  native host, including the former conformance checks. The pre-change 154 executed cases are
  retained; diagnostic regression cases also use MSTest rather than a second test framework.
- `eng/test.sh` is the supported test entry point. Project mode checks evaluated MTP recognition,
  generated restore imports and package-cache files before running; module mode intentionally
  bypasses project evaluation. Fresh TRX reports and per-suite expected minimums supplement the
  original process exit status. MTP exit-code ignore configuration is rejected.
- The twelve tool schemas each evaluate once as a host `EmbeddedResource`; their appearance in
  several tool views is not evidence of duplicate compilation. The generator's extra link to the
  entire `artifacts` directory was removed. Generated outputs and package sources stay available
  to the compiler but are excluded from InspectCode findings; `--project` offers a bounded local
  inspection without claiming full-solution coverage.
- SDK and repository `Version.props` are intentionally separate analyzer inputs. Their IDE links
  now distinguish `Build/SDK` from `Build/Repository`; the host's schemas are grouped under
  `Contracts/Schemas` without changing resource names. The unused Web SDK alias was removed.
  `ANcpLua.NET.Sdk` still supplies analyzer policy, version enforcement and generator defaults for
  the three opting-in projects. Nami's .NET 11 build does not require publishing a new SDK or
  retargeting analyzer assemblies: the analyzer host contract remains separate from the product
  target framework. `ANcpLua.Roslyn.Utilities.Sources` is an explicit generator dependency, while
  the analyzer package bundles its own Utilities assembly; they are not product runtime dependencies.
- The development laptop runs at most two build/tool workers and two MSTest workers within a
  single test module. `eng/scripts/limits.sh` owns the shell entry-point CPU budget. Full InspectCode
  and the full build gate must not overlap with each other or an initial IDE-wide analysis.
  Rider's central DotSettings layer uses low analysis resource utilization, one solution-loading
  process and excludes source-generated files from background monitoring while retaining warnings.
- The root-span transport test selects its own operation when polling `traces_slowest`. Polling
  also emits traces, which otherwise displace the observed call from the default top ten under
  low CPU parallelism. Parent/root assertions and the real native self-export path are unchanged.
- `Nami.Api.Contracts` is an internal, non-packable AOT-compatible assembly. It inherits the
  repository's framework/language settings and keeps reflection JSON serialization disabled.
  Its former public-package metadata and packaging inputs were removed. The emitter no longer
  adds an unreachable default arm after all cases of the closed attribute type; generated sources
  are regenerated from that change and wire schemas/revision remain unchanged.
- A repository call-site audit keeps the contracts assembly's used validation, exact-value
  converters and generated type registry. Five unreferenced convenience entry points were
  removed: explicit-type-info deserialization, array/async serialization, the named round-trip
  wrapper and the JsonNode validation overload. Optional-value converters remain because the
  emitter references them and the PRD explicitly requires their three-state semantics.

- Numeric measurement identity uses `double.IsInteger` for exact integrality, with the inclusive
  `long.MinValue` and exclusive `2^63` bounds retained before conversion. Integral doubles share
  measurement identity with their exact Int64 equivalent; fractions, non-finite values and values
  outside Int64 stay on invariant round-trip formatting. Observation identity still preserves the
  original numeric representation. `MetricIdentityTests` covers both boundaries, adjacent doubles,
  negative zero, non-finite values and absent values.

The build and tests, not unchecked tasks or an older chat status, establish completion.
