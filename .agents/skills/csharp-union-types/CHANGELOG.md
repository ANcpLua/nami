# Changelog

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
