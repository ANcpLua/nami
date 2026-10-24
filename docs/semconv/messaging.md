# `messaging` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `messaging.batch.message_count` | `int` | development |  | The number of messages sent, received, or processed in the scope of the batching operation. | `0`, `1`, `2` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.client.id` | `string` | development |  | A unique identifier for the client that consumes or produces a message. | `"client-5"`, `"myhost@8742@s8083jm"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.client_id` | `string` | development | Replaced by `messaging.client.id`. | Deprecated, use `messaging.client.id` instead. | `"client-5"`, `"myhost@8742@s8083jm"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.consumer.group.name` | `string` | development |  | The name of the consumer group with which a consumer is associated. | `"my-group"`, `"indexer"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.destination.anonymous` | `boolean` | development |  | A boolean that is true if the message destination is anonymous (could be unnamed or have auto-generated name). |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.destination.name` | `string` | development |  | The message destination name | `"MyQueue"`, `"MyTopic"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.destination.partition.id` | `string` | development |  | The identifier of the partition messages are sent to or received from, unique within the `messaging.destination.name`. | `"1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.destination.subscription.name` | `string` | development |  | The name of the destination subscription from which a message is consumed. | `"subscription-a"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.destination.template` | `string` | development |  | Low cardinality representation of the messaging destination name | `"/customers/{customerId}"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.destination.temporary` | `boolean` | development |  | A boolean that is true if the message destination is temporary and might not exist anymore after messages are processed. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.destination_publish.anonymous` | `boolean` | development | Removed. No replacement at this time. | Deprecated, no replacement at this time. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.destination_publish.name` | `string` | development | Removed. No replacement at this time. | Deprecated, no replacement at this time. | `"MyQueue"`, `"MyTopic"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.eventhubs.consumer.group` | `string` | development | Replaced by `messaging.consumer.group.name`. | Deprecated, use `messaging.consumer.group.name` instead. | `"$Default"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.eventhubs.message.enqueued_time` | `int` | development |  | The UTC epoch seconds at which the message has been accepted and stored in the entity. | `1701393730` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.gcp_pubsub.message.ack_deadline` | `int` | development |  | The ack deadline in seconds set for the modify ack deadline request. | `10` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.gcp_pubsub.message.ack_id` | `string` | development |  | The ack ID for a given message. | `"ack_id"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.gcp_pubsub.message.delivery_attempt` | `int` | development |  | The delivery attempt for a given message. | `2` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.gcp_pubsub.message.ordering_key` | `string` | development |  | The ordering key for a given message. If the attribute is not present, the message does not have an ordering key. | `"ordering_key"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.kafka.cluster.id` | `string` | development |  | The Kafka cluster ID, obtained from the broker metadata exposed through the Kafka client (or AdminClient) API. | `"MkU3OEVBNTcwNTJENDM2Qk"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.kafka.consumer.group` | `string` | development | Replaced by `messaging.consumer.group.name`. | Deprecated, use `messaging.consumer.group.name` instead. | `"my-group"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.kafka.destination.partition` | `int` | development | Record string representation of the partition ID in `messaging.destination.partition.id` attribute. | Deprecated, use `messaging.destination.partition.id` instead. | `2` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.kafka.message.key` | `string` | development |  | Message keys in Kafka are used for grouping alike messages to ensure they're processed on the same partition. They differ from `messaging.message.id` in that they're not unique. If the key is `null`, the attribute MUST NOT be set. | `"myKey"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.kafka.message.offset` | `int` | development | Replaced by `messaging.kafka.offset`. | Deprecated, use `messaging.kafka.offset` instead. | `42` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `true` |
| `messaging.kafka.message.tombstone` | `boolean` | development |  | A boolean that is true if the message is a tombstone. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.kafka.offset` | `int` | development |  | The offset of a record in the corresponding Kafka partition. | `42` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.message.body.size` | `int` | development |  | The size of the message body in bytes. | `1439` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `true` |
| `messaging.message.conversation_id` | `string` | development |  | The conversation ID identifying the conversation to which the message belongs, represented as a string. Sometimes called "Correlation ID". | `"MyConversationId"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.message.envelope.size` | `int` | development |  | The size of the message body and metadata in bytes. | `2738` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.message.id` | `string` | development |  | A value used by the messaging system as an identifier for the message, represented as a string. | `"452a7c7c7c7048c2f887f61572b18fc2"` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `true` |
| `messaging.operation` | `string` | development | Replaced by `messaging.operation.type`. | Deprecated, use `messaging.operation.type` instead. | `"publish"`, `"create"`, `"process"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.operation.name` | `string` | development |  | The system-specific name of the messaging operation. | `"ack"`, `"nack"`, `"send"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.operation.type` | `enum` | development |  | A string identifying the type of the messaging operation. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.rabbitmq.destination.routing_key` | `string` | development |  | RabbitMQ message routing key. | `"myKey"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.rabbitmq.message.delivery_tag` | `int` | development |  | RabbitMQ message delivery tag | `123` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.rocketmq.client_group` | `string` | development | Replaced by `messaging.consumer.group.name` on the consumer spans. No replacement for producer spans. | Deprecated, use `messaging.consumer.group.name` instead. | `"myConsumerGroup"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.rocketmq.consumption_model` | `enum` | development |  | Model of message consumption. This only applies to consumer spans. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.rocketmq.message.delay_time_level` | `int` | development |  | The delay time level for delay message, which determines the message delay time. | `3` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.rocketmq.message.delivery_timestamp` | `int` | development |  | The timestamp in milliseconds that the delay message is expected to be delivered to consumer. | `1665987217045` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.rocketmq.message.group` | `string` | development |  | It is essential for FIFO message. Messages that belong to the same message group are always processed one by one within the same consumer group. | `"myMessageGroup"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.rocketmq.message.keys` | `string[]` | development |  | Key(s) of message, another way to mark message besides message ID. | `["keyA","keyB"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.rocketmq.message.tag` | `string` | development |  | The secondary classifier of message besides topic. | `"tagA"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.rocketmq.message.type` | `enum` | development |  | Type of message. |  | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `true` |
| `messaging.rocketmq.namespace` | `string` | development |  | Namespace of RocketMQ resources, resources in different namespaces are individual. | `"myNamespace"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.servicebus.destination.subscription_name` | `string` | development | Replaced by `messaging.destination.subscription.name`. | Deprecated, use `messaging.destination.subscription.name` instead. | `"subscription-a"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.servicebus.disposition_status` | `enum` | development |  | Describes the [settlement type](https://learn.microsoft.com/azure/service-bus-messaging/message-transfers-locks-settlement#peeklock). |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.servicebus.message.delivery_count` | `int` | development |  | Number of deliveries that have been attempted for this message. | `2` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.servicebus.message.enqueued_time` | `int` | development |  | The UTC epoch seconds at which the message has been accepted and stored in the entity. | `1701393730` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `messaging.system` | `enum` | development |  | The messaging system as identified by the client instrumentation. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `messaging.operation.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `create` | development |  | A message is created. "Create" spans always refer to a single message and are used to provide a unique creation context for messages in batch sending scenarios. |
| `deliver` | development | Replaced by `process`. | Deprecated. Use `process` instead. |
| `process` | development |  | One or more messages are processed by a consumer. |
| `publish` | development | Replaced by `send`. | Deprecated. Use `send` instead. |
| `receive` | development |  | One or more messages are requested by a consumer. This operation refers to pull-based scenarios, where consumers explicitly call methods of messaging SDKs to receive messages. |
| `send` | development |  | One or more messages are provided for sending to an intermediary. If a single message is sent, the context of the "Send" span can be used as the creation context and no "Create" span needs to be created. |
| `settle` | development |  | One or more messages are settled. |

### `messaging.rocketmq.consumption_model` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `broadcasting` | development |  | Broadcasting consumption model |
| `clustering` | development |  | Clustering consumption model |

### `messaging.rocketmq.message.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `delay` | development |  | Delay message |
| `fifo` | development |  | FIFO message |
| `normal` | development |  | Normal message |
| `transaction` | development |  | Transaction message |

### `messaging.servicebus.disposition_status` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `abandon` | development |  | Message is abandoned |
| `complete` | development |  | Message is completed |
| `dead_letter` | development |  | Message is sent to dead letter queue |
| `defer` | development |  | Message is deferred |

### `messaging.system` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `activemq` | development |  | Apache ActiveMQ |
| `aws.sns` | development |  | Amazon Simple Notification Service (SNS) |
| `aws_sqs` | development |  | Amazon Simple Queue Service (SQS) |
| `eventgrid` | development |  | Azure Event Grid |
| `eventhubs` | development |  | Azure Event Hubs |
| `gcp_pubsub` | development |  | Google Cloud Pub/Sub |
| `jms` | development |  | Java Message Service |
| `kafka` | development |  | Apache Kafka |
| `pulsar` | development |  | Apache Pulsar |
| `rabbitmq` | development |  | RabbitMQ |
| `rocketmq` | development |  | Apache RocketMQ |
| `servicebus` | development |  | Azure Service Bus |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `messaging.client.consumed.messages` | counter | `{message}` | development |  | Number of messages that were delivered to the application. |
| `messaging.client.operation.duration` | histogram | `s` | development |  | Duration of messaging operation initiated by a producer or consumer client. |
| `messaging.client.published.messages` | counter | `{message}` | development | Replaced by `messaging.client.sent.messages`. | Deprecated. Use `messaging.client.sent.messages` instead. |
| `messaging.client.sent.messages` | counter | `{message}` | development |  | Number of messages producer attempted to send to the broker. |
| `messaging.process.duration` | histogram | `s` | development |  | Duration of processing operation. |
| `messaging.process.messages` | counter | `{message}` | development | Replaced by `messaging.client.consumed.messages`. | Deprecated. Use `messaging.client.consumed.messages` instead. |
| `messaging.publish.duration` | histogram | `s` | development | Replaced by `messaging.client.operation.duration`. | Deprecated. Use `messaging.client.operation.duration` instead. |
| `messaging.publish.messages` | counter | `{message}` | development | Replaced by `messaging.client.sent.messages`. | Deprecated. Use `messaging.client.sent.messages` instead. |
| `messaging.receive.duration` | histogram | `s` | development | Replaced by `messaging.client.operation.duration`. | Deprecated. Use `messaging.client.operation.duration` instead. |
| `messaging.receive.messages` | counter | `{message}` | development | Replaced by `messaging.client.consumed.messages`. | Deprecated. Use `messaging.client.consumed.messages` instead. |
