# `cassandra` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `cassandra.consistency.level` | `enum` | development |  | The consistency level of the query. Based on consistency values from [CQL](https://docs.datastax.com/en/cassandra-oss/3.0/cassandra/dml/dmlConfigConsistency.html). |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cassandra.coordinator.dc` | `string` | development |  | The data center of the coordinating node for a query. | `"us-west-2"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cassandra.coordinator.id` | `string` | development |  | The ID of the coordinating node for a query. | `"be13faa2-8574-4d71-926d-27f16cf8a7af"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cassandra.page.size` | `int` | development |  | The fetch size used for paging, i.e. how many rows will be returned at once. | `5000` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cassandra.query.idempotent` | `boolean` | development |  | Whether or not the query is idempotent. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cassandra.speculative_execution.count` | `int` | development |  | The number of times a query was speculatively executed. Not set or `0` if the query was not executed speculatively. | `0`, `2` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `cassandra.consistency.level` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `all` | development |  | All |
| `any` | development |  | Any |
| `each_quorum` | development |  | Each Quorum |
| `local_one` | development |  | Local One |
| `local_quorum` | development |  | Local Quorum |
| `local_serial` | development |  | Local Serial |
| `one` | development |  | One |
| `quorum` | development |  | Quorum |
| `serial` | development |  | Serial |
| `three` | development |  | Three |
| `two` | development |  | Two |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
