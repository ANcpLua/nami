# `nami` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `nami.attribute.key` | `string` | development |  | An attribute key observed during attribute discovery, as the sender wrote it. | `"http.request.method"`, `"db.query.parameter.id"` | `nami.persisted`: `true`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.1.0"`, `nami.token_exempt`: `false` |
| `nami.attribute.namespace` | `string` | development |  | The namespace of an attribute key observed during attribute discovery. | `"http.request"`, `"gen_ai.usage"` | `nami.persisted`: `true`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.1.0"`, `nami.token_exempt`: `false` |
| `nami.gen_ai.tool.call.arguments_hash` | `string` | development |  | A hash of the tool call arguments; never the arguments themselves. | `"sha256:9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08"` | `nami.persisted`: `true`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.1.0"`, `nami.token_exempt`: `true` |
| `nami.project.id` | `string` | development |  | The nami project a resource belongs to. | `"tomo"`, `"nami"` | `nami.persisted`: `true`, `nami.source_schema`: `"https://ancplua.github.io/nami/schemas/0.1.0"`, `nami.token_exempt`: `false` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `nami.collector.attributes.coalesced` | counter | `{attribute}` | development |  | Alias attributes coalesced into a canonical semantic convention key. |
| `nami.collector.attributes.dropped` | counter | `{attribute}` | development |  | Attributes excluded by the persistence policy. |
| `nami.collector.attributes.sanitization_failed` | counter | `{attribute}` | development |  | Exception attributes whose text could not be sanitized. |
