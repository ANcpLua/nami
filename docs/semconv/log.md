# `log` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `log.file.name` | `string` | development |  | The basename of the file. | `"audit.log"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `log.file.name_resolved` | `string` | development |  | The basename of the file, with symlinks resolved. | `"uuid.log"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `log.file.path` | `string` | development |  | The full path to the file. | `"/var/log/mysql/audit.log"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `log.file.path_resolved` | `string` | development |  | The full path to the file, with symlinks resolved. | `"/var/lib/docker/uuid.log"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `log.iostream` | `enum` | development |  | The stream associated with the log. See below for a list of well-known values. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `log.record.original` | `string` | development |  | The complete original Log Record. | `"77 <86>1 2015-08-06T21:58:59.694Z 192.168.2.133 inactive - - - Something happened"`, `"[INFO] 8/3/24 12:34:56 Something happened"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `log.record.uid` | `string` | development |  | A unique identifier for the Log Record. | `"01ARZ3NDEKTSV4RRFFQ69G5FAV"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `log.iostream` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `stderr` | development |  | Events from stderr stream |
| `stdout` | development |  | Logs from stdout stream |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
