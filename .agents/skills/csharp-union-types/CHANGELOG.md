# Changelog

## Unreleased

- Verify old-consumer/new-library behavior by replacing only the library DLL and checking that the consumer hash stays unchanged.
- Check local Markdown files and section anchors in CI.
- Add an explicit, drift-checked one-way exporter from the canonical Nami skill to standalone/plugin copies.

## 2026-10-26 — multi-level closed hierarchies

- Added a visible closed-hierarchy section and a self-contained example with a closed intermediate node and sealed leaves.
- Added runtime leaf/null checks and compiler checks for a missing leaf, an open external extension point and a closed intermediate boundary.
- Synchronized from the current Nami skill, retaining its RC3 pin and local verification workflow.

## 2026-10-25 — .NET 11 RC3 security update

- Updated the pinned SDK and verification runner to `11.0.100-rc.3.26522.148`.
- Revalidated the existing union examples, functional composition, compiler diagnostics, JSON behavior and polyfill boundaries on RC3.
- Retained the existing example and polyfill implementations; the RC3 release notes list no union-language changes.

## 2026-10-24 — functional result composition

- Added pure Map/Bind/Match composition with explicit error values and an invariant-culture pipeline.
- Verified success transformations, failure propagation, skipped callbacks and representative functional laws.
- Retained explicit rejection of invalid defaults/null callbacks and unchanged callback exceptions.

## 2026-10-24 — typed generic results

- Added a `Result<T>(Success<T>, Failure)` example with int/string success, failure and default checks.
- Added compiler rejections for the wrong generic case type and a missing failure arm.
- Distinguished SDK compilers and active language versions from separately referenced Roslyn packages.
- Refreshed the recorded verification results.

## 2026-10-24 — immutable examples and runtime proof

- Made the outcome and length unions readonly and explained default/null handling and allocation tradeoffs.
- Added executed checks for both outcome cases, both units, scalar boundaries, false/zero, empty states and consistent custom accessors.
- Measured the warmed scalar construction/matching path and retained its zero-managed-allocation check alongside C# 15.0 compilation.
- Retained per-check build binlogs next to the verification report.

## 2026-10-24

- Created the skill package from the local files and probes supplied by Alexander Nachtmann.
- Included union polyfills, local diagnostic explanations and compiler/runtime checks.
- Removed remote source dependencies, documentation lookups and commit/hash comparisons.
- Retained existing third-party license notices with the supplied code.
