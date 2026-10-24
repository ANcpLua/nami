# Registry integration

The canonical `registry/` is a committed Weaver publication: `manifest.yaml` points to
`resolved.json`. The direct check is green without upstream resolution:

```sh
weaver registry check --future --v2 -r registry -p registry/policies
```

Authored source is in `registry/source/`. Its canonical GenAI pin imports core v1.44.0;
Nami imports GenAI only. A second direct core import produced `AmbiguousReference` for
`gen_ai.agent.description`, so the documented checklist fallback is applied.

The authored upstream pins produce known mixed v1/v2 format-migration notices under Weaver
0.28.0. `registry/generate.sh` narrowly classifies them, materializes the effective definitions
without semantic changes, and strictly validates the materialized schema before publishing
or comparing it. `--check` also verifies the canonical publication has not been hand-edited.
See `registry/README.md`; no policy is globally disabled.

The effective stable counts are 121 attributes, 51 metrics, 8 span refinements (4 base types),
1 event, and 5 entities. The initial raw count of 116 attributes counted excluded legacy
shadows incorrectly; the generated inventory counts the effective definitions.

Persistence annotations for inherited attributes live in
`registry/source/nami/persistence.yaml`. Collector counter names are generated from
`registry/source/nami/metrics.yaml`.
