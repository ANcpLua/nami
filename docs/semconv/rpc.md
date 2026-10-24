# `rpc` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `rpc.connect_rpc.error_code` | `enum` | development | Replaced by `rpc.response.status_code`. | Deprecated, use `rpc.response.status_code` attribute instead. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `rpc.connect_rpc.request.metadata` | `template[string[]]` | development | Replaced by `rpc.request.metadata`. | Deprecated, use `rpc.request.metadata` instead. | `["1.2.3.4","1.2.3.5"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `rpc.connect_rpc.response.metadata` | `template[string[]]` | development | Replaced by `rpc.response.metadata`. | Deprecated, use `rpc.response.metadata` instead. | `["attribute_value"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `rpc.grpc.request.metadata` | `template[string[]]` | development | Replaced by `rpc.request.metadata`. | Deprecated, use `rpc.request.metadata` instead. | `["1.2.3.4","1.2.3.5"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `rpc.grpc.response.metadata` | `template[string[]]` | development | Replaced by `rpc.response.metadata`. | Deprecated, use `rpc.response.metadata` instead. | `["attribute_value"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `rpc.grpc.status_code` | `enum` | development | Use string representation of the gRPC status code on the `rpc.response.status_code` attribute. | Deprecated, use string representation on the `rpc.response.status_code` attribute instead. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `rpc.jsonrpc.error_code` | `int` | development | Use string representation of the error code on the `rpc.response.status_code` attribute. | Deprecated, use string representation on the `rpc.response.status_code` attribute instead. | `-32700`, `100` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `rpc.jsonrpc.error_message` | `string` | development | Use the span status description when reporting JSON-RPC spans. | Deprecated, use the span status description when reporting JSON-RPC spans. | `"Parse error"`, `"User already exists"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `rpc.jsonrpc.request_id` | `string` | development | Replaced by `jsonrpc.request.id`. | Deprecated, use `jsonrpc.request.id` instead. | `"10"`, `"request-7"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `rpc.jsonrpc.version` | `string` | development | Replaced by `jsonrpc.protocol.version`. | Deprecated, use `jsonrpc.protocol.version` instead. | `"2.0"`, `"1.0"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `rpc.message.compressed_size` | `int` | development | Deprecated, no replacement at this time. | Compressed size of the message in bytes. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `rpc.message.id` | `int` | development | Deprecated, no replacement at this time. | MUST be calculated as two different counters starting from `1` one for sent messages and one for received message. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `rpc.message.type` | `enum` | development | Deprecated, no replacement at this time. | Whether this is a received or sent message. |  | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `true` |
| `rpc.message.uncompressed_size` | `int` | development | Deprecated, no replacement at this time. | Uncompressed size of the message in bytes. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `rpc.method` | `string` | release_candidate |  | The fully-qualified logical name of the method from the RPC interface perspective. | `"com.example.ExampleService/exampleMethod"`, `"EchoService/Echo"`, `"_OTHER"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `rpc.method_original` | `string` | release_candidate |  | The original name of the method used by the client. | `"com.myservice.EchoService/catchAll"`, `"com.myservice.EchoService/unknownMethod"`, `"InvalidMethod"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `rpc.request.metadata` | `template[string[]]` | development |  | RPC request metadata, `<key>` being the normalized RPC metadata key (lowercase), the value being the metadata values. | `["1.2.3.4","1.2.3.5"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `rpc.response.metadata` | `template[string[]]` | development |  | RPC response metadata, `<key>` being the normalized RPC metadata key (lowercase), the value being the metadata values. | `["attribute_value"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `rpc.response.status_code` | `string` | release_candidate |  | Status code of the RPC returned by the RPC server or generated by the client | `"OK"`, `"DEADLINE_EXCEEDED"`, `"-32602"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `rpc.service` | `string` | development | Value should be included in `rpc.method` which is expected to be a fully-qualified name. | Deprecated, use fully-qualified `rpc.method` instead. | `"myservice.EchoService"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `rpc.system` | `enum` | development | Replaced by `rpc.system.name`. | Deprecated, use `rpc.system.name` attribute instead. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `rpc.system.name` | `enum` | release_candidate |  | The Remote Procedure Call (RPC) system. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `rpc.connect_rpc.error_code` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `aborted` | development |  | `aborted`. |
| `already_exists` | development |  | `already_exists`. |
| `cancelled` | development |  | `cancelled`. |
| `data_loss` | development |  | `data_loss`. |
| `deadline_exceeded` | development |  | `deadline_exceeded`. |
| `failed_precondition` | development |  | `failed_precondition`. |
| `internal` | development |  | `internal`. |
| `invalid_argument` | development |  | `invalid_argument`. |
| `not_found` | development |  | `not_found`. |
| `out_of_range` | development |  | `out_of_range`. |
| `permission_denied` | development |  | `permission_denied`. |
| `resource_exhausted` | development |  | `resource_exhausted`. |
| `unauthenticated` | development |  | `unauthenticated`. |
| `unavailable` | development |  | `unavailable`. |
| `unimplemented` | development |  | `unimplemented`. |
| `unknown` | development |  | `unknown`. |

### `rpc.grpc.status_code` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `10` | development |  | ABORTED |
| `6` | development |  | ALREADY_EXISTS |
| `1` | development |  | CANCELLED |
| `15` | development |  | DATA_LOSS |
| `4` | development |  | DEADLINE_EXCEEDED |
| `9` | development |  | FAILED_PRECONDITION |
| `13` | development |  | INTERNAL |
| `3` | development |  | INVALID_ARGUMENT |
| `5` | development |  | NOT_FOUND |
| `0` | development |  | OK |
| `11` | development |  | OUT_OF_RANGE |
| `7` | development |  | PERMISSION_DENIED |
| `8` | development |  | RESOURCE_EXHAUSTED |
| `16` | development |  | UNAUTHENTICATED |
| `14` | development |  | UNAVAILABLE |
| `12` | development |  | UNIMPLEMENTED |
| `2` | development |  | UNKNOWN |

### `rpc.message.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `RECEIVED` | development |  | `RECEIVED`. |
| `SENT` | development |  | `SENT`. |

### `rpc.system` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `apache_dubbo` | development |  | Apache Dubbo |
| `connect_rpc` | development |  | Connect RPC |
| `dotnet_wcf` | development |  | .NET WCF |
| `grpc` | development |  | gRPC |
| `java_rmi` | development |  | Java RMI |
| `jsonrpc` | development |  | JSON-RPC |
| `onc_rpc` | development |  | [ONC RPC (Sun RPC)](https://datatracker.ietf.org/doc/html/rfc5531) |

### `rpc.system.name` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `connectrpc` | development |  | [Connect RPC](https://connectrpc.com/) |
| `dubbo` | release_candidate |  | [Apache Dubbo](https://dubbo.apache.org/) |
| `grpc` | release_candidate |  | [gRPC](https://grpc.io/) |
| `jsonrpc` | development |  | [JSON-RPC](https://www.jsonrpc.org/) |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `rpc.client.call.duration` | histogram | `s` | release_candidate |  | Measures the duration of an outgoing Remote Procedure Call (RPC). |
| `rpc.client.duration` | histogram | `ms` | development | Replaced by `rpc.client.call.duration` with unit `s`. | Deprecated, use `rpc.client.call.duration` instead. Note: the unit also changed from `ms` to `s`. |
| `rpc.client.request.size` | histogram | `By` | development | Removed, no replacement at this time. | Measures the size of RPC request messages (uncompressed). |
| `rpc.client.requests_per_rpc` | histogram | `{count}` | development | Removed, no replacement at this time. | Measures the number of messages received per RPC. |
| `rpc.client.response.size` | histogram | `By` | development | Removed, no replacement at this time. | Measures the size of RPC response messages (uncompressed). |
| `rpc.client.responses_per_rpc` | histogram | `{count}` | development | Removed, no replacement at this time. | Measures the number of messages sent per RPC. |
| `rpc.server.call.duration` | histogram | `s` | release_candidate |  | Measures the duration of an incoming Remote Procedure Call (RPC). |
| `rpc.server.duration` | histogram | `ms` | development | Replaced by `rpc.server.call.duration` with unit `s`. | Deprecated, use `rpc.server.call.duration` instead. Note: the unit also changed from `ms` to `s`. |
| `rpc.server.request.size` | histogram | `By` | development | Removed, no replacement at this time. | Measures the size of RPC request messages (uncompressed). |
| `rpc.server.requests_per_rpc` | histogram | `{count}` | development | Removed, no replacement at this time. | Measures the number of messages received per RPC. |
| `rpc.server.response.size` | histogram | `By` | development | Removed, no replacement at this time. | Measures the size of RPC response messages (uncompressed). |
| `rpc.server.responses_per_rpc` | histogram | `{count}` | development | Removed, no replacement at this time. | Measures the number of messages sent per RPC. |
