# `server` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `server.address` | `string` | stable |  | Server domain name if available without reverse DNS lookup; otherwise, IP address or UNIX domain socket name. | `"example.com"`, `"10.1.2.80"`, `"/tmp/my.sock"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `server.as.number` | `int` | development |  | The [Autonomous System Number (ASN)](https://www.iana.org/assignments/as-numbers/as-numbers.xhtml) associated with the server IP address. | `64496`, `15169` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `server.as.organization` | `string` | development |  | The name of the organization associated with the autonomous system of the server IP address. | `"Example Networks Inc."`, `"Google LLC"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `server.mac.address` | `string` | development |  | Server Media Access Control (MAC) address. | `"00-00-5E-00-53-23"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `server.port` | `int` | stable |  | Server port number. | `80`, `8080`, `443` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `server.prefix` | `string` | development |  | The IP prefix associated with the server IP address, in CIDR notation. | `"192.0.2.0/24"`, `"2001:db8::/32"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `server.service.name` | `string` | development |  | Logical name of the service acting as the server in a connection-based network interaction. | `"payments"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `server.service.namespace` | `string` | development |  | Logical namespace of the service acting as the server in a connection-based network interaction. | `"Shop"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
