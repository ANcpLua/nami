# `linux` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `linux.memory.slab.state` | `enum` | development | Replaced by `system.memory.linux.slab.state`. | The Linux Slab memory state | `"reclaimable"`, `"unreclaimable"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `linux.memory.slab.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `reclaimable` | development |  | `reclaimable`. |
| `unreclaimable` | development |  | `unreclaimable`. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
