# `feature_flag` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `feature_flag.context.id` | `string` | release_candidate |  | The unique identifier for the flag evaluation context. For example, the targeting key. | `"5157782b-2203-4c80-a857-dbbd5e7761db"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `feature_flag.error.message` | `string` | release_candidate |  | A message providing more detail about an error that occurred during feature flag evaluation in human-readable form. | `"Unexpected input type: string"`, `"The user has exceeded their storage quota"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `feature_flag.evaluation.error.message` | `string` | development | Replaced by `feature_flag.error.message`. | Deprecated, use `feature_flag.error.message` instead. | `"Flag 'header-color' expected type 'string' but found type 'number'"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `feature_flag.evaluation.reason` | `enum` | development | Replaced by `feature_flag.result.reason`. | Deprecated, use `feature_flag.result.reason` instead. | `"static"`, `"targeting_match"`, `"error"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `feature_flag.key` | `string` | release_candidate |  | The lookup key of the feature flag. | `"logo-color"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `feature_flag.provider.name` | `string` | release_candidate |  | Identifies the feature flag provider. | `"Flag Manager"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `feature_flag.provider_name` | `string` | development | Replaced by `feature_flag.provider.name`. | Deprecated, use `feature_flag.provider.name` instead. | `"Flag Manager"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `feature_flag.result.reason` | `enum` | release_candidate |  | The reason code which shows how a feature flag value was determined. | `"static"`, `"targeting_match"`, `"error"` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `true` |
| `feature_flag.result.value` | `any` | release_candidate |  | The evaluated value of the feature flag. | `"#ff0000"`, `true`, `3` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `feature_flag.result.variant` | `string` | release_candidate |  | A semantic identifier for an evaluated flag value. | `"red"`, `"true"`, `"on"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `feature_flag.set.id` | `string` | release_candidate |  | The identifier of the [flag set](https://openfeature.dev/specification/glossary/#flag-set) to which the feature flag belongs. | `"proj-1"`, `"ab98sgs"`, `"service1/dev"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `feature_flag.variant` | `string` | development | Replaced by `feature_flag.result.variant`. | Deprecated, use `feature_flag.result.variant` instead. | `"red"`, `"true"`, `"on"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `feature_flag.version` | `string` | release_candidate |  | The version of the ruleset used during the evaluation. This may be any stable value which uniquely identifies the ruleset. | `"1"`, `"01ABCDEF"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `feature_flag.evaluation.reason` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `cached` | development |  | The resolved value was retrieved from cache. |
| `default` | development |  | The resolved value fell back to a pre-configured value (no dynamic evaluation occurred or dynamic evaluation yielded no result). |
| `disabled` | development |  | The resolved value was the result of the flag being disabled in the management system. |
| `error` | development |  | The resolved value was the result of an error. |
| `split` | development |  | The resolved value was the result of pseudorandom assignment. |
| `stale` | development |  | The resolved value is non-authoritative or possibly out of date |
| `static` | development |  | The resolved value is static (no dynamic evaluation). |
| `targeting_match` | development |  | The resolved value was the result of a dynamic evaluation, such as a rule or specific user-targeting. |
| `unknown` | development |  | The reason for the resolved value could not be determined. |

### `feature_flag.result.reason` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `cached` | release_candidate |  | The resolved value was retrieved from cache. |
| `default` | release_candidate |  | The resolved value fell back to a pre-configured value (no dynamic evaluation occurred or dynamic evaluation yielded no result). |
| `disabled` | release_candidate |  | The resolved value was the result of the flag being disabled in the management system. |
| `error` | release_candidate |  | The resolved value was the result of an error. |
| `split` | release_candidate |  | The resolved value was the result of pseudorandom assignment. |
| `stale` | release_candidate |  | The resolved value is non-authoritative or possibly out of date |
| `static` | release_candidate |  | The resolved value is static (no dynamic evaluation). |
| `targeting_match` | release_candidate |  | The resolved value was the result of a dynamic evaluation, such as a rule or specific user-targeting. |
| `unknown` | release_candidate |  | The reason for the resolved value could not be determined. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
