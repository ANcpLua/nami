# `a2a` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `a2a.message.id` | `string` | development |  | The unique identifier of the A2A message. | `"msg-user-1234"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `a2a.message.reference_task_ids` | `string[]` | development |  | Task IDs referenced by the A2A request message. | `["task-abc-5678"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `a2a.method.name` | `enum` | development |  | The name of the A2A request method. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `a2a.protocol.version` | `string` | development |  | The version of the A2A protocol used. | `"1.0"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `a2a.task.id` | `string` | development |  | The unique identifier of an A2A task. | `"task-5f2a7c7d9f8e4d2a"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `a2a.task.state` | `enum` | development |  | The state of an A2A task. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `a2a.tenant` | `string` | development |  | The opaque routing identifier of the target [A2A tenant](https://github.com/a2aproject/A2A/blob/main/docs/topics/multi-tenancy.md). | `"billing"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |

### `a2a.method.name` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `CancelTask` | development |  | Request to cancel a task. |
| `CreateTaskPushNotificationConfig` | development |  | Request to create or update a task push notification configuration. |
| `DeleteTaskPushNotificationConfig` | development |  | Request to delete a task push notification configuration. |
| `GetExtendedAgentCard` | development |  | Request to get the extended agent card. |
| `GetTask` | development |  | Request to get the current state of a task. |
| `GetTaskPushNotificationConfig` | development |  | Request to get a task push notification configuration. |
| `ListTaskPushNotificationConfigs` | development |  | Request to list task push notification configurations. |
| `ListTasks` | development |  | Request to list tasks. |
| `SendMessage` | development |  | Request to send a message to an agent and receive a non-streaming response. |
| `SendStreamingMessage` | development |  | Request to send a message to an agent and receive a streaming response. |
| `SubscribeToTask` | development |  | Request to subscribe to task updates. |

### `a2a.task.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `TASK_STATE_AUTH_REQUIRED` | development |  | The task requires authentication. |
| `TASK_STATE_CANCELED` | development |  | The task was canceled. |
| `TASK_STATE_COMPLETED` | development |  | The task completed successfully. |
| `TASK_STATE_FAILED` | development |  | The task failed. |
| `TASK_STATE_INPUT_REQUIRED` | development |  | The task requires additional input. |
| `TASK_STATE_REJECTED` | development |  | The task was rejected. |
| `TASK_STATE_SUBMITTED` | development |  | The task has been submitted. |
| `TASK_STATE_UNSPECIFIED` | development |  | The task state is unspecified. |
| `TASK_STATE_WORKING` | development |  | The task is being processed. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
