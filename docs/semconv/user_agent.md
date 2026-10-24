# `user_agent` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `user_agent.name` | `string` | development |  | Name of the user-agent extracted from original. Usually refers to the browser's name. | `"Safari"`, `"YourApp"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `user_agent.original` | `string` | stable |  | Value of the [HTTP User-Agent](https://www.rfc-editor.org/rfc/rfc9110.html#field.user-agent) header sent by the client. | `"CERN-LineMode/2.15 libwww/2.17b3"`, `"Mozilla/5.0 (iPhone; CPU iPhone OS 14_7_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/14.1.2 Mobile/15E148 Safari/604.1"`, `"YourApp/1.0.0 grpc-java-okhttp/1.27.2"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `user_agent.os.name` | `string` | development |  | Human readable operating system name. | `"iOS"`, `"Android"`, `"Ubuntu"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `user_agent.os.version` | `string` | development |  | The version string of the operating system as defined in [Version Attributes](/docs/resource/README.md#version-attributes). | `"14.2.1"`, `"18.04.1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `user_agent.synthetic.type` | `enum` | development |  | Specifies the category of synthetic traffic, such as tests or bots. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `user_agent.version` | `string` | development |  | Version of the user-agent extracted from original. Usually refers to the browser's version | `"14.1.2"`, `"1.0.0"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `user_agent.synthetic.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `bot` | development |  | Bot source. |
| `test` | development |  | Synthetic test source. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
