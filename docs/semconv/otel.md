# `otel` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `otel.component.name` | `string` | development |  | A name uniquely identifying the instance of the OpenTelemetry component within its containing SDK instance. | `"otlp_grpc_span_exporter/0"`, `"custom-name"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `otel.component.type` | `enum` | development |  | A name identifying the type of the OpenTelemetry component. | `"batching_span_processor"`, `"com.example.MySpanExporter"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `otel.event.name` | `string` | stable |  | Identifies the class / type of event. | `"browser.mouse.click"`, `"device.app.lifecycle"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `otel.library.name` | `string` | development | Replaced by `otel.scope.name`. | Deprecated. Use the `otel.scope.name` attribute | `"io.opentelemetry.contrib.mongodb"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `otel.library.version` | `string` | development | Replaced by `otel.scope.version`. | Deprecated. Use the `otel.scope.version` attribute. | `"1.0.0"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `otel.scope.name` | `string` | stable |  | The name of the instrumentation scope - (`InstrumentationScope.Name` in OTLP). | `"io.opentelemetry.contrib.mongodb"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `otel.scope.schema_url` | `string` | development |  | The schema URL of the instrumentation scope. | `"https://opentelemetry.io/schemas/1.31.0"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `otel.scope.version` | `string` | stable |  | The version of the instrumentation scope - (`InstrumentationScope.Version` in OTLP). | `"1.0.0"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `otel.span.parent.origin` | `enum` | development |  | Determines whether the span has a parent span, and if so, [whether it is a remote parent](https://opentelemetry.io/docs/specs/otel/trace/api/#isremote) |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `otel.span.sampling_result` | `enum` | development |  | The result value of the sampler for this span |  | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `true` |
| `otel.status_code` | `enum` | stable |  | Name of the code, either "OK" or "ERROR". MUST NOT be set if the status code is UNSET. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `otel.status_description` | `string` | stable |  | Description of the Status if it has a value, otherwise not set. | `"resource not found"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `otel.component.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `batching_log_processor` | development |  | The built-in SDK batching log record processor |
| `batching_span_processor` | development |  | The built-in SDK batching span processor |
| `otlp_grpc_log_exporter` | development |  | OTLP log record exporter over gRPC with protobuf serialization |
| `otlp_grpc_metric_exporter` | development |  | OTLP metric exporter over gRPC with protobuf serialization |
| `otlp_grpc_span_exporter` | development |  | OTLP span exporter over gRPC with protobuf serialization |
| `otlp_http_json_log_exporter` | development |  | OTLP log record exporter over HTTP with JSON serialization |
| `otlp_http_json_metric_exporter` | development |  | OTLP metric exporter over HTTP with JSON serialization |
| `otlp_http_json_span_exporter` | development |  | OTLP span exporter over HTTP with JSON serialization |
| `otlp_http_log_exporter` | development |  | OTLP log record exporter over HTTP with protobuf serialization |
| `otlp_http_metric_exporter` | development |  | OTLP metric exporter over HTTP with protobuf serialization |
| `otlp_http_span_exporter` | development |  | OTLP span exporter over HTTP with protobuf serialization |
| `periodic_metric_reader` | development |  | The built-in SDK periodically exporting metric reader |
| `prometheus_http_text_metric_exporter` | development |  | Prometheus metric exporter over HTTP with the default text-based format |
| `simple_log_processor` | development |  | The built-in SDK simple log record processor |
| `simple_span_processor` | development |  | The built-in SDK simple span processor |
| `zipkin_http_span_exporter` | development |  | Zipkin span exporter over HTTP |

### `otel.span.parent.origin` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `local` | development |  | The span has a parent and the parent's span context [isRemote()](https://opentelemetry.io/docs/specs/otel/trace/api/#isremote) is false |
| `none` | development |  | The span does not have a parent, it is a root span |
| `remote` | development |  | The span has a parent and the parent's span context [isRemote()](https://opentelemetry.io/docs/specs/otel/trace/api/#isremote) is true |

