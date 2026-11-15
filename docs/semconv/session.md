# `session` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `session.id` | `string` | development |  | A unique ID to identify a session. | `"00112233-4455-6677-8899-aabbccddeeff"` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"`, `nami.token_exempt`: `false` |
| `session.previous_id` | `string` | development |  | The previous `session.id` for this user, when known. | `"00112233-4455-6677-8899-aabbccddeeff"` | `nami.persisted`: `false`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"`, `nami.token_exempt`: `false` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |

## Events

| Name | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `session.end` | development |  | Indicates that a session has ended. |
| `session.start` | development |  | Indicates that a new session has been started, optionally linking to the prior session. |
