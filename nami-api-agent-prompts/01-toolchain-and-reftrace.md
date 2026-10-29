# Toolchain, resolved APIs, and RefTrace evidence

## Context and working method

Work in `/Users/alexandernachtmann/RiderProjects/nami`. Read the current `AGENTS.md` and relevant local skills. Nami is the preview/experimental successor to Qyl and is built for AI coding agents, not a human dashboard. It has its own C# collector, DuckDB storage, generated contracts, and a TypeScript MCP surface targeting protocol `2026-07-28`. Qyl is only a source for the remaining instrumentation migration, not an architecture or correctness reference.

The supplied snapshot selects SDK `11.0.100-rc.3.26522.148`, `net11.0`, C# `preview`, ANcpLua.NET.Sdk `3.4.51`, Bun `1.4.4`, and Node `24.22.1`; the user identifies the compiler as TypeScript 7.1 beta with the Go implementation. Resolve the current checkout's actual packages, imports, framework/RID assets, compiler executable and lockfile rather than treating this snapshot as current truth. Preview and experimental APIs are eligible when they improve the whole implementation; do not introduce stable fallbacks merely because they are experimental.

Use `ref help` or `ref` to discover the installed RefTrace interface and read its skill. Do not invent switches. Use resolved NuGet reference assets for callable APIs, implementation assemblies/ILSpy for behavior, and package metadata/SourceLink to identify the matching upstream source. Reuse existing reference checkouts; clone missing ones yourself outside product source, following the local reference-directory convention. Bind a package to its source commit rather than assuming GitHub `main` is what was restored. For TypeScript use the installed package's exported declarations and runtime implementation. Use Microsoft Learn as a lead, not the sole basis for a version-sensitive member. Compile the affected call shape with the pinned consumer toolchain; do not substitute a different SDK to make it compile.

This is focused API/library research for implementation agents, not a repository-wide audit. Return only worthwhile remaining changes: current Nami `path:line` → desired replacement, exact package/member, and a short source locator containing package version, framework, upstream commit and absolute local source/decompilation path with member or line range. State a real blocker rather than guessing. Omit work already completed. No status report, broad regression suite, compatibility layer or unrelated cleanup. Keep product edits out of the research pass. Independently consider a better design than the suggested candidates when it reduces total code, allocation, ambiguity, dependency cost or agent reasoning effort.

## Outcome

Give the other agents the correct API surface for Nami's actual consumers without confusing SDK, language, generator, runtime and NuGet versions.

Follow `global.json`, `Directory.Build.props`, its `eng/MSBuild/Shared.props` import, the ANcpLua SDK imports, project overrides, `Directory.Packages.props`, nested package files and resolved assets. Distinguish the SDK's Roslyn compiler from the separately pinned `Microsoft.CodeAnalysis.CSharp` dependency. Also distinguish build-host and generator target frameworks from product target frameworks.

Two specific leads from the snapshot: `MicrosoftOpenApiVersion` is declared in `Version.props` but has no visible central item in the uploaded root package file; discover its real consumer/import before calling it active or unused. `McpVersion` feeds the .NET `ModelContextProtocol` central item; it does not identify the TypeScript SDK or establish that C# owns production MCP. Identify actual consuming projects before proposing removal or migration. Do not mechanically change all Microsoft packages to major version 11.

Resolve the native TypeScript package, executable and selected entry points from package manifests, lockfiles and scripts, not from the phrase “7.1 beta”. Development moved back from `microsoft/typescript-go` to `microsoft/TypeScript`.

Use RefTrace as a research tool; `System.CommandLine` belongs to RefTrace, not Nami. Consult RefTrace/ancplua-fprog's existing skill and source before extending that workflow. Preserve reusable source locators through its existing mechanism instead of adding a second documentation system. A local reference-cache link should include version/commit identity so another agent cannot mistake a mutable checkout for the original evidence.

## Source leads

- https://github.com/ANcpLua/RefTrace
- https://github.com/dotnet/command-line-api
- https://github.com/dotnet/core/blob/main/release-notes/11.0/preview/rc3/11.0.0-rc.3.md
- https://github.com/microsoft/typescript-go/issues/4918
- https://github.com/microsoft/TypeScript
