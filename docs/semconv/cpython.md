# `cpython` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `cpython.gc.generation` | `enum` | development |  | Value of the garbage collector collection generation. | `0`, `1`, `2` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `cpython.gc.generation` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `0` | development |  | Generation 0 |
| `1` | development |  | Generation 1 |
| `2` | development |  | Generation 2 |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `cpython.gc.collected_objects` | counter | `{object}` | development |  | The total number of objects collected inside a generation since interpreter start. |
| `cpython.gc.collections` | counter | `{collection}` | development |  | The number of times a generation was collected since interpreter start. |
| `cpython.gc.uncollectable_objects` | counter | `{object}` | development |  | The total number of objects which were found to be uncollectable inside a generation since interpreter start. |
