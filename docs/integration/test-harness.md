# Test harness handover

The active implementation is in this repository under `tests/`, built by `nami.slnx` and the existing `eng/` entry points. The experimental Downloads checkout is no longer needed to build, generate, test or run Nami. Closing its IDE window has no effect on this repository.

The 2026-10-25 file-by-file reconciliation is recorded in [test-harness-inventory.csv](test-harness-inventory.csv). Every prototype file is assigned to an implemented Nami owner, an explicit replacement, or a retired concern. The inventory is a migration record, not a list of deferred imports.

## What is retained

| Concern | Nami owner |
| --- | --- |
| In-memory project definition, deferred materialization and package feeds | `Nami.Tests/FixtureProjectDefinition.cs`, `FixtureProject.cs`, `PackageFeedFixture.cs` |
| Per-invocation attribute fixtures and cleanup | `Nami.Tests/ProjectFixture.cs`; `CaptureFixtureTests`, `InvocationContractTests` |
| Process group/job ownership and captured diagnostics | `Nami.Tests/ChildProcess.cs`, `OwnedProcess.cs`, `Fixtures/ProcessHost` |
| Compiler and binary inspection | `Nami.Tests/Compilation`, `Metadata`; integration compiler/PE tests |
| Raw OTLP evidence, isolated receiver and telemetry assertions | `Nami.Tests/Capture`, `OtlpDecode`, `OtlpReceiver`, `TraceEvidence`; Unit/Integration receiver tests |
| Policy outcomes, missing evidence and original test failures | `Nami.Tests/Policies`; `TelemetryPolicyTests` and generated invocation probes |
| Schema, codec and cross-language parity | TypeSpec generator tests, `EncodedPresenceTests`, `EncodedContainerTests`, `ContractIntegrationTests` |
| Real collector/storage and MCP behavior | `IngestStoreTests`, `MetricIdentityTests`, `HostTransportTests`, `NativeConformanceTests` |

## What is not carried forward

The old product SDK, its public host setup, HTTP collector client, package-delivery probes and additional analysis tools are not Nami APIs. Their project files, snapshot packages and parent-checkout preparation scripts are not used. Relevant ingestion, persistence, isolation, exact-value and restart behavior is exercised against the real Nami engine and its six generated tools.

The old standalone definition-conformance API and registry runtime mirror are replaced by Nami's Weaver-generated semantic catalog and its policy/registry tests. This does not add a second runtime registry or recreate the old SDK. Unused wrappers and stub handlers are removed.

## Contract and dependency boundaries

Nami-owned product contracts come from TypeSpec and are consumed through generated C#/TypeScript types and JSON schemas. Weaver remains the semantic-convention authority. Upstream OTLP protocol messages and test-only capture/evidence records have separate roles; they do not define another Nami application protocol.

The C# MCP package is only a test client for the TypeScript server. It is not a C# product MCP implementation. Native publication and MCP/engine framing continue through Nami's existing automation.

No project reference, MSBuild import, package feed, staged application or generator points to the Downloads checkout. Temporary consumers and probes are materialized from sources in this repository. Test-run payloads are removed after success or failure; only an explicit bounded retention option keeps failed-run evidence. See [the lifecycle rules](../testing.md#per-invocation-consumer-projects).

For runnable commands and current diagnostic status, use [testing.md](../testing.md). This handover completes the prototype integration; broader product roadmap items remain governed by the PRD rather than being silently treated as shipped features.
