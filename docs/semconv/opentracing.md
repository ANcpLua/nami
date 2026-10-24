# `opentracing` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `opentracing.ref_type` | `enum` | development |  | Parent-child Reference type |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `opentracing.ref_type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `child_of` | development |  | The parent Span depends on the child Span in some capacity |
| `follows_from` | development |  | The parent Span doesn't depend in any way on the result of the child Span |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
