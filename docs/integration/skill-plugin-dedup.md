# Skill and plugin cleanup — 27 October 2026

This records the completed cleanup after the prototype handover. It does not
repeat the migration or claim new product validation. The earlier handover's
backup and directory inventory describe its earlier observation, not current
filesystem state.

## Removed with explicit user authorization

- The Downloads checkout
  `~/Downloads/Qyl.Telemetry.Tests-checkpoint-2026-10-24-000952-nami-prd`.
- Its old backup
  `~/.local/state/nami/handoffs/retired-prototype-20261026T070739Z`, including
  the archive, Git bundle, patches, status and manifest. No replacement backup
  was created. These local deletions were not commits in the retired repository.
- The redundant `~/RiderProjects/stale/dotfiles` checkout and its eleven skill
  copies. Its clean HEAD `7f3d1a5` was confirmed on GitHub and fast-forwarded into
  the active Chezmoi repository; the existing WISHLIST changes were preserved.
- The redundant `~/RiderProjects/stale/csharp-union-types` checkout. Its clean
  HEAD `d3d32d6` was confirmed on GitHub. All its source files remain in Nami's
  canonical union skill, including newer serialization guidance absent from
  that checkout.

## Retained ownership

| Source | Role |
| --- | --- |
| `ANcpLua/csharp-union-types` | Canonical union skill since 2026-11-10 (commit `111e764`). The copy under `nami/.agents/skills` was removed after its newer serialization guidance was pushed there. |
| `nami-plugin/plugins/nami` in `ANcpLua/opusfix` | Portable Nami developer plugin, version 0.1.1. |
| Installed `ancplua-openai/nami/0.1.1` cache | Runtime distribution; all five skill entrypoints match the source. |
| `~/.local/share/chezmoi/.agents/skills` | Canonical eleven macOS skills; deployed files remain under `~/.agents/skills`. |
| `ancplua-openai` | Sole own plugin marketplace; local source links remain valid. |

The plugin's union sync now exports only to its package by default. An additional
standalone export requires an explicit `UNION_CHECKOUT`. Generated plugin or
Claude distributions and deployed Chezmoi files are retained; they are not
independently maintained source copies. Nami's product-specific
`fixture-lifecycle` and the portable plugin's `fixture-scope` remain distinct.

## Deferred investigation

Qyl and the playground were retained during this cleanup. Subsequently, after consolidation,
the user explicitly authorized deleting the playground; its directory was removed on 27 October
2026 while its verified archive was retained. See [workspace-handoff.md](workspace-handoff.md).
The user explicitly retained Qyl for examination
of `qyl.telemetry.autoinstrumentation`. Local sources found so far include
`qyl/internal/qyl.instrumentation` and `qyl/internal/qyl.instrumentation.generators`;
this is not yet a confirmed mapping to the named package. Follow the approved
[request/response review](../prd/nami-request-response-review.md): establish the
needed observation capability before choosing an interceptor or other mechanism.
No Qyl implementation was imported or changed by this cleanup.

## Validation

The Nami plugin package check and all 22 existing package/union maintenance tests
passed. The canonical union-to-plugin sync reports no drift. All remaining
inventoried skill entrypoints retain their pre-cleanup hashes, and all four local
marketplace links resolve. No product build or runtime test was required or run
for these source-ownership and documentation changes.
