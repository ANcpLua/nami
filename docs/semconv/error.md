# `error` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `error.message` | `string` | development | Use domain-specific error message attribute. For example, use `feature_flag.error.message` for feature flag errors. | A message providing more detail about an error in human-readable form. | `"Unexpected input type: string"`, `"The user has exceeded their storage quota"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `error.type` | `enum` | stable |  | Describes a class of error the operation ended with. | `"timeout"`, `"java.net.UnknownHostException"`, `"server_certificate_invalid"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `error.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `_OTHER` | stable |  | A fallback error value to be used when the instrumentation doesn't define a custom value. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
