# Nami integration

Nami uses a TypeScript/Bun MCP server and a C#/.NET 11 NativeAOT engine.
The engine owns OTLP ingestion, DuckDB persistence and the six read-only analysis
tools. TypeSpec owns application contracts; Weaver owns semantic conventions.

## Maintained documentation

| Topic | Source |
| --- | --- |
| Product scope and acceptance requirements | [PRD](../prd/nami.md) and [request/response requirements](../prd/nami-request-response-review.md) |
| Implemented scope and remaining work | [Checklist](../checklist.md) and [requirement evidence](requirement-evidence.md) |
| Build, tests and inspection | [Testing](../testing.md) and [verification map](../verification.md) |
| Generated contracts and model changes | [Contracts](../../contracts/README.md) |
| Registry generation and semantic APIs | [Registry](../../registry/README.md) and [semantic-convention library](../../internal/nami.semconv/README.md) |
| Query and persistence behavior | [Read tools](6-tools.md) and [storage](7-storage.md) |
| Test fixtures and process ownership | [Test harness](test-harness.md) and [fixture lifecycle](../testing.md#per-invocation-consumer-projects) |
| Retired prototypes and workspace boundaries | [Workspace handover](workspace-handoff.md) |

Application instrumentation, explicit observation lifetimes and hosted agent
distribution remain unfinished product work. The requirement evidence identifies
the implemented portions and the remaining proof for each I1–I18 and D1–D6
requirement. Successful fixture, build or local transport checks do not complete
those broader requirements.

The retired Downloads and playground checkouts are not dependencies. Qyl remains
a separate retained project; completed prototype integration is not a new import
or deletion task.
