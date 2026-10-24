# `exception` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `exception.escaped` | `boolean` | stable | It's no longer recommended to record exceptions that are handled and do not escape the scope of a span. | Indicates that the exception is escaping the scope of the span. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `exception.message` | `string` | stable |  | The exception message. | `"Division by zero"`, `"Can't convert 'int' object to str implicitly"` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `true` |
| `exception.stacktrace` | `string` | stable |  | A stacktrace as a string in the natural representation for the language runtime. The representation is to be determined and documented by each language SIG. | `"Exception in thread \"main\" java.lang.RuntimeException: Test exception\\n at com.example.GenerateTrace.methodB(GenerateTrace.java:13)\\n at com.example.GenerateTrace.methodA(GenerateTrace.java:9)\\n at com.example.GenerateTrace.main(GenerateTrace.java:5)\n"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `exception.type` | `string` | stable |  | The type of the exception (its fully-qualified class name, if applicable). The dynamic type of the exception should be preferred over the static type in languages that support it. | `"java.net.ConnectException"`, `"OSError"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
