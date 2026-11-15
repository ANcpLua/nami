# `rabbitmq` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `rabbitmq.destination.routing_key` | `string` | development |  | RabbitMQ message routing key. | `"myKey"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `rabbitmq.message.delivery_tag` | `int` | development |  | RabbitMQ message delivery tag | `123` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
