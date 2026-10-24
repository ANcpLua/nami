# `go` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `go.cpu.detailed_state` | `string` | development |  | The detailed state of the CPU. | `"gc/pause"`, `"gc/mark/assist"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `go.cpu.state` | `enum` | development |  | The state of the CPU. | `"user"`, `"gc"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `go.memory.detailed_type` | `string` | development |  | The detailed type of memory. | `"heap/objects"`, `"heap/free"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `go.memory.type` | `enum` | development |  | The type of memory. | `"other"`, `"stack"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `go.cpu.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `gc` | development |  | CPU time spent performing garbage collection tasks. |
| `idle` | development |  | Available CPU time not spent executing any Go or Go runtime code. |
| `scavenge` | development |  | CPU time spent returning unused memory to the underlying platform. |
| `user` | development |  | CPU time spent running user Go code. |

### `go.memory.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `other` | development |  | Memory used by the Go runtime, excluding other categories of memory usage described in this enumeration. |
| `stack` | development |  | Memory allocated from the heap that is reserved for stack space, whether or not it is currently in-use. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `go.config.gogc` | updowncounter | `%` | development |  | Heap size target percentage configured by the user, otherwise 100. |
| `go.cpu.time` | counter | `s` | development |  | Estimated CPU time spent by the Go runtime. |
| `go.goroutine.count` | updowncounter | `{goroutine}` | development |  | Count of live goroutines. |
| `go.memory.allocated` | counter | `By` | development |  | Memory allocated to the heap by the application. |
| `go.memory.allocations` | counter | `{allocation}` | development |  | Count of allocations to the heap by the application. |
| `go.memory.gc.cycles` | counter | `{gc_cycle}` | development |  | Number of completed GC cycles. |
| `go.memory.gc.goal` | updowncounter | `By` | development |  | Heap size target for the end of the GC cycle. |
| `go.memory.gc.pause.duration` | histogram | `s` | development |  | Distribution of individual GC-related stop-the-world pause latencies. This is the time from deciding to stop the world until the world is started again. |
| `go.memory.limit` | updowncounter | `By` | development |  | Go runtime memory limit configured by the user, if a limit exists. |
| `go.memory.used` | updowncounter | `By` | development |  | Memory used by the Go runtime. |
| `go.processor.limit` | updowncounter | `{thread}` | development |  | The number of OS threads that can execute user-level Go code simultaneously. |
| `go.schedule.duration` | histogram | `s` | development |  | The time goroutines have spent in the scheduler in a runnable state before actually running. |
