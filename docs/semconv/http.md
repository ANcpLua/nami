# `http` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `http.client_ip` | `string` | development | Replaced by `client.address`. | Deprecated, use `client.address` instead. | `"83.164.160.102"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.connection.state` | `enum` | development |  | State of the HTTP connection in the HTTP connection pool. | `"active"`, `"idle"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.flavor` | `enum` | development | Split into `network.protocol.name` and `network.protocol.version` | Deprecated, use `network.protocol.name` and `network.protocol.version` instead. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.host` | `string` | development | Replaced by one of `server.address`, `client.address` or `http.request.header.host`, depending on the usage. | Deprecated, use one of `server.address`, `client.address` or `http.request.header.host` instead, depending on the usage. | `"www.example.org"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.method` | `string` | development | Replaced by `http.request.method`. | Deprecated, use `http.request.method` instead. | `"GET"`, `"POST"`, `"HEAD"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.request.body.size` | `int` | development |  | The size of the request payload body in bytes. This is the number of bytes transferred excluding headers and is often, but not always, present as the [Content-Length](https://www.rfc-editor.org/rfc/rfc9110.html#field.content-length) header. For requests using transport encoding, this should be the compressed size. | `3495` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.request.header` | `template[string[]]` | stable |  | HTTP request headers, `<key>` being the normalized HTTP Header name (lowercase), the value being the header values. | `["application/json"]`, `["1.2.3.4","1.2.3.5"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.request.method` | `enum` | stable |  | HTTP request method. | `"GET"`, `"POST"`, `"HEAD"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.request.method_original` | `string` | stable |  | Original HTTP method sent by the client in the request line. | `"GeT"`, `"ACL"`, `"foo"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.request.resend_count` | `int` | stable |  | The ordinal number of request resending attempt (for any reason, including redirects). | `3` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.request.size` | `int` | development |  | The total size of the request in bytes. This should be the total number of bytes sent over the wire, including the request line (HTTP/1.1), framing (HTTP/2 and HTTP/3), headers, and request body if any. | `1437` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.request_content_length` | `int` | development | Replaced by `http.request.header.content-length`. | Deprecated, use `http.request.header.content-length` instead. | `3495` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.request_content_length_uncompressed` | `int` | development | Replaced by `http.request.body.size`. | Deprecated, use `http.request.body.size` instead. | `5493` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.response.body.size` | `int` | development |  | The size of the response payload body in bytes. This is the number of bytes transferred excluding headers and is often, but not always, present as the [Content-Length](https://www.rfc-editor.org/rfc/rfc9110.html#field.content-length) header. For requests using transport encoding, this should be the compressed size. | `3495` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.response.header` | `template[string[]]` | stable |  | HTTP response headers, `<key>` being the normalized HTTP Header name (lowercase), the value being the header values. | `["application/json"]`, `["abc","def"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.response.size` | `int` | development |  | The total size of the response in bytes. This should be the total number of bytes sent over the wire, including the status line (HTTP/1.1), framing (HTTP/2 and HTTP/3), headers, and response body and trailers if any. | `1437` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.response.status_code` | `int` | stable |  | [HTTP response status code](https://tools.ietf.org/html/rfc7231#section-6). | `200` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.response_content_length` | `int` | development | Replaced by `http.response.header.content-length`. | Deprecated, use `http.response.header.content-length` instead. | `3495` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.response_content_length_uncompressed` | `int` | development | Replaced by `http.response.body.size`. | Deprecated, use `http.response.body.size` instead. | `5493` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.route` | `string` | stable |  | The matched route template for the request. This MUST be low-cardinality and include all static path segments, with dynamic path segments represented with placeholders. | `"/users/:userID?"`, `"my-controller/my-action/{id?}"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.scheme` | `string` | development | Replaced by `url.scheme`. | Deprecated, use `url.scheme` instead. | `"http"`, `"https"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.server_name` | `string` | development | Replaced by `server.address`. | Deprecated, use `server.address` instead. | `"example.com"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.status_code` | `int` | development | Replaced by `http.response.status_code`. | Deprecated, use `http.response.status_code` instead. | `200` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.target` | `string` | development | Split to `url.path` and `url.query`. | Deprecated, use `url.path` and `url.query` instead. | `"/search?q=OpenTelemetry#SemConv"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.url` | `string` | development | Replaced by `url.full`. | Deprecated, use `url.full` instead. | `"https://www.foo.bar/search?q=OpenTelemetry#SemConv"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `http.user_agent` | `string` | development | Replaced by `user_agent.original`. | Deprecated, use `user_agent.original` instead. | `"CERN-LineMode/2.15 libwww/2.17b3"`, `"Mozilla/5.0 (iPhone; CPU iPhone OS 14_7_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/14.1.2 Mobile/15E148 Safari/604.1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `http.connection.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `active` | development |  | active state. |
| `idle` | development |  | idle state. |

### `http.flavor` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `1.0` | development |  | HTTP/1.0 |
| `1.1` | development |  | HTTP/1.1 |
| `2.0` | development |  | HTTP/2 |
| `3.0` | development |  | HTTP/3 |
| `QUIC` | development |  | QUIC protocol. |
| `SPDY` | development |  | SPDY protocol. |

### `http.request.method` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `CONNECT` | stable |  | CONNECT method. |
| `DELETE` | stable |  | DELETE method. |
| `GET` | stable |  | GET method. |
| `HEAD` | stable |  | HEAD method. |
| `OPTIONS` | stable |  | OPTIONS method. |
| `_OTHER` | stable |  | Any HTTP method that the instrumentation has no prior knowledge of. |
| `PATCH` | stable |  | PATCH method. |
| `POST` | stable |  | POST method. |
| `PUT` | stable |  | PUT method. |
| `QUERY` | development |  | QUERY method. |
| `TRACE` | stable |  | TRACE method. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `http.client.active_requests` | updowncounter | `{request}` | development |  | Number of active HTTP requests. |
| `http.client.connection.duration` | histogram | `s` | development |  | The duration of the successfully established outbound HTTP connections. |
| `http.client.open_connections` | updowncounter | `{connection}` | development |  | Number of outbound HTTP connections that are currently active or idle on the client. |
| `http.client.request.body.size` | histogram | `By` | development |  | Size of HTTP client request bodies. |
| `http.client.request.duration` | histogram | `s` | stable |  | Duration of HTTP client requests. |
| `http.client.response.body.size` | histogram | `By` | development |  | Size of HTTP client response bodies. |
| `http.server.active_requests` | updowncounter | `{request}` | development |  | Number of active HTTP server requests. |
| `http.server.request.body.size` | histogram | `By` | development |  | Size of HTTP server request bodies. |
| `http.server.request.duration` | histogram | `s` | stable |  | Duration of HTTP server requests. |
| `http.server.response.body.size` | histogram | `By` | development |  | Size of HTTP server response bodies. |