### `otel.span.sampling_result` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `DROP` | development |  | The span is not sampled and not recording |
| `RECORD_AND_SAMPLE` | development |  | The span is sampled and recording |
| `RECORD_ONLY` | development |  | The span is not sampled, but recording |

### `otel.status_code` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `ERROR` | stable |  | The operation contains an error. |
| `OK` | stable |  | The operation has been validated by an Application developer or Operator to have completed successfully. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `otel.sdk.exporter.log.exported` | counter | `{log_record}` | development |  | The number of log records for which the export has finished, either successful or failed. |
| `otel.sdk.exporter.log.inflight` | updowncounter | `{log_record}` | development |  | The number of log records which were passed to the exporter, but that have not been exported yet (neither successful, nor failed). |
| `otel.sdk.exporter.metric_data_point.exported` | counter | `{data_point}` | development |  | The number of metric data points for which the export has finished, either successful or failed. |
| `otel.sdk.exporter.metric_data_point.inflight` | updowncounter | `{data_point}` | development |  | The number of metric data points which were passed to the exporter, but that have not been exported yet (neither successful, nor failed). |
| `otel.sdk.exporter.operation.duration` | histogram | `s` | development |  | The duration of exporting a batch of telemetry records. |
| `otel.sdk.exporter.span.exported` | counter | `{span}` | development |  | The number of spans for which the export has finished, either successful or failed. |
| `otel.sdk.exporter.span.exported.count` | updowncounter | `{span}` | development | Replaced by `otel.sdk.exporter.span.exported`. | Deprecated, use `otel.sdk.exporter.span.exported` instead. |
| `otel.sdk.exporter.span.inflight` | updowncounter | `{span}` | development |  | The number of spans which were passed to the exporter, but that have not been exported yet (neither successful, nor failed). |
| `otel.sdk.exporter.span.inflight.count` | updowncounter | `{span}` | development | Replaced by `otel.sdk.exporter.span.inflight`. | Deprecated, use `otel.sdk.exporter.span.inflight` instead. |
| `otel.sdk.log.created` | counter | `{log_record}` | development |  | The number of logs submitted to enabled SDK Loggers. |
| `otel.sdk.metric_reader.collection.duration` | histogram | `s` | development |  | The duration of the collect operation of the metric reader. |
| `otel.sdk.processor.log.processed` | counter | `{log_record}` | development |  | The number of log records for which the processing has finished, either successful or failed. |
| `otel.sdk.processor.log.queue.capacity` | updowncounter | `{log_record}` | development |  | The maximum number of log records the queue of a given instance of an SDK Log Record processor can hold. |
| `otel.sdk.processor.log.queue.size` | updowncounter | `{log_record}` | development |  | The number of log records in the queue of a given instance of an SDK log processor. |
| `otel.sdk.processor.span.processed` | counter | `{span}` | development |  | The number of spans for which the processing has finished, either successful or failed. |
| `otel.sdk.processor.span.processed.count` | updowncounter | `{span}` | development | Replaced by `otel.sdk.processor.span.processed`. | Deprecated, use `otel.sdk.processor.span.processed` instead. |
| `otel.sdk.processor.span.queue.capacity` | updowncounter | `{span}` | development |  | The maximum number of spans the queue of a given instance of an SDK span processor can hold. |
| `otel.sdk.processor.span.queue.size` | updowncounter | `{span}` | development |  | The number of spans in the queue of a given instance of an SDK span processor. |
| `otel.sdk.span.ended` | counter | `{span}` | development | Obsoleted. | Use `otel.sdk.span.started` minus `otel.sdk.span.live` to derive this value. |
| `otel.sdk.span.ended.count` | counter | `{span}` | development | Obsoleted. | Use `otel.sdk.span.started` minus `otel.sdk.span.live` to derive this value. |
| `otel.sdk.span.live` | updowncounter | `{span}` | development |  | The number of created spans with `recording=true` for which the end operation has not been called yet. |
| `otel.sdk.span.live.count` | updowncounter | `{span}` | development | Replaced by `otel.sdk.span.live`. | Deprecated, use `otel.sdk.span.live` instead. |
| `otel.sdk.span.started` | counter | `{span}` | development |  | The number of created spans. |
