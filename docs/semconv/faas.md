# `faas` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `faas.coldstart` | `boolean` | development |  | A boolean that is true if the serverless function is executed for the first time (aka cold-start). |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `faas.cron` | `string` | development |  | A string containing the schedule period as [Cron Expression](https://docs.oracle.com/cd/E12058_01/doc/doc.1014/e12030/cron_expressions.htm). | `"0/5 * * * ? *"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `faas.document.collection` | `string` | development |  | The name of the source on which the triggering operation was performed. For example, in Cloud Storage or S3 corresponds to the bucket name, and in Cosmos DB to the database name. | `"myBucketName"`, `"myDbName"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `faas.document.name` | `string` | development |  | The document name/table subjected to the operation. For example, in Cloud Storage or S3 is the name of the file, and in Cosmos DB the table name. | `"myFile.txt"`, `"myTableName"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `faas.document.operation` | `enum` | development |  | Describes the type of the operation that was performed on the data. |  | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `true` |
| `faas.document.time` | `string` | development |  | A string containing the time when the data was accessed in the [ISO 8601](https://www.iso.org/iso-8601-date-and-time-format.html) format expressed in [UTC](https://www.w3.org/TR/NOTE-datetime). | `"2020-01-23T13:47:06Z"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `faas.instance` | `string` | development |  | The execution environment ID as a string, that will be potentially reused for other invocations to the same function/function version. | `"2021/06/28/[$LATEST]2f399eb14537447da05ab2a2e39309de"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `faas.invocation_id` | `string` | development |  | The invocation ID of the current function invocation. | `"af9d5aa4-a685-4c5f-a22b-444f80b3cc28"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `faas.invoked_name` | `string` | development |  | The name of the invoked function. | `"my-function"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `faas.invoked_provider` | `enum` | development |  | The cloud provider of the invoked function. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `faas.invoked_region` | `string` | development |  | The cloud region of the invoked function. | `"eu-central-1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `faas.max_memory` | `int` | development |  | The amount of memory available to the serverless function converted to Bytes. | `134217728` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `faas.name` | `string` | development |  | The name of the single function that this runtime instance executes. | `"my-function"`, `"myazurefunctionapp/some-function-name"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `faas.time` | `string` | development |  | A string containing the function invocation time in the [ISO 8601](https://www.iso.org/iso-8601-date-and-time-format.html) format expressed in [UTC](https://www.w3.org/TR/NOTE-datetime). | `"2020-01-23T13:47:06Z"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `faas.trigger` | `enum` | development |  | Type of the trigger which caused this function invocation. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `faas.version` | `string` | development |  | The immutable version of the function being executed. | `"26"`, `"pinkfroid-00002"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `faas.document.operation` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `delete` | development |  | When an object is deleted. |
| `edit` | development |  | When an object is modified. |
| `insert` | development |  | When a new object is created. |

### `faas.invoked_provider` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `alibaba_cloud` | development |  | Alibaba Cloud |
| `aws` | development |  | Amazon Web Services |
| `azure` | development |  | Microsoft Azure |
| `gcp` | development |  | Google Cloud Platform |
| `tencent_cloud` | development |  | Tencent Cloud |

### `faas.trigger` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `datasource` | development |  | A response to some data source operation such as a database or filesystem read/write |
| `http` | development |  | To provide an answer to an inbound HTTP request |
| `other` | development |  | If none of the others apply |
| `pubsub` | development |  | A function is set to be executed when messages are sent to a messaging system |
| `timer` | development |  | A function is scheduled to be executed regularly |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `faas.coldstarts` | counter | `{coldstart}` | development |  | Number of invocation cold starts. |
| `faas.cpu_usage` | histogram | `s` | development |  | Distribution of CPU usage per invocation. |
| `faas.errors` | counter | `{error}` | development |  | Number of invocation errors. |
| `faas.init_duration` | histogram | `s` | development |  | Measures the duration of the function's initialization, such as a cold start. |
| `faas.invocations` | counter | `{invocation}` | development |  | Number of successful invocations. |
| `faas.invoke_duration` | histogram | `s` | development |  | Measures the duration of the function's logic execution. |
| `faas.mem_usage` | histogram | `By` | development |  | Distribution of max memory usage per invocation. |
| `faas.net_io` | histogram | `By` | development |  | Distribution of net I/O usage per invocation. |
| `faas.timeouts` | counter | `{timeout}` | development |  | Number of invocation timeouts. |
