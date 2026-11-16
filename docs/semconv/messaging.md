# `messaging` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `messaging.batch.message_count` | `int` | development |  | The number of messages sent, received, or processed in the scope of the batching operation. | `0`, `1`, `2` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.client.id` | `string` | development |  | A unique identifier for the client that consumes or produces a message. | `"client-5"`, `"myhost@8742@s8083jm"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.client_id` | `string` | development | Replaced by `messaging.client.id`. | Deprecated, use `messaging.client.id` instead. | `"client-5"`, `"myhost@8742@s8083jm"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.consumer.group.name` | `string` | development |  | The name of the consumer group with which a consumer is associated. | `"my-group"`, `"indexer"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.destination.anonymous` | `boolean` | development |  | A boolean that is true if the message destination is anonymous (could be unnamed or have auto-generated name). |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.destination.name` | `string` | development |  | The message destination name | `"MyQueue"`, `"MyTopic"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.destination.partition.id` | `string` | development |  | The identifier of the partition messages are sent to or received from, unique within the `messaging.destination.name`. | `"1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.destination.subscription.name` | `string` | development |  | The name of the destination subscription from which a message is consumed. | `"subscription-a"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.destination.template` | `string` | development |  | Low cardinality representation of the messaging destination name | `"/customers/{customerId}"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.destination.temporary` | `boolean` | development |  | A boolean that is true if the message destination is temporary and might not exist anymore after messages are processed. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.destination_publish.anonymous` | `boolean` | development | Removed. No replacement at this time. | Deprecated, no replacement at this time. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.destination_publish.name` | `string` | development | Removed. No replacement at this time. | Deprecated, no replacement at this time. | `"MyQueue"`, `"MyTopic"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.eventhubs.consumer.group` | `string` | development | Replaced by `messaging.consumer.group.name`. | Deprecated, use `messaging.consumer.group.name` instead. | `"$Default"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.eventhubs.message.enqueued_time` | `int` | development | Replaced by `azure.eventhubs.message.enqueued_time`. | Deprecated, use `azure.eventhubs.message.enqueued_time` instead. | `1701393730` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.gcp_pubsub.message.ack_deadline` | `int` | development | Replaced by `gcp.pubsub.message.ack_deadline`. | Deprecated, use `gcp.pubsub.message.ack_deadline` instead. | `10` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.gcp_pubsub.message.ack_id` | `string` | development | Replaced by `gcp.pubsub.message.ack_id`. | Deprecated, use `gcp.pubsub.message.ack_id` instead. | `"ack_id"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.gcp_pubsub.message.delivery_attempt` | `int` | development | Replaced by `gcp.pubsub.message.delivery_attempt`. | Deprecated, use `gcp.pubsub.message.delivery_attempt` instead. | `2` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.gcp_pubsub.message.ordering_key` | `string` | development | Replaced by `gcp.pubsub.message.ordering_key`. | Deprecated, use `gcp.pubsub.message.ordering_key` instead. | `"ordering_key"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.kafka.cluster.id` | `string` | development | Replaced by `kafka.cluster.id`. | Deprecated, use `kafka.cluster.id` instead. | `"MkU3OEVBNTcwNTJENDM2Qk"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.kafka.consumer.group` | `string` | development | Replaced by `messaging.consumer.group.name`. | Deprecated, use `messaging.consumer.group.name` instead. | `"my-group"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.kafka.destination.partition` | `int` | development | Record string representation of the partition ID in `messaging.destination.partition.id` attribute. | Deprecated, use `messaging.destination.partition.id` instead. | `2` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.kafka.message.key` | `string` | development | Replaced by `kafka.message.key`. | Deprecated, use `kafka.message.key` instead. | `"myKey"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.kafka.message.offset` | `int` | development | Replaced by `kafka.offset`. | Deprecated, use `kafka.offset` instead. | `42` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.kafka.message.tombstone` | `boolean` | development | Replaced by `kafka.message.tombstone`. | Deprecated, use `kafka.message.tombstone` instead. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.kafka.offset` | `int` | development | Replaced by `kafka.offset`. | Deprecated, use `kafka.offset` instead. | `42` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.message.body.size` | `int` | development |  | The size of the message body in bytes. | `1439` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.message.conversation_id` | `string` | development |  | The conversation ID identifying the conversation to which the message belongs, represented as a string. Sometimes called "Correlation ID". | `"MyConversationId"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.message.envelope.size` | `int` | development |  | The size of the message body and metadata in bytes. | `2738` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.message.id` | `string` | development |  | A value used by the messaging system as an identifier for the message, represented as a string. | `"452a7c7c7c7048c2f887f61572b18fc2"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.operation` | `string` | development | Replaced by `messaging.operation.type`. | Deprecated, use `messaging.operation.type` instead. | `"publish"`, `"create"`, `"process"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.operation.name` | `string` | development |  | The system-specific name of the messaging operation. | `"ack"`, `"nack"`, `"send"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.operation.type` | `enum` | development |  | A string identifying the type of the messaging operation. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.rabbitmq.destination.routing_key` | `string` | development | Replaced by `rabbitmq.destination.routing_key`. | Deprecated, use `rabbitmq.destination.routing_key` instead. | `"myKey"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.rabbitmq.message.delivery_tag` | `int` | development | Replaced by `rabbitmq.message.delivery_tag`. | Deprecated, use `rabbitmq.message.delivery_tag` instead. | `123` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.rocketmq.client_group` | `string` | development | Replaced by `messaging.consumer.group.name` on the consumer spans. No replacement for producer spans. | Deprecated, use `messaging.consumer.group.name` instead. | `"myConsumerGroup"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.rocketmq.consumption_model` | `enum` | development | Replaced by `rocketmq.consumption_model`. | Deprecated, use `rocketmq.consumption_model` instead. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.rocketmq.message.delay_time_level` | `int` | development | Replaced by `rocketmq.message.delay_time_level`. | Deprecated, use `rocketmq.message.delay_time_level` instead. | `3` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.rocketmq.message.delivery_timestamp` | `int` | development | Replaced by `rocketmq.message.delivery_timestamp`. | Deprecated, use `rocketmq.message.delivery_timestamp` instead. | `1665987217045` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.rocketmq.message.group` | `string` | development | Replaced by `rocketmq.message.group`. | Deprecated, use `rocketmq.message.group` instead. | `"myMessageGroup"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.rocketmq.message.keys` | `string[]` | development | Replaced by `rocketmq.message.keys`. | Deprecated, use `rocketmq.message.keys` instead. | `["keyA","keyB"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.rocketmq.message.tag` | `string` | development | Replaced by `rocketmq.message.tag`. | Deprecated, use `rocketmq.message.tag` instead. | `"tagA"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.rocketmq.message.type` | `enum` | development | Replaced by `rocketmq.message.type`. | Deprecated, use `rocketmq.message.type` instead. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.rocketmq.namespace` | `string` | development | Replaced by `rocketmq.namespace`. | Deprecated, use `rocketmq.namespace` instead. | `"myNamespace"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.servicebus.destination.subscription_name` | `string` | development | Replaced by `messaging.destination.subscription.name`. | Deprecated, use `messaging.destination.subscription.name` instead. | `"subscription-a"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.servicebus.disposition_status` | `enum` | development | Replaced by `azure.servicebus.disposition_status`. | Deprecated, use `azure.servicebus.disposition_status` instead. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.servicebus.message.delivery_count` | `int` | development | Replaced by `azure.servicebus.message.delivery_count`. | Deprecated, use `azure.servicebus.message.delivery_count` instead. | `2` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.servicebus.message.enqueued_time` | `int` | development | Replaced by `azure.servicebus.message.enqueued_time`. | Deprecated, use `azure.servicebus.message.enqueued_time` instead. | `1701393730` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.system` | `enum` | development | Replaced by `messaging.system.name`. | Deprecated, use `messaging.system.name` instead. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `messaging.system.name` | `enum` | development |  | The messaging system as identified by the client instrumentation. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |

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

