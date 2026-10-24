# `thread` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `thread.id` | `int` | development |  | Current "managed" thread ID (as opposed to OS thread ID). | `42` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `thread.name` | `string` | development |  | Current thread name. | `"main"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
