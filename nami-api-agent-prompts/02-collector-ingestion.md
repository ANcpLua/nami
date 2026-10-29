# Collector ingestion: HTTP, Protobuf and AOT

## Context and working method

Work in `/Users/alexandernachtmann/RiderProjects/nami`. Read the current `AGENTS.md` and relevant local skills. Nami is the preview/experimental successor to Qyl and is built for AI coding agents, not a human dashboard. It has its own C# collector, DuckDB storage, generated contracts, and a TypeScript MCP surface targeting protocol `2026-07-28`. Qyl is only a source for the remaining instrumentation migration, not an architecture or correctness reference.

The supplied snapshot selects SDK `11.0.100-rc.3.26522.148`, `net11.0`, C# `preview`, ANcpLua.NET.Sdk `3.4.51`, Bun `1.4.4`, and Node `24.22.1`; the user identifies the compiler as TypeScript 7.1 beta with the Go implementation. Resolve the current checkout's actual packages, imports, framework/RID assets, compiler executable and lockfile rather than treating this snapshot as current truth. Preview and experimental APIs are eligible when they improve the whole implementation; do not introduce stable fallbacks merely because they are experimental.

Use `ref help` or `ref` to discover the installed RefTrace interface and read its skill. Do not invent switches. Use resolved NuGet reference assets for callable APIs, implementation assemblies/ILSpy for behavior, and package metadata/SourceLink to identify the matching upstream source. Reuse existing reference checkouts; clone missing ones yourself outside product source, following the local reference-directory convention. Bind a package to its source commit rather than assuming GitHub `main` is what was restored. For TypeScript use the installed package's exported declarations and runtime implementation. Use Microsoft Learn as a lead, not the sole basis for a version-sensitive member. Compile the affected call shape with the pinned consumer toolchain; do not substitute a different SDK to make it compile.

This is focused API/library research for implementation agents, not a repository-wide audit. Return only worthwhile remaining changes: current Nami `path:line` → desired replacement, exact package/member, and a short source locator containing package version, framework, upstream commit and absolute local source/decompilation path with member or line range. State a real blocker rather than guessing. Omit work already completed. No status report, broad regression suite, compatibility layer or unrelated cleanup. Keep product edits out of the research pass. Independently consider a better design than the suggested candidates when it reduces total code, allocation, ambiguity, dependency cost or agent reasoning effort.

## Outcome

Find the smallest, best-supported receive/decode/accept path for Nami's actual OTLP producers in `src/nami.collector.ingestion` and the host. Do not replace it with a different collector or carry MCP transport conventions into OTLP.

The snapshot pins `Google.Protobuf` 3.36.2 and gRPC runtime/tools 2.84.0. The protobuf `v36.2` project declares version 3.36.2 and its `MessageParser<T>` exposes `ParseFrom(ReadOnlySequence<byte>)` and `ParseFrom(ReadOnlySpan<byte>)`. Locate avoidable `ToArray`, string round trips and whole-payload copies in the current path. Compare these overloads with the existing stream implementation using the exact resolved assembly. Prefer the simpler end-to-end path; do not impose PipeReader merely because it is newer or call message materialization “zero-copy”. Account for complete-message framing, buffer ownership, cancellation, compressed inputs and bounded memory.

Inspect the .NET 11 RC3 reference surface for request-body and source-generated JSON APIs, and the actual generated gRPC service binding and marshallers. Inspect the protoc binary delivered by Grpc.Tools independently of the Google.Protobuf runtime version. Trace specific trimming/dynamic-code requirements through reached members; do not ban an entire namespace or infer AOT readiness solely from generated files.

Keep the OTLP wire contract distinct from Nami's internal DTOs: trace/span IDs, enum encoding, unknown fields, 64-bit integers, protocol status bodies, partial acceptance and retryability. Ensure any existing generic HTTP error/JSON infrastructure does not change that contract. Evaluate acceptance and cancellation at the storage/queue boundary so a lost HTTP response is not confused with an unaccepted batch. Research the actual persistence behavior before proposing a queue.

## Source leads

- https://www.nuget.org/packages/Google.Protobuf/3.36.2
- https://github.com/protocolbuffers/protobuf/tree/v36.2/csharp/src/Google.Protobuf
- https://github.com/dotnet/aspnetcore/releases/tag/v11.0.0-rc.3.26522.148
- https://github.com/grpc/grpc-dotnet
- https://opentelemetry.io/docs/specs/otlp/
