# `signalr` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `signalr.connection.status` | `enum` | stable |  | SignalR HTTP connection closure status. | `"app_shutdown"`, `"timeout"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `signalr.transport` | `enum` | stable |  | [SignalR transport type](https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/docs/specs/TransportProtocols.md) | `"web_sockets"`, `"long_polling"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `signalr.connection.status` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `app_shutdown` | stable |  | The connection was closed because the app is shutting down. |
| `normal_closure` | stable |  | The connection was closed normally. |
| `timeout` | stable |  | The connection was closed due to a timeout. |

### `signalr.transport` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `long_polling` | stable |  | LongPolling protocol |
| `server_sent_events` | stable |  | ServerSentEvents protocol |
| `web_sockets` | stable |  | WebSockets protocol |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `signalr.server.active_connections` | updowncounter | `{connection}` | stable |  | Number of connections that are currently active on the server. |
| `signalr.server.connection.duration` | histogram | `s` | stable |  | The duration of connections on the server. |
