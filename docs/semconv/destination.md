# `destination` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `destination.address` | `string` | development |  | Destination address as observed at the point of instrumentation - typically an IP address, or a UNIX domain socket name; a domain name only when the receiver was addressed by name (for example a peer-to-peer node dialed by hostname), never obtained via reverse DNS lookup. | `"10.1.2.80"`, `"destination.example.com"`, `"/tmp/my.sock"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `destination.as.number` | `int` | development |  | The [Autonomous System Number (ASN)](https://www.iana.org/assignments/as-numbers/as-numbers.xhtml) associated with the destination IP address. | `64496`, `15169` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `destination.as.organization` | `string` | development |  | The name of the organization associated with the autonomous system of the destination IP address. | `"Example Networks Inc."`, `"Google LLC"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `destination.mac.address` | `string` | development |  | Destination Media Access Control (MAC) address. | `"00-00-5E-00-53-23"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `destination.port` | `int` | development |  | Destination port number | `3389`, `2888` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `destination.prefix` | `string` | development |  | The IP prefix associated with the destination IP address, in CIDR notation. | `"192.0.2.0/24"`, `"2001:db8::/32"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
