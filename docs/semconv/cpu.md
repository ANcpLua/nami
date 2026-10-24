# `cpu` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `cpu.logical_number` | `int` | development |  | The logical CPU number [0..n-1] | `1` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cpu.mode` | `enum` | release_candidate |  | The mode of the CPU | `"user"`, `"system"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `cpu.mode` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `idle` | release_candidate |  | Idle |
| `interrupt` | release_candidate |  | Interrupt |
| `iowait` | release_candidate |  | IO Wait |
| `kernel` | release_candidate | Replaced by `system`. | Deprecated. Use `system` instead. |
| `nice` | release_candidate |  | Nice |
| `steal` | release_candidate |  | Steal |
| `system` | release_candidate |  | System |
| `user` | release_candidate |  | User |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `cpu.frequency` | gauge | `{Hz}` | development | Replaced by `system.cpu.frequency`. | Deprecated. Use `system.cpu.frequency` instead. |
| `cpu.time` | counter | `s` | development | Replaced by `system.cpu.time`. | Deprecated. Use `system.cpu.time` instead. |
| `cpu.utilization` | gauge | `1` | development | Replaced by `system.cpu.utilization`. | Deprecated. Use `system.cpu.utilization` instead. |
