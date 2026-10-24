# `service` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `service.criticality` | `enum` | alpha |  | The operational criticality of the service. | `"critical"`, `"high"`, `"medium"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `service.instance.id` | `string` | stable |  | The string ID of the service instance. | `"627cc493-f310-47de-96bd-71410b7dec09"` | `nami.persisted`: `false`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `false` |
| `service.name` | `string` | stable |  | Logical name of the service. | `"shoppingcart"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `service.namespace` | `string` | stable |  | A namespace for `service.name`. | `"Shop"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `service.peer.name` | `string` | development |  | Logical name of the service on the other side of the connection. SHOULD be equal to the actual [`service.name`](/docs/resource/README.md#service) resource attribute of the remote service if any. | `"shoppingcart"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `service.peer.namespace` | `string` | development |  | Logical namespace of the service on the other side of the connection. SHOULD be equal to the actual [`service.namespace`](/docs/resource/README.md#service) resource attribute of the remote service if any. | `"Shop"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `service.version` | `string` | stable |  | The version string of the service component. The format is not defined by these conventions. | `"2.0.0"`, `"a01dbef8a"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `service.criticality` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `critical` | alpha |  | Service is business-critical; downtime directly impacts revenue, user experience, or core functionality. |
| `high` | alpha |  | Service is important but has degradation tolerance or fallback mechanisms. |
| `low` | alpha |  | Service is non-essential to core operations; used for background tasks or internal tools. |
| `medium` | alpha |  | Service provides supplementary functionality; degradation has limited user impact. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
