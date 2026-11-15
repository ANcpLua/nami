# `rocketmq` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `rocketmq.consumption_model` | `enum` | development |  | Model of message consumption. This only applies to consumer spans. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `rocketmq.message.delay_time_level` | `int` | development |  | The delay time level for delay message, which determines the message delay time. | `3` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `rocketmq.message.delivery_timestamp` | `int` | development |  | The timestamp in milliseconds that the delay message is expected to be delivered to consumer. | `1665987217045` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `rocketmq.message.group` | `string` | development |  | It is essential for FIFO message. Messages that belong to the same message group are always processed one by one within the same consumer group. | `"myMessageGroup"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `rocketmq.message.keys` | `string[]` | development |  | Key(s) of message, another way to mark message besides message ID. | `["keyA","keyB"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `rocketmq.message.tag` | `string` | development |  | The secondary classifier of message besides topic. | `"tagA"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `rocketmq.message.type` | `enum` | development |  | Type of message. |  | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"`, `nami.token_exempt`: `true` |
| `rocketmq.namespace` | `string` | development |  | Namespace of RocketMQ resources, resources in different namespaces are individual. | `"myNamespace"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |

### `rocketmq.consumption_model` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `broadcasting` | development |  | Broadcasting consumption model |
| `clustering` | development |  | Clustering consumption model |

### `rocketmq.message.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `delay` | development |  | Delay message |
| `fifo` | development |  | FIFO message |
| `normal` | development |  | Normal message |
| `transaction` | development |  | Transaction message |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
