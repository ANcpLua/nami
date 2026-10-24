# `server` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `server.address` | `string` | stable |  | Server domain name if available without reverse DNS lookup; otherwise, IP address or UNIX domain socket name. | `"example.com"`, `"10.1.2.80"`, `"/tmp/my.sock"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `server.port` | `int` | stable |  | Server port number. | `80`, `8080`, `443` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
