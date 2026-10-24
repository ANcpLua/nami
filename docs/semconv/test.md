# `test` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `test.case.name` | `string` | development |  | The fully qualified human readable name of the [test case](https://wikipedia.org/wiki/Test_case). | `"org.example.TestCase1.test1"`, `"example/tests/TestCase1.test1"`, `"ExampleTestCase1_test1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `test.case.result.status` | `enum` | development |  | The status of the actual test case result from test execution. | `"pass"`, `"fail"` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `true` |
| `test.suite.name` | `string` | development |  | The human readable name of a [test suite](https://wikipedia.org/wiki/Test_suite). | `"TestSuite1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `test.suite.run.status` | `enum` | development |  | The status of the test suite run. | `"success"`, `"failure"`, `"skipped"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `test.case.result.status` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `fail` | development |  | fail |
| `pass` | development |  | pass |

### `test.suite.run.status` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `aborted` | development |  | aborted |
| `failure` | development |  | failure |
| `in_progress` | development |  | in_progress |
| `skipped` | development |  | skipped |
| `success` | development |  | success |
| `timed_out` | development |  | timed_out |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