### `messaging.system.name` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `activemq` | development |  | Apache ActiveMQ |
| `aws.sns` | development |  | Amazon Simple Notification Service (SNS) |
| `aws.sqs` | development |  | Amazon Simple Queue Service (SQS) |
| `azure.eventgrid` | development |  | Azure Event Grid |
| `azure.eventhubs` | development |  | Azure Event Hubs |
| `azure.servicebus` | development |  | Azure Service Bus |
| `gcp.pubsub` | development |  | Google Cloud Pub/Sub |
| `jms` | development |  | Java Message Service |
| `kafka` | development |  | Apache Kafka |
| `pulsar` | development |  | Apache Pulsar |
| `rabbitmq` | development |  | RabbitMQ |
| `rocketmq` | development |  | Apache RocketMQ |

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

## Events

| Name | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `messaging.create.exception` | development |  | This event represents an exception that occurred during a messaging create operation. |
| `messaging.process.exception` | development |  | This event represents an exception that occurred during messaging process operations. |
| `messaging.receive.exception` | development |  | This event represents an exception that occurred during a messaging receive operation. |
| `messaging.send.exception` | development |  | This event represents an exception that occurred during a messaging send operation. |
| `messaging.settle.exception` | development |  | This event represents an exception that occurred during a messaging settle operation. |

## Spans

| Type | Kind | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- |
| `messaging.create.producer` | producer | development |  | Describes a message being created or passed to a client library for sending. |
| `messaging.eventhubs.generic.client` | client | development |  | Describes an Azure Event Hubs client operation that is not covered by a more specific (create, send, receive, process, or settle) span. |
| `messaging.process.consumer` | consumer | development |  | Describes one or more messages being processed by a consumer (push-based). |
| `messaging.receive.client` | client | development |  | Describes one or more messages being requested by a consumer (pull-based). |
| `messaging.send.client` | client | development |  | Describes one or more messages being sent to an intermediary when a separate message creation context ("Create" span or custom context) already exists. |
| `messaging.send.producer` | producer | development |  | Describes one or more messages being sent to an intermediary, where the "Send" span's context is used as the message creation context. |
| `messaging.servicebus.generic.client` | client | development |  | Describes an Azure Service Bus client operation that is not covered by a more specific (create, send, receive, process, or settle) span. |
| `messaging.settle.client` | client | development |  | Describes one or more messages being settled. |
