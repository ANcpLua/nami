# `kafka` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `kafka.cluster.id` | `string` | development |  | The Kafka cluster ID, obtained from the broker metadata exposed through the Kafka client (or AdminClient) API. | `"MkU3OEVBNTcwNTJENDM2Qk"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `kafka.message.key` | `string` | development |  | Message keys in Kafka are used for grouping alike messages to ensure they're processed on the same partition. They differ from `messaging.message.id` in that they're not unique. If the key is `null`, the attribute MUST NOT be set. | `"myKey"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `kafka.message.tombstone` | `boolean` | development |  | A boolean that is true if the message is a tombstone. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `kafka.offset` | `int` | development |  | The offset of a record in the corresponding Kafka partition. | `42` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
