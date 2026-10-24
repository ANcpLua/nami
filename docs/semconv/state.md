# `state` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `state` | `enum` | development | Replaced by `db.client.connection.state`. | Deprecated, use `db.client.connection.state` instead. | `"idle"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `idle` | development |  | `idle`. |
| `used` | development |  | `used`. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
