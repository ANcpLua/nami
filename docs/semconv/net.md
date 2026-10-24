# `net` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `net.host.ip` | `string` | development | Replaced by `network.local.address`. | Deprecated, use `network.local.address`. | `"192.168.0.1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `net.host.name` | `string` | development | Replaced by `server.address`. | Deprecated, use `server.address`. | `"example.com"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `net.host.port` | `int` | development | Replaced by `server.port`. | Deprecated, use `server.port`. | `8080` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `net.peer.ip` | `string` | development | Replaced by `network.peer.address`. | Deprecated, use `network.peer.address`. | `"127.0.0.1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `net.peer.name` | `string` | development | Replaced by `server.address` on client spans and `client.address` on server spans. | Deprecated, use `server.address` on client spans and `client.address` on server spans. | `"example.com"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `net.peer.port` | `int` | development | Replaced by `server.port` on client spans and `client.port` on server spans. | Deprecated, use `server.port` on client spans and `client.port` on server spans. | `8080` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `net.protocol.name` | `string` | development | Replaced by `network.protocol.name`. | Deprecated, use `network.protocol.name`. | `"amqp"`, `"http"`, `"mqtt"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `net.protocol.version` | `string` | development | Replaced by `network.protocol.version`. | Deprecated, use `network.protocol.version`. | `"3.1.1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `net.sock.family` | `enum` | development | Split to `network.transport` and `network.type`. | Deprecated, use `network.transport` and `network.type`. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `net.sock.host.addr` | `string` | development | Replaced by `network.local.address`. | Deprecated, use `network.local.address`. | `"/var/my.sock"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `net.sock.host.port` | `int` | development | Replaced by `network.local.port`. | Deprecated, use `network.local.port`. | `8080` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `net.sock.peer.addr` | `string` | development | Replaced by `network.peer.address`. | Deprecated, use `network.peer.address`. | `"192.168.0.1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `net.sock.peer.name` | `string` | development | Removed. No replacement at this time. | Deprecated, no replacement at this time. | `"/var/my.sock"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `net.sock.peer.port` | `int` | development | Replaced by `network.peer.port`. | Deprecated, use `network.peer.port`. | `65531` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `net.transport` | `enum` | development | Replaced by `network.transport`. | Deprecated, use `network.transport`. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `net.sock.family` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `inet` | development |  | IPv4 address |
| `inet6` | development |  | IPv6 address |
| `unix` | development |  | UNIX domain socket path |

### `net.transport` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `inproc` | development |  | In-process communication. |
| `ip_tcp` | development |  | `ip_tcp`. |
| `ip_udp` | development |  | `ip_udp`. |
| `other` | development |  | Something else (non IP-based). |
| `pipe` | development |  | Named or anonymous pipe. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
