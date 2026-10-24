# `user` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `user.email` | `string` | development |  | User email address. | `"a.einstein@example.com"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `user.full_name` | `string` | development |  | User's full name | `"Albert Einstein"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `user.hash` | `string` | development |  | Unique user hash to correlate information for a user in anonymized form. | `"364fc68eaf4c8acec74a4e52d7d1feaa"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `user.id` | `string` | development |  | Unique identifier of the user. | `"S-1-5-21-202424912787-2692429404-2351956786-1000"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `user.name` | `string` | development |  | Short name or login/username of the user. | `"a.einstein"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `user.roles` | `string[]` | development |  | Array of user roles at the time of the event. | `["admin","reporting_user"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
