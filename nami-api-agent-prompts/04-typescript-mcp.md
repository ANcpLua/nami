# TypeScript 7.1 beta and MCP 2026-07-28

## Context and working method

Work in `/Users/alexandernachtmann/RiderProjects/nami`. Read the current `AGENTS.md` and relevant local skills. Nami is the preview/experimental successor to Qyl and is built for AI coding agents, not a human dashboard. It has its own C# collector, DuckDB storage, generated contracts, and a TypeScript MCP surface targeting protocol `2026-07-28`. Qyl is only a source for the remaining instrumentation migration, not an architecture or correctness reference.

The supplied snapshot selects SDK `11.0.100-rc.3.26522.148`, `net11.0`, C# `preview`, ANcpLua.NET.Sdk `3.4.51`, Bun `1.4.4`, and Node `24.22.1`; the user identifies the compiler as TypeScript 7.1 beta with the Go implementation. Resolve the current checkout's actual packages, imports, framework/RID assets, compiler executable and lockfile rather than treating this snapshot as current truth. Preview and experimental APIs are eligible when they improve the whole implementation; do not introduce stable fallbacks merely because they are experimental.

Use `ref help` or `ref` to discover the installed RefTrace interface and read its skill. Do not invent switches. Use resolved NuGet reference assets for callable APIs, implementation assemblies/ILSpy for behavior, and package metadata/SourceLink to identify the matching upstream source. Reuse existing reference checkouts; clone missing ones yourself outside product source, following the local reference-directory convention. Bind a package to its source commit rather than assuming GitHub `main` is what was restored. For TypeScript use the installed package's exported declarations and runtime implementation. Use Microsoft Learn as a lead, not the sole basis for a version-sensitive member. Compile the affected call shape with the pinned consumer toolchain; do not substitute a different SDK to make it compile.

This is focused API/library research for implementation agents, not a repository-wide audit. Return only worthwhile remaining changes: current Nami `path:line` → desired replacement, exact package/member, and a short source locator containing package version, framework, upstream commit and absolute local source/decompilation path with member or line range. State a real blocker rather than guessing. Omit work already completed. No status report, broad regression suite, compatibility layer or unrelated cleanup. Keep product edits out of the research pass. Independently consider a better design than the suggested candidates when it reduces total code, allocation, ambiguity, dependency cost or agent reasoning effort.

## Outcome

Find version-correct simplifications and missing useful capabilities in `src/nami.mcp`, using the actual installed TypeScript compiler, MCP packages, runtime and generated contracts. Do not migrate MCP to C# merely because a .NET ModelContextProtocol central version exists.

Identify the exact TypeScript 7.1 beta distribution and executable through the package/lockfile/scripts. The Go implementation returned to `microsoft/TypeScript`; do not use the archived staging repository as current source. Identify Bun versus Node production execution separately from type checking. Npm dist-tags and the SDK's moving main branch are discovery aids, not evidence of the installed API.

Current v2 guidance explicitly says v2 construction alone does not opt into protocol 2026-07-28. Inspect the actual server entry point, advertised revision and SDK transport rather than assuming the major version establishes wire behavior. Research the package's supported modern-only configuration and remove the need for hand-written transport/session compatibility machinery in the proposed design. Do not expand supported legacy clients without an actual consumer requirement.

Study the reached implementations for request metadata, discovery, request-scoped cancellation, inputRequired/requestState, structured tool results and resource references. Prefer existing SDK facilities over parallel protocol code. Distinguish durable application evidence handles from protocol requestState; bind authorization to the actual principal on every call and use the SDK's integrity-protection facilities where the installed version provides them. Do not treat caller-supplied requestState as trusted or force ordinary pagination through an input-required round trip.

Choose only features that reduce agent ambiguity or repeated work. Preserve cancellation and error meaning through fetch to the C# boundary. Nami supplies evidence and deterministic operations; do not add an internal model-driven diagnosis loop or human interaction flow just because an SDK supports one.

## Source leads

- https://github.com/microsoft/typescript-go/issues/4918
- https://github.com/microsoft/TypeScript
- https://github.com/modelcontextprotocol/typescript-sdk
- https://ts.sdk.modelcontextprotocol.io/v2/migration/support-2026-07-28
- https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/streamable-http
