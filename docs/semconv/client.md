# `client` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `client.address` | `string` | stable |  | Client address - domain name if available without reverse DNS lookup; otherwise, IP address or UNIX domain socket name. | `"client.example.com"`, `"10.1.2.80"`, `"/tmp/my.sock"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `client.as.number` | `int` | development |  | The [Autonomous System Number (ASN)](https://www.iana.org/assignments/as-numbers/as-numbers.xhtml) associated with the client IP address. | `64496`, `15169` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `client.as.organization` | `string` | development |  | The name of the organization associated with the autonomous system of the client IP address. | `"Example Networks Inc."`, `"Google LLC"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `client.mac.address` | `string` | development |  | Client Media Access Control (MAC) address. | `"00-00-5E-00-53-23"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `client.port` | `int` | stable |  | Client port number. | `65123` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `client.prefix` | `string` | development |  | The IP prefix associated with the client IP address, in CIDR notation. | `"192.0.2.0/24"`, `"2001:db8::/32"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `client.service.name` | `string` | development |  | Logical name of the service acting as the client in a connection-based network interaction. | `"shoppingcart"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `client.service.namespace` | `string` | development |  | Logical namespace of the service acting as the client in a connection-based network interaction. | `"Shop"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
