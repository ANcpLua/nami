# Instrumentation migration, OpenTelemetry, and NativeAOT

## Context and working method

Work in `/Users/alexandernachtmann/RiderProjects/nami`. Read the current `AGENTS.md` and relevant local skills. Nami is the preview/experimental successor to Qyl and is built for AI coding agents, not a human dashboard. It has its own C# collector, DuckDB storage, generated contracts, and a TypeScript MCP surface targeting protocol `2026-07-28`. Qyl is only a source for the remaining instrumentation migration, not an architecture or correctness reference.

The supplied snapshot selects SDK `11.0.100-rc.3.26522.148`, `net11.0`, C# `preview`, ANcpLua.NET.Sdk `3.4.51`, Bun `1.4.4`, and Node `24.22.1`; the user identifies the compiler as TypeScript 7.1 beta with the Go implementation. Resolve the current checkout's actual packages, imports, framework/RID assets, compiler executable and lockfile rather than treating this snapshot as current truth. Preview and experimental APIs are eligible when they improve the whole implementation; do not introduce stable fallbacks merely because they are experimental.

Use `ref help` or `ref` to discover the installed RefTrace interface and read its skill. Do not invent switches. Use resolved NuGet reference assets for callable APIs, implementation assemblies/ILSpy for behavior, and package metadata/SourceLink to identify the matching upstream source. Reuse existing reference checkouts; clone missing ones yourself outside product source, following the local reference-directory convention. Bind a package to its source commit rather than assuming GitHub `main` is what was restored. For TypeScript use the installed package's exported declarations and runtime implementation. Use Microsoft Learn as a lead, not the sole basis for a version-sensitive member. Compile the affected call shape with the pinned consumer toolchain; do not substitute a different SDK to make it compile.

This is focused API/library research for implementation agents, not a repository-wide audit. Return only worthwhile remaining changes: current Nami `path:line` → desired replacement, exact package/member, and a short source locator containing package version, framework, upstream commit and absolute local source/decompilation path with member or line range. State a real blocker rather than guessing. Omit work already completed. No status report, broad regression suite, compatibility layer or unrelated cleanup. Keep product edits out of the research pass. Independently consider a better design than the suggested candidates when it reduces total code, allocation, ambiguity, dependency cost or agent reasoning effort.

## Outcome

Select the next improvements to Nami's instrumentation migration from Qyl using actual runtime/compiler/SDK behavior, not inherited Qyl patterns. Keep Nami's own instrumentation distinct from instrumentation generated for supported applications.

The uploaded snapshot has `OpenTelemetryVersion` 1.20.0 at `Version.props:35`. First determine the live resolved version. Core 1.20.1 was released on 20 October 2026 and is published on NuGet. It includes fixes for ARM64 measurement races, suppression lifetime, pooled log records and diagnostics behavior. Follow the actual source/assembly paths relevant to Nami and prioritize the patch migration if the live graph still uses 1.20.0. Avoid carrying application workarounds for upstream issues once the selected package fixes them.

For concrete API simplifications, inspect `TracerProviderBuilder.ConfigureSampler` introduced in 1.20.0: it receives the resolved sampler so instrumentation libraries can compose behavior without replacing application configuration. Also investigate the shipped instrumentation-scope version/schema APIs and existing SDK self-observability before proposing parallel metadata or health instrumentation. These are research candidates, not assumptions that Nami lacks them.

Trace the actual generated call sites, wrapper lifetime, Activity/ActivitySource/Meter subscriptions, processors and exporters. Resolve the compiler that runs the generator separately from any Microsoft.CodeAnalysis NuGet dependency. Inspect trimming and dynamic-code attributes on the specific reached APIs; broad rules such as “Reflection is banned” or “source generation makes a package AOT-safe” are insufficient.

Use targeted compilation against the real consumer and its existing NativeAOT build path where the proposed change makes an AOT claim. Avoid changes to diagnostic severity or suppressions just to pass. Preserve self-telemetry separation and avoid duplicate observations or recursive exports. Do not rebuild this as a CLR-profiler architecture.

## Source leads

- https://github.com/open-telemetry/opentelemetry-dotnet/releases/tag/core-1.20.0
- https://github.com/open-telemetry/opentelemetry-dotnet/releases/tag/core-1.20.1
- https://www.nuget.org/packages/OpenTelemetry/
- https://github.com/dotnet/runtime/releases/tag/v11.0.0-rc.3.26522.148
- https://github.com/dotnet/roslyn
