# `telemetry` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `telemetry.distro.name` | `string` | stable |  | The name of the auto instrumentation agent or distribution, if used. | `"parts-unlimited-java"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `telemetry.distro.version` | `string` | stable |  | The version string of the auto instrumentation agent or distribution, if used. | `"1.2.3"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `telemetry.sdk.language` | `enum` | stable |  | The language of the telemetry SDK. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `telemetry.sdk.name` | `string` | stable |  | The name of the telemetry SDK as defined above. | `"opentelemetry"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `telemetry.sdk.version` | `string` | stable |  | The version string of the telemetry SDK. | `"1.2.3"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `telemetry.sdk.language` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `cpp` | stable |  | [C++](https://opentelemetry.io/docs/languages/cpp/) |
| `dotnet` | stable |  | [.NET](https://opentelemetry.io/docs/languages/dotnet/) |
| `erlang` | stable |  | [Erlang/Elixir](https://opentelemetry.io/docs/languages/erlang/) |
| `go` | stable |  | [Go](https://opentelemetry.io/docs/languages/go/) |
| `java` | stable |  | [Java](https://opentelemetry.io/docs/languages/java/) |
| `kotlin` | stable |  | [Kotlin](https://opentelemetry.io/docs/languages/kotlin/) |
| `nodejs` | stable |  | [Node.js](https://opentelemetry.io/docs/languages/js/) |
| `php` | stable |  | [PHP](https://opentelemetry.io/docs/languages/php/) |
| `python` | stable |  | [Python](https://opentelemetry.io/docs/languages/python/) |
| `ruby` | stable |  | [Ruby](https://opentelemetry.io/docs/languages/ruby/) |
| `rust` | stable |  | [Rust](https://opentelemetry.io/docs/languages/rust/) |
| `swift` | stable |  | [Swift](https://opentelemetry.io/docs/languages/swift/) |
| `webjs` | stable |  | [Browser](https://opentelemetry.io/docs/languages/js/) |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
