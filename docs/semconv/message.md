# `message` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `message.compressed_size` | `int` | development | Deprecated, no replacement at this time. | Deprecated, no replacement at this time. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `message.id` | `int` | development | Deprecated, no replacement at this time. | Deprecated, no replacement at this time. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `message.type` | `enum` | development | Deprecated, no replacement at this time. | Deprecated, no replacement at this time. |  | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `true` |
| `message.uncompressed_size` | `int` | development | Deprecated, no replacement at this time. | Deprecated, no replacement at this time. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `message.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `RECEIVED` | development |  | `RECEIVED`. |
| `SENT` | development |  | `SENT`. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
