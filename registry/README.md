# Nami telemetry registry

`registry/` is a self-contained Weaver publication. Its `manifest.yaml` points to the
committed `resolved.json`, so the canonical registry can be validated without fetching
or reparsing external definitions:

```sh
weaver registry check --future --v2 -r registry -p registry/policies
```

Authored definitions live in `registry/source/`. They define nami's own `nami.*` attributes, its
project entity and the collector's counters, and import the pinned GenAI registry. The registry
names what nami itself emits and the conventions its tools read; it does not decide what is stored
and describes no sender. What Claude Code and the Claude API send is recorded in
[`docs/telemetry/claude-code-anthropic-reference.md`](../docs/telemetry/claude-code-anthropic-reference.md).
GenAI imports OpenTelemetry core v1.45.0. Core is deliberately not a second
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

Since 16 Nov 2026 the collector stores every key as it was sent, known or not, deprecated or not,
in the type and container it was sent in. No annotation decides what is stored, converted or
bounded: the annotations `nami.persisted`, `nami.token_exempt`, `nami.producer`,
`nami.instrumentation_scope`, `nami.wire_type`, `nami.wire_container` and `nami.max_utf8_bytes`
are gone with their policy rules and generated code (0.5.0). Every string passes the collector's
credential stage before any bound, whatever its key (`CredentialRedaction`); the size bounds are
the same for every key (`IngestLimits`). A `nami.*` key needs a brief and a stability; that is the
whole policy.

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
descriptions, annotations, and external JSON-schema changes. The 0.4.0 publication kept
these pins and added definitions of Claude Code's telemetry; 0.5.0 removes them again and with
them every annotation that steered ingest. `CHANGELOG.md` lists both.
