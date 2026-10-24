# `kestrel` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples |
| --- | --- | --- | --- | --- | --- |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `kestrel.active_connections` | updowncounter | `{connection}` | stable |  | Number of connections that are currently active on the server. |
| `kestrel.active_tls_handshakes` | updowncounter | `{handshake}` | stable |  | Number of TLS handshakes that are currently in progress on the server. |
| `kestrel.connection.duration` | histogram | `s` | stable |  | The duration of connections on the server. |
| `kestrel.queued_connections` | updowncounter | `{connection}` | stable |  | Number of connections that are currently queued and are waiting to start. |
| `kestrel.queued_requests` | updowncounter | `{request}` | stable |  | Number of HTTP requests on multiplexed connections (HTTP/2 and HTTP/3) that are currently queued and are waiting to start. |
| `kestrel.rejected_connections` | counter | `{connection}` | stable |  | Number of connections rejected by the server. |
| `kestrel.tls_handshake.duration` | histogram | `s` | stable |  | The duration of TLS handshakes on the server. |
| `kestrel.upgraded_connections` | updowncounter | `{connection}` | stable |  | Number of connections that are currently upgraded (WebSockets). . |
