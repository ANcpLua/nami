# `peer` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `peer.service` | `string` | development | Use `server.service.name` on the client side or `client.service.name` on the server side. | The [`service.name`](/docs/resource/README.md#service) of the remote service. SHOULD be equal to the actual `service.name` resource attribute of the remote service if any. | `"AuthTokenCache"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
