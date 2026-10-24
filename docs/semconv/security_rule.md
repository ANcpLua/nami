# `security_rule` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `security_rule.category` | `string` | development |  | A categorization value keyword used by the entity using the rule for detection of this event | `"Attempted Information Leak"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `security_rule.description` | `string` | development |  | The description of the rule generating the event. | `"Block requests to public DNS over HTTPS / TLS protocols"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `security_rule.license` | `string` | development |  | Name of the license under which the rule used to generate this event is made available. | `"Apache 2.0"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `security_rule.name` | `string` | development |  | The name of the rule or signature generating the event. | `"BLOCK_DNS_over_TLS"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `security_rule.reference` | `string` | development |  | Reference URL to additional information about the rule used to generate this event. | `"https://en.wikipedia.org/wiki/DNS_over_TLS"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `security_rule.ruleset.name` | `string` | development |  | Name of the ruleset, policy, group, or parent category in which the rule used to generate this event is a member. | `"Standard_Protocol_Filters"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `security_rule.uuid` | `string` | development |  | A rule ID that is unique within the scope of a set or group of agents, observers, or other entities using the rule for detection of this event. | `"550e8400-e29b-41d4-a716-446655440000"`, `"1100110011"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `security_rule.version` | `string` | development |  | The version / revision of the rule being used for analysis. | `"1.0.0"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
