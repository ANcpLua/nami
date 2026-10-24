# Nami telemetry registry

`registry/` is a self-contained Weaver publication. Its `manifest.yaml` points to the
committed `resolved.json`, so the canonical registry can be validated without fetching
or reparsing external definitions:

```sh
weaver registry check --future --v2 -r registry -p registry/policies
```

Authored definitions live in `registry/source/`. They define Nami's project/discovery
attributes, collector counters, and persistence annotations, and import the pinned GenAI
registry. GenAI imports OpenTelemetry core v1.44.0. Core is deliberately not a second
direct dependency: Weaver 0.28.0 reports ambiguous GenAI attribute references when both
registries are imported directly.

Run from the repository root:

```sh
registry/generate.sh          # validate source and regenerate the publication, C#, Rust, and Markdown
registry/generate.sh --check  # validate source and compare all outputs without editing them
```

The script requires Python 3, git, and Weaver 0.28.0. It reads exact git objects from existing
sibling checkouts when available; otherwise it fetches the pinned public repositories into
`artifacts/registry-source-cache`. Uncommitted changes in sibling checkouts are never used.
Edit files under `source/`, a template, or a pin; do not edit the publication by hand.

## Materialization of the pinned mixed-format sources

The raw authored upstream trees are not accepted by Weaver 0.28.0's `--future` check: Weaver
promotes its v1/v2 migration notices to errors. Core and GenAI include `definition/2` files,
omitted manifest format tags, and four signals whose requirement level is implicit. GenAI
also includes excluded legacy attributes beside their replacements, which Weaver's
compatibility resolver reports as duplicate IDs.

The entrypoint therefore performs these explicit steps:

1. Resolve the exact pinned authored registries with Weaver. Unexpected diagnostics fail.
   Only the enumerated immutable format-migration diagnostics in `generate.py` are accepted;
   authored Nami policy failures and arbitrary duplicate definitions are not waived.
2. Materialize that resolved view into Weaver's `resolved/2.0` format. Excluded legacy definitions
   are removed; derived definitions win in dependency order. Per-signal attribute refinements
   are kept as separate catalog entries. Original schema provenance is retained in the
   `nami.source_schema` annotation, rather than temporary filesystem paths.
3. Strictly validate the materialization with Weaver and Nami's Rego policies, then regenerate
   or compare `registry/manifest.yaml` and `registry/resolved.json`.
4. Strictly validate the committed canonical registry with the direct command above and
   generate C#, Rust, and Markdown from the same checked snapshot.

Source registries and their git objects are never edited. The generated publication contains
all effective definitions and provenance annotations; the source files retain the canonical
upstream URLs and exact dependency pin.

`source/nami/persistence.yaml` refines inherited attributes through a public attribute group;
it does not redeclare upstream definitions. Unknown keys are not persisted. A deprecated key
normalizes to its declared successor or is dropped. Exception text remains subject to the
collector sanitizer. Captured header templates are known keys and retain their supplied values.

The embedded `internal/nami.semconv/Generated/registry-inventory.json` records exact pins and
counts. The effective pinned inventory contains 121 stable nondeprecated attributes, 51 stable
metric instruments, eight stable span refinements (four base span types), one stable event,
and five stable entities. Development definitions and Nami additions retain explicit stability.

The generation gate also verifies that the authored dependency URI and the current CHANGELOG
entry match both exact pinned commits. A pin change beyond the initial baseline requires a
nonempty committed report under `changes/`, referenced by a `Diff:` line in that entry.

A noninitial diff report is JSON containing `head_pins` and `baseline_pins` (each with
`core`, `genai`, and `weaver`), `baseline_registry` (a committed publication directory
under `registry/changes/`), and `weaver_diff` (the JSON output of Weaver's diff command).
The baseline directory includes its generated `source-pins.json`. Baseline commit IDs
must occur in an earlier changelog entry. The gate reruns Weaver's diff against that
baseline and the current materialization and compares the complete result.
