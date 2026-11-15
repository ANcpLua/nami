# Nami telemetry registry

`registry/` is a self-contained Weaver publication. Its `manifest.yaml` points to the
committed `resolved.json`, so the canonical registry can be validated without fetching
or reparsing external definitions:

```sh
weaver registry check --future --v2 -r registry -p registry/policies
```

Authored definitions live in `registry/source/`. They define Nami's project/discovery
attributes, collector counters, and persistence annotations, and import the pinned GenAI
registry. `source/claude_code/` describes the telemetry nami ingests from Claude Code: its
counters, events, spans and the keys they carry, under Claude Code's own names. GenAI imports OpenTelemetry core v1.45.0. Core is deliberately not a second
direct dependency: Weaver 0.28.0 reports ambiguous GenAI attribute references when both
registries are imported directly.

Run from the repository root:

```sh
registry/generate.sh          # validate source and regenerate the publication, C# and Markdown
registry/generate.sh --check  # validate source and compare all outputs without editing them
```

The script requires Python 3, git, and Weaver 0.28.0. It reads exact git objects from existing
sibling checkouts when available; otherwise it fetches the pinned public repositories into
`artifacts/registry-source-cache`. Uncommitted changes in sibling checkouts are never used.
Edit files under `source/`, a template, or a pin; do not edit the publication by hand.

## Materialization of the pinned mixed-format sources

The raw authored upstream trees are not accepted by Weaver 0.28.0's `--future` check: Weaver
promotes its v1/v2 migration notices to errors. Core and GenAI include `definition/2` files,
omitted manifest format tags, and six signals whose requirement level is implicit. GenAI
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
   generate C# and Markdown from the same checked snapshot.

Source registries and their git objects are never edited. The generated publication contains
all effective definitions and provenance annotations; the source files retain the canonical
upstream URLs and exact dependency pin.

`source/nami/persistence.yaml` refines inherited attributes through a public attribute group;
it does not redeclare upstream definitions. Unknown keys are not persisted. A deprecated key
normalizes to its declared successor or is dropped. Exception text remains subject to the
collector sanitizer. Captured header templates are known keys and retain their supplied values.

A key nami defines for a sender carries `nami.producer` (Claude Code's keys: `claude_code`)
and, like a `nami.*` key, a brief, a stability and `nami.persisted`. Its type is the type nami
stores. Where the sender puts another primitive type on the wire, the signal's reference says
so in `nami.wire_type`, and ingest converts; where it nests the key inside a JSON string
attribute, the reference names that attribute in `nami.wire_container`, and ingest lifts the
key out and drops the container. A key may declare `nami.max_utf8_bytes`, the longest value
in UTF-8 bytes that ingest keeps of it (generated as `ValueLimits.MaxUtf8Bytes`; ingest
enforces it). A producer's signals name the instrumentation scope they
arrive under (`nami.instrumentation_scope`); a producer's key means what the registry says
only on records from those scopes. Weaver 0.28.0 replaces, rather than merges, a definition's
annotations with those of a reference, so persistence is always read from the definition.

The embedded `internal/nami.semconv/Generated/registry-inventory.json` records exact pins and
counts. The effective pinned inventory contains 125 stable nondeprecated attributes, 53 stable
metric instruments, 14 stable span refinements (five base span types), one stable event,
and five stable entities. Development definitions and Nami additions retain explicit stability.

The generation gate verifies that the authored dependency URI and the current CHANGELOG
entry match both exact pinned commits and the selected Weaver version. It validates the
current materialization, publication, policies, and generated outputs without requiring
historical registry copies or stored diff reports. Earlier versions remain in Git history.

The current GenAI pin is `e6658f14c9d9ea0760825f029cb82e7673f48fab`, the head of upstream
`main` on 14 Nov 2026; GenAI imports core v1.45.0 since `f4219d1` (#675). The 0.3.0
publication follows core v1.45.0 and adds GenAI realtime sessions, exception events for the
client operations, the internal retrieval span and the `gen_ai.execute_tool` span type.
Inspect the exact upstream comparison when reviewing a pin update, including refinements,
descriptions, annotations, and external JSON-schema changes. The 0.4.0 publication keeps
these pins and adds the Claude Code definitions; `CHANGELOG.md` lists them.
