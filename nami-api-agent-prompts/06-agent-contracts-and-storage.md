# Agent-facing contracts, evidence handles, and DuckDB

## Context and working method

Work in `/Users/alexandernachtmann/RiderProjects/nami`. Read the current `AGENTS.md` and relevant local skills. Nami is the preview/experimental successor to Qyl and is built for AI coding agents, not a human dashboard. It has its own C# collector, DuckDB storage, generated contracts, and a TypeScript MCP surface targeting protocol `2026-07-28`. Qyl is only a source for the remaining instrumentation migration, not an architecture or correctness reference.

The supplied snapshot selects SDK `11.0.100-rc.3.26522.148`, `net11.0`, C# `preview`, ANcpLua.NET.Sdk `3.4.51`, Bun `1.4.4`, and Node `24.22.1`; the user identifies the compiler as TypeScript 7.1 beta with the Go implementation. Resolve the current checkout's actual packages, imports, framework/RID assets, compiler executable and lockfile rather than treating this snapshot as current truth. Preview and experimental APIs are eligible when they improve the whole implementation; do not introduce stable fallbacks merely because they are experimental.

Use `ref help` or `ref` to discover the installed RefTrace interface and read its skill. Do not invent switches. Use resolved NuGet reference assets for callable APIs, implementation assemblies/ILSpy for behavior, and package metadata/SourceLink to identify the matching upstream source. Reuse existing reference checkouts; clone missing ones yourself outside product source, following the local reference-directory convention. Bind a package to its source commit rather than assuming GitHub `main` is what was restored. For TypeScript use the installed package's exported declarations and runtime implementation. Use Microsoft Learn as a lead, not the sole basis for a version-sensitive member. Compile the affected call shape with the pinned consumer toolchain; do not substitute a different SDK to make it compile.

This is focused API/library research for implementation agents, not a repository-wide audit. Return only worthwhile remaining changes: current Nami `path:line` → desired replacement, exact package/member, and a short source locator containing package version, framework, upstream commit and absolute local source/decompilation path with member or line range. State a real blocker rather than guessing. Omit work already completed. No status report, broad regression suite, compatibility layer or unrelated cleanup. Keep product edits out of the research pass. Independently consider a better design than the suggested candidates when it reduces total code, allocation, ambiguity, dependency cost or agent reasoning effort.

## Outcome

Find the few changes that let an AI coding agent answer a question with less inference and fewer tool calls. Optimize the entire TypeSpec → generated C#/TypeScript/schema → storage mapping → MCP result path, not isolated API novelty. Start with the repository's contract-model skill and actual tools.

Nami collects and exposes evidence and performs deterministic filtering, grouping, aggregation and comparison. The consuming model interprets causes. Do not introduce a human dashboard, internal autonomous diagnosis service, agent framework or new serializer just because the application is AI-facing.

Look for state encoded as ambiguous booleans/nulls, prose that agents must parse, redundant schema copies, repeated joins in the model's context, unbounded result dumps, and implicit state hidden in MCP sessions. Prefer generated discriminated outcomes, explicit bounded queries and authorized evidence/snapshot handles with concise provenance and follow-up references. Preserve distinctions such as no matching data versus unavailable, incomplete or expired data without leaking unauthorized resource existence. Reuse existing identities and contracts rather than adding a new abstraction for every distinction.

The snapshot pins DuckDB.NET.Data.Full 1.5.6.1. Inspect the actual selected managed and native assets, provider implementations and matched upstream source. Investigate generated/typed row access, batching/appender ownership, streaming result lifetime, real cancellation behavior and resource bounds. Do not infer that an Async-suffixed ADO.NET method performs nonblocking work, or that wrapping it in Task.Run creates native cancellation. Do not propose Arrow, a different database, a generic ORM or a custom native wrapper without an end-to-end advantage in a real consumer.

Treat experimental retention settings and code comments as leads, not established behavior. Preserve the existing storage/wire/model boundary where its semantics differ, and eliminate duplicate representations only where the actual contracts are equivalent. Return concrete generated-source-owner and consumer locations, not manual edits to generated output.

## Source leads

- https://www.nuget.org/packages/DuckDB.NET.Data.Full/1.5.6.1
- https://github.com/Giorgi/DuckDB.NET
- https://github.com/duckdb/duckdb
- https://github.com/microsoft/typespec
- https://modelcontextprotocol.io/specification/2026-07-28/basic/transports
