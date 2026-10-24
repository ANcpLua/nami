# `enduser` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `enduser.id` | `string` | development |  | Unique identifier of an end user in the system. It maybe a username, email address, or other identifier. | `"username"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `enduser.pseudo.id` | `string` | development |  | Pseudonymous identifier of an end user. This identifier should be a random value that is not directly linked or associated with the end user's actual identity. | `"QdH5CAWJgqVT4rOr0qtumf"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `enduser.role` | `string` | development | Use `user.roles` instead. | Deprecated, use `user.roles` instead. | `"admin"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `enduser.scope` | `string` | development | Removed, no replacement at this time. | Deprecated, no replacement at this time. | `"read:message, write:files"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
