# `v8js` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `v8js.gc.type` | `enum` | development |  | The type of garbage collection. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `v8js.heap.space.name` | `enum` | development |  | The name of the space type of heap memory. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `v8js.resource.type` | `enum` | development |  | The type of resource keeping the event loop active. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `v8js.gc.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `incremental` | development |  | Incremental (Incremental Marking). |
| `major` | development |  | Major (Mark Sweep Compact). |
| `minor` | development |  | Minor (Scavenge). |
| `weakcb` | development |  | Weak Callbacks (Process Weak Callbacks). |

### `v8js.heap.space.name` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `code_space` | development |  | Code memory space. |
| `large_object_space` | development |  | Large object memory space. |
| `map_space` | development |  | Map memory space. |
| `new_space` | development |  | New memory space. |
| `old_space` | development |  | Old memory space. |

### `v8js.resource.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `Immediate` | development |  | Active `setImmediate` callbacks. |
| `TCPServerWrap` | development |  | Active TCP Servers. |
| `TCPWrap` | development |  | Active TCP connections. |
| `Timeout` | development |  | Active `setTimeout` or `setInterval` timers. |
| `TTYWrap` | development |  | Active Terminal I/O (stdin/stdout). |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `v8js.gc.duration` | histogram | `s` | development |  | Garbage collection duration. |
| `v8js.heap.space.available_size` | updowncounter | `By` | development | Replaced by `v8js.memory.heap.space.available_size`. | Deprecated, use `v8js.memory.heap.space.available_size` instead. |
| `v8js.heap.space.physical_size` | updowncounter | `By` | development | Replaced by `v8js.memory.heap.space.physical_size`. | Deprecated, use `v8js.memory.heap.space.physical_size` instead. |
| `v8js.memory.heap.limit` | updowncounter | `By` | development | Replaced by `v8js.memory.heap.space.size`. | Deprecated, use `v8js.memory.heap.space.size` instead. |
| `v8js.memory.heap.space.available_size` | updowncounter | `By` | development |  | Heap space available size. |
| `v8js.memory.heap.space.physical_size` | updowncounter | `By` | development |  | Committed size of a heap space. |
| `v8js.memory.heap.space.size` | updowncounter | `By` | development |  | Total heap memory size pre-allocated for a heap space. |
| `v8js.memory.heap.used` | updowncounter | `By` | development |  | Heap Memory size allocated. |
| `v8js.resource.active` | gauge | `{resource}` | development |  | Gauge of the active resources that are currently keeping the event loop alive. |
