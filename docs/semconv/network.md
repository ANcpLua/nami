# `network` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `network.carrier.icc` | `string` | development |  | The ISO 3166-1 alpha-2 2-character country code associated with the mobile carrier network. | `"DE"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.carrier.mcc` | `string` | development |  | The mobile carrier country code. | `"310"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.carrier.mnc` | `string` | development |  | The mobile carrier network code. | `"001"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.carrier.name` | `string` | development |  | The name of the mobile carrier. | `"sprint"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.connection.state` | `enum` | release_candidate |  | The state of network connection | `"close_wait"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.connection.subtype` | `enum` | development |  | This describes more details regarding the connection.type. It may be the type of cell technology connection, but it could be used for describing details about a Wi-Fi connection. | `"LTE"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.connection.type` | `enum` | development |  | The internet connection type. | `"wifi"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.interface.name` | `string` | release_candidate |  | The network interface name. | `"lo"`, `"eth0"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.io.direction` | `enum` | release_candidate |  | The direction of traffic from the perspective of the observing host's physical or virtual network interface. It should not be used to represent the logical direction of a stateful connection or network flow. | `"transmit"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.local.address` | `string` | stable |  | Local address of the network connection - IP address or UNIX domain socket name. | `"10.1.2.80"`, `"/tmp/my.sock"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.local.as.number` | `int` | development |  | The [Autonomous System Number (ASN)](https://www.iana.org/assignments/as-numbers/as-numbers.xhtml) associated with the local IP address. | `64496`, `15169` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.local.as.organization` | `string` | development |  | The name of the organization associated with the autonomous system of the local IP address. | `"Example Networks Inc."`, `"Google LLC"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.local.mac.address` | `string` | development |  | Local Media Access Control (MAC) address of the network connection. | `"00-00-5E-00-53-23"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.local.port` | `int` | stable |  | Local port number of the network connection. | `65123` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.local.prefix` | `string` | development |  | The IP prefix associated with the local IP address, in CIDR notation. | `"192.0.2.0/24"`, `"2001:db8::/32"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.peer.address` | `string` | stable |  | Peer address of the network connection - IP address or UNIX domain socket name. | `"10.1.2.80"`, `"/tmp/my.sock"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.peer.as.number` | `int` | development |  | The [Autonomous System Number (ASN)](https://www.iana.org/assignments/as-numbers/as-numbers.xhtml) associated with the peer IP address. | `64496`, `15169` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.peer.as.organization` | `string` | development |  | The name of the organization associated with the autonomous system of the peer IP address. | `"Example Networks Inc."`, `"Google LLC"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.peer.mac.address` | `string` | development |  | Peer Media Access Control (MAC) address of the network connection. | `"00-00-5E-00-53-23"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.peer.port` | `int` | stable |  | Peer port number of the network connection. | `65123` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.peer.prefix` | `string` | development |  | The IP prefix associated with the peer IP address, in CIDR notation. | `"192.0.2.0/24"`, `"2001:db8::/32"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.protocol.name` | `string` | stable |  | [OSI application layer](https://wikipedia.org/wiki/Application_layer) or non-OSI equivalent. | `"amqp"`, `"http"`, `"mqtt"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.protocol.version` | `string` | stable |  | The actual version of the protocol used for network communication. | `"1.1"`, `"2"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.transport` | `enum` | stable |  | [OSI transport layer](https://wikipedia.org/wiki/Transport_layer) or [inter-process communication method](https://wikipedia.org/wiki/Inter-process_communication). | `"tcp"`, `"udp"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `network.type` | `enum` | stable |  | [OSI network layer](https://wikipedia.org/wiki/Network_layer) or non-OSI equivalent. | `"ipv4"`, `"ipv6"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |

### `network.connection.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `close_wait` | release_candidate |  | `close_wait`. |
| `closed` | release_candidate |  | `closed`. |
| `closing` | release_candidate |  | `closing`. |
| `established` | release_candidate |  | `established`. |
| `fin_wait_1` | release_candidate |  | `fin_wait_1`. |
| `fin_wait_2` | release_candidate |  | `fin_wait_2`. |
| `last_ack` | release_candidate |  | `last_ack`. |
| `listen` | release_candidate |  | `listen`. |
| `syn_received` | release_candidate |  | `syn_received`. |
| `syn_sent` | release_candidate |  | `syn_sent`. |
| `time_wait` | release_candidate |  | `time_wait`. |

### `network.connection.subtype` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `cdma` | development |  | CDMA |
| `cdma2000_1xrtt` | development |  | CDMA2000 1XRTT |
| `edge` | development |  | EDGE |
| `ehrpd` | development |  | EHRPD |
| `evdo_0` | development |  | EVDO Rel. 0 |
| `evdo_a` | development |  | EVDO Rev. A |
| `evdo_b` | development |  | EVDO Rev. B |
| `gprs` | development |  | GPRS |
| `gsm` | development |  | GSM |
| `hsdpa` | development |  | HSDPA |
| `hspa` | development |  | HSPA |
| `hspap` | development |  | HSPAP |
| `hsupa` | development |  | HSUPA |
| `iden` | development |  | IDEN |
| `iwlan` | development |  | IWLAN |
| `lte` | development |  | LTE |
| `lte_ca` | development |  | LTE CA |
| `nr` | development |  | 5G NR (New Radio) |
| `nrnsa` | development |  | 5G NRNSA (New Radio Non-Standalone) |
| `td_scdma` | development |  | TD-SCDMA |
| `umts` | development |  | UMTS |

### `network.connection.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `cell` | development |  | `cell`. |
| `unavailable` | development |  | `unavailable`. |
| `unknown` | development |  | `unknown`. |
| `wifi` | development |  | Wi-Fi |
| `wired` | development |  | `wired`. |

### `network.io.direction` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `receive` | release_candidate |  | `receive`. |
| `transmit` | release_candidate |  | `transmit`. |

### `network.transport` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `pipe` | stable |  | Named or anonymous pipe. |
| `quic` | stable |  | QUIC |
| `tcp` | stable |  | TCP |
| `udp` | stable |  | UDP |
| `unix` | stable |  | UNIX domain socket |

### `network.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `ipv4` | stable |  | IPv4 |
| `ipv6` | stable |  | IPv6 |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
