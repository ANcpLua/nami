# `service` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `service.criticality` | `enum` | alpha |  | The operational criticality of the service. | `"critical"`, `"high"`, `"medium"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `service.instance.cost_center.id` | `string` | development |  | Identifier of the cost center associated with the service instance. | `"94002"`, `"111111111111"`, `"cc-eng-402"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `service.instance.cost_center.name` | `string` | development |  | Name of the cost center associated with the service instance. | `"engineering-platform"`, `"marketing-campaigns"`, `"research-and-development"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `service.instance.id` | `string` | stable |  | The string ID of the service instance. | `"627cc493-f310-47de-96bd-71410b7dec09"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `service.name` | `string` | stable |  | Logical name of the service. | `"shoppingcart"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `service.namespace` | `string` | stable |  | A namespace for `service.name`. | `"Shop"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `service.peer.name` | `string` | development | Use `server.service.name` on the client side or `client.service.name` on the server side. | Logical name of the service on the other side of the connection. SHOULD be equal to the actual [`service.name`](/docs/resource/README.md#service) resource attribute of the remote service if any. | `"shoppingcart"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `service.peer.namespace` | `string` | development | Use `server.service.namespace` on the client side or `client.service.namespace` on the server side. | Logical namespace of the service on the other side of the connection. SHOULD be equal to the actual [`service.namespace`](/docs/resource/README.md#service) resource attribute of the remote service if any. | `"Shop"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `service.version` | `string` | stable |  | The version string of the service component. The format is not defined by these conventions. | `"2.0.0"`, `"a01dbef8a"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |

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
