# `mcp` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `mcp.method.name` | `enum` | development |  | The name of the request or notification method. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `mcp.protocol.version` | `string` | development |  | The [version](https://modelcontextprotocol.io/specification/versioning) of the Model Context Protocol used. | `"2025-06-18"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `mcp.resource.uri` | `string` | development |  | The value of the resource uri. | `"postgres://database/customers/schema"`, `"file:///home/user/documents/report.pdf"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `mcp.session.id` | `string` | development |  | Identifies [MCP session](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports#session-management). | `"191c4850af6c49e08843a3f6c80e5046"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |

### `mcp.method.name` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `completion/complete` | development |  | Request to complete a prompt. |
| `elicitation/create` | development |  | Request from the server to elicit additional information from the user via the client |
| `initialize` | development |  | Request to initialize the MCP client. |
| `logging/setLevel` | development |  | Request to set the logging level. |
| `notifications/cancelled` | development |  | Notification cancelling a previously-issued request. |
| `notifications/initialized` | development |  | Notification indicating that the MCP client has been initialized. |
| `notifications/message` | development |  | Notification indicating that a message has been received. |
| `notifications/progress` | development |  | Notification indicating the progress for a long-running operation. |
| `notifications/prompts/list_changed` | development |  | Notification indicating that the list of prompts has changed. |
| `notifications/resources/list_changed` | development |  | Notification indicating that the list of resources has changed. |
| `notifications/resources/updated` | development |  | Notification indicating that a resource has been updated. |
| `notifications/roots/list_changed` | development |  | Notification indicating that the list of roots has changed. |
| `notifications/tools/list_changed` | development |  | Notification indicating that the list of tools has changed. |
| `ping` | development |  | Request to check that the other party is still alive. |
| `prompts/get` | development |  | Request to get a prompt. |
| `prompts/list` | development |  | Request to list prompts available on server. |
| `resources/list` | development |  | Request to list resources available on server. |
| `resources/read` | development |  | Request to read a resource. |
| `resources/subscribe` | development |  | Request to subscribe to a resource. |
| `resources/templates/list` | development |  | Request to list resource templates available on server. |
| `resources/unsubscribe` | development |  | Request to unsubscribe from resource updates. |
| `roots/list` | development |  | Request to list roots available on server. |
| `sampling/createMessage` | development |  | Request to create a sampling message. |
| `tools/call` | development |  | Request to call a tool. |
| `tools/list` | development |  | Request to list tools available on server. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `mcp.client.operation.duration` | histogram | `s` | development |  | The duration of the MCP request or notification as observed on the sender from the time it was sent until the response or ack is received. |
| `mcp.client.session.duration` | histogram | `s` | development |  | The duration of the MCP session as observed on the MCP client. |
| `mcp.server.operation.duration` | histogram | `s` | development |  | MCP request or notification duration as observed on the receiver from the time it was received until the result or ack is sent. |
| `mcp.server.session.duration` | histogram | `s` | development |  | The duration of the MCP session as observed on the MCP server. |
