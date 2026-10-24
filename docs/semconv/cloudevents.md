# `cloudevents` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `cloudevents.event_id` | `string` | development |  | The [event_id](https://github.com/cloudevents/spec/blob/v1.0.2/cloudevents/spec.md#id) uniquely identifies the event. | `"123e4567-e89b-12d3-a456-426614174000"`, `"0001"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cloudevents.event_source` | `string` | development |  | The [source](https://github.com/cloudevents/spec/blob/v1.0.2/cloudevents/spec.md#source-1) identifies the context in which an event happened. | `"https://github.com/cloudevents"`, `"/cloudevents/spec/pull/123"`, `"my-service"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cloudevents.event_spec_version` | `string` | development |  | The [version of the CloudEvents specification](https://github.com/cloudevents/spec/blob/v1.0.2/cloudevents/spec.md#specversion) which the event uses. | `"1.0"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cloudevents.event_subject` | `string` | development |  | The [subject](https://github.com/cloudevents/spec/blob/v1.0.2/cloudevents/spec.md#subject) of the event in the context of the event producer (identified by source). | `"mynewfile.jpg"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cloudevents.event_type` | `string` | development |  | The [event_type](https://github.com/cloudevents/spec/blob/v1.0.2/cloudevents/spec.md#type) contains a value describing the type of event related to the originating occurrence. | `"com.github.pull_request.opened"`, `"com.example.object.deleted.v2"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
