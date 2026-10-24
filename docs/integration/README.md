# Nami integration decisions

The product requirements are `docs/prd/nami.md`; `docs/checklist.md` owns implementation scope.

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
