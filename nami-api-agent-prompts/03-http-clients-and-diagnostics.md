# HTTP clients, request diagnostics, and structured failures

## Context and working method

Work in `/Users/alexandernachtmann/RiderProjects/nami`. Read the current `AGENTS.md` and relevant local skills. Nami is the preview/experimental successor to Qyl and is built for AI coding agents, not a human dashboard. It has its own C# collector, DuckDB storage, generated contracts, and a TypeScript MCP surface targeting protocol `2026-07-28`. Qyl is only a source for the remaining instrumentation migration, not an architecture or correctness reference.

The supplied snapshot selects SDK `11.0.100-rc.3.26522.148`, `net11.0`, C# `preview`, ANcpLua.NET.Sdk `3.4.51`, Bun `1.4.4`, and Node `24.22.1`; the user identifies the compiler as TypeScript 7.1 beta with the Go implementation. Resolve the current checkout's actual packages, imports, framework/RID assets, compiler executable and lockfile rather than treating this snapshot as current truth. Preview and experimental APIs are eligible when they improve the whole implementation; do not introduce stable fallbacks merely because they are experimental.

Use `ref help` or `ref` to discover the installed RefTrace interface and read its skill. Do not invent switches. Use resolved NuGet reference assets for callable APIs, implementation assemblies/ILSpy for behavior, and package metadata/SourceLink to identify the matching upstream source. Reuse existing reference checkouts; clone missing ones yourself outside product source, following the local reference-directory convention. Bind a package to its source commit rather than assuming GitHub `main` is what was restored. For TypeScript use the installed package's exported declarations and runtime implementation. Use Microsoft Learn as a lead, not the sole basis for a version-sensitive member. Compile the affected call shape with the pinned consumer toolchain; do not substitute a different SDK to make it compile.

This is focused API/library research for implementation agents, not a repository-wide audit. Return only worthwhile remaining changes: current Nami `path:line` → desired replacement, exact package/member, and a short source locator containing package version, framework, upstream commit and absolute local source/decompilation path with member or line range. State a real blocker rather than guessing. Omit work already completed. No status report, broad regression suite, compatibility layer or unrelated cleanup. Keep product edits out of the research pass. Independently consider a better design than the suggested candidates when it reduces total code, allocation, ambiguity, dependency cost or agent reasoning effort.

## Outcome

Select concrete improvements to Nami's HTTP call sites and request diagnostics, separating incoming requests, ordinary outgoing service calls, and OTLP exporter traffic.

Trace actual consumers of the snapshot's Microsoft.Extensions.Http.Resilience, Microsoft.AspNetCore.Diagnostics.Middleware, compliance/redaction and telemetry packages, all on the 10.11.0 Extensions line. Match their selected assets to the source release. Candidate changes should delete custom machinery or expose better structured evidence, not add another abstraction around HttpClient.

An exact version-sensitive lead: dotnet/extensions 10.11.0 renamed incoming `AddHttpLatencyTelemetry` to `AddHttpServerLatencyTelemetry` on `HttpLoggingServiceCollectionExtensions`, retaining experimental diagnostic EXTEXP0013. The original pull-request description discusses stabilization, but later commits and release notes explicitly keep this release experimental. Read the shipped metadata and required companion registrations before recommending it. Do not layer duplicate request instrumentation on top of Nami's existing observation path.

Investigate supported request-scoped buffering, scope preservation, structured logging and redaction only where they solve an actual diagnostic gap. Distinguish request logs, activities and metrics rather than treating them as interchangeable evidence. Protect sensitive payloads; logging complete telemetry or tool arguments is not an acceptable shortcut.

For outgoing requests, resolve handler ownership, connection reuse, body lifetime and cancellation through the consumer. Inspect the exact resilience implementation before choosing policies. Do not apply generic retries or hedging to OTLP or side-effecting operations without their delivery/idempotency semantics. On the TypeScript boundary, runtime fetch behavior belongs to the resolved Bun/Node runtime, not to the TypeScript compiler version.

## Source leads

- https://github.com/dotnet/extensions/releases/tag/v10.11.0
- https://github.com/dotnet/extensions/pull/7730
- https://www.nuget.org/packages/Microsoft.AspNetCore.Diagnostics.Middleware
- https://www.nuget.org/packages/Microsoft.Extensions.Http.Resilience
- https://opentelemetry.io/docs/specs/otlp/
