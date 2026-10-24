# `jsonrpc` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `jsonrpc.protocol.version` | `string` | development |  | Protocol version, as specified in the `jsonrpc` property of the request and its corresponding response. | `"2.0"`, `"1.0"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `jsonrpc.request.id` | `string` | development |  | A string representation of the `id` property of the request and its corresponding response. | `"10"`, `"request-7"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
