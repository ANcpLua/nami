# `client` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `client.address` | `string` | stable |  | Client address - domain name if available without reverse DNS lookup; otherwise, IP address or UNIX domain socket name. | `"client.example.com"`, `"10.1.2.80"`, `"/tmp/my.sock"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `client.port` | `int` | stable |  | Client port number. | `65123` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
