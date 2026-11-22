# Nami integration

Nami uses a TypeScript/Bun MCP server and a C#/.NET 11 NativeAOT engine.
The engine owns OTLP ingestion, DuckDB persistence and the six read-only analysis
tools. TypeSpec owns application contracts; Weaver owns semantic conventions.

## Maintained documentation

| Topic | Source |
| --- | --- |
| Permanent decisions (binding) | [Decisions](../decisions/README.md) |
| The public "Ways to run nami" page and what a directory listing needs | [Distribution](../distribution.md) |
| Product scope and acceptance requirements | [PRD](../prd/nami.md) and [request/response requirements](../prd/nami-request-response-review.md) |
| Implemented scope and remaining work | [Checklist](../checklist.md) and [requirement evidence](requirement-evidence.md) |
| Build, tests and inspection | [Testing](../testing.md) and [verification map](../verification.md) |
| Generated contracts and model changes | [Contracts](../../contracts/README.md) |
| Registry generation and semantic APIs | [Registry](../../registry/README.md) and [semantic-convention library](../../internal/nami.semconv/README.md) |
| Test fixtures and process ownership | [Fixture lifecycle](../testing.md#per-invocation-consumer-projects) |

Application instrumentation, explicit observation lifetimes and hosted agent
distribution remain unfinished product work. The requirement evidence identifies
the implemented portions and the remaining proof for each I1–I18 and D1–D6
requirement. Successful fixture, build or local transport checks do not complete
those broader requirements.
