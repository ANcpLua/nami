# `code` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `code.column` | `int` | development | Replaced by `code.column.number`. | Deprecated, use `code.column.number` | `16` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `code.column.number` | `int` | stable |  | The column number in `code.file.path` best representing the operation. It SHOULD point within the code unit named in `code.function.name`. This attribute MUST NOT be used on the Profile signal since the data is already captured in 'message Line'. This constraint is imposed to prevent redundancy and maintain data integrity. | `16` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `code.file.path` | `string` | stable |  | The source code filename that identifies the code unit as uniquely as possible (preferably an absolute file path). This attribute MUST NOT be used on the Profile signal since the data is already captured in 'message Function'. This constraint is imposed to prevent redundancy and maintain data integrity. | `"/usr/local/MyApplication/content_root/app/index.php"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `code.filepath` | `string` | development | Replaced by `code.file.path`. | Deprecated, use `code.file.path` instead | `"/usr/local/MyApplication/content_root/app/index.php"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `code.function` | `string` | development | Value should be included in `code.function.name` which is expected to be a fully-qualified name. | Deprecated, use `code.function.name` instead | `"serveRequest"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `code.function.name` | `string` | stable |  | The method or function fully-qualified name without arguments. The value should fit the natural representation of the language runtime, which is also likely the same used within `code.stacktrace` attribute value. This attribute MUST NOT be used on the Profile signal since the data is already captured in 'message Function'. This constraint is imposed to prevent redundancy and maintain data integrity. | `"com.example.MyHttpService.serveRequest"`, `"GuzzleHttp\\Client::transfer"`, `"fopen"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `code.line.number` | `int` | stable |  | The line number in `code.file.path` best representing the operation. It SHOULD point within the code unit named in `code.function.name`. This attribute MUST NOT be used on the Profile signal since the data is already captured in 'message Line'. This constraint is imposed to prevent redundancy and maintain data integrity. | `42` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `code.lineno` | `int` | development | Replaced by `code.line.number`. | Deprecated, use `code.line.number` instead | `42` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `code.namespace` | `string` | development | Value should be included in `code.function.name` which is expected to be a fully-qualified name. | Deprecated, namespace is now included into `code.function.name` | `"com.example.MyHttpService"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `code.stacktrace` | `string` | stable |  | A stacktrace as a string in the natural representation for the language runtime. The representation is identical to [`exception.stacktrace`](/docs/exceptions/exceptions-spans.md#stacktrace-representation). This attribute MUST NOT be used on the Profile signal since the data is already captured in 'message Location'. This constraint is imposed to prevent redundancy and maintain data integrity. | `"at com.example.GenerateTrace.methodB(GenerateTrace.java:13)\\n at com.example.GenerateTrace.methodA(GenerateTrace.java:9)\\n at com.example.GenerateTrace.main(GenerateTrace.java:5)\n"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
