# Agent-operated application debugging with nami

The normative product specification and full acceptance catalog are [the PRD](../docs/prd/nami.md).
This document corrects the original example: **Tomo is a Rust developer who asks an agent to debug
his application using nami**. He is not a required service, protocol participant or sole consumer.
The same workflow applies to other supported applications and languages.

## Product access

Agents call the same six read-only tools through a Claude.ai connector and/or an OpenAI plugin with
bundled MCP integration. Local MCP clients, including Claude Code, are also supported. Developers
configure and authorize the integration; there is no human-facing analysis dashboard.

## Target stack

- TypeScript/Bun owns MCP, agent result framing, connector authentication and the local engine launcher.
- C#/.NET 11 NativeAOT owns OTLP ingestion, DuckDB and snapshot-based analysis.
- Private long-lived streams/pipes connect these processes locally; no internal HTTP API is required.
- TypeSpec generates both language contracts and JSON Schemas. Weaver owns pinned semantic definitions,
  stability and persistence policy, including generated artifacts for supported Rust applications.
- A common release carries both processes and their declared dependencies. It need not be one native file.

The existing C# MCP host is the pre-migration implementation. [The checklist](../docs/checklist.md)
records the pending TypeScript, IPC, instrumentation and distribution work; this document does not
claim those changes have already shipped.

## Instrumentation

For supported C# call sites, the compiler binds generated wrappers at build time. During execution,
the compiled wrapper uses the pinned telemetry contract, observes the original operation, and sends
its telemetry to the configured pipeline. Runtime code enforces XML-configured observation lifetime.
New generated interceptors require a new build; a skill can orchestrate that build or invoke existing
capabilities, not add compiled instrumentation to an already published binary.

Coverage must be explicit. C# interception does not instrument arbitrary internals of precompiled
libraries or Rust code. Rust applications use appropriate Rust instrumentation such as `tracing` and
OTLP while sharing the declared semantic definitions.

Observation state is identified explicitly and isolated by project, independent of MCP connections.
No hidden fifteen-minute cap applies. Request deadlines, inactivity, observation lifetime, retention
and export delay are separate rules. All declared models and supported call forms require the PRD's
I1–I18 proofs, including exact-once semantics, async/enumeration lifetime, parallel context isolation,
signal shape, sampling, metric aggregation, export loss and shutdown accounting.

## Agent investigation

1. `sources_status`: check whether the application is sending each signal; silence is a finding.
2. `errors_recent`: inspect error groups and obtain a correlated example trace ID.
3. `traces_get`: inspect that trace with explicit field projection.
4. `traces_slowest`: inspect the slow tail in the chosen window.
5. `logs_search`: find literal text without regex or case folding.
6. `changes_recent`: correlate observed resource revisions and deployment metadata.

Every result carries bounded data and freshness. An empty incomplete result never proves absence of
errors. Recording, export acceptance, persistence and query visibility are distinct pieces of evidence.
Use the same explicit snapshot identifier for comparable reads. Telemetry is untrusted data, never
instructions to the agent.
