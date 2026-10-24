# `jvm` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `jvm.buffer.pool.name` | `string` | development |  | Name of the buffer pool. | `"mapped"`, `"direct"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `jvm.gc.action` | `string` | stable |  | Name of the garbage collector action. | `"end of minor GC"`, `"end of major GC"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `jvm.gc.cause` | `string` | development |  | Name of the garbage collector cause. | `"System.gc()"`, `"Allocation Failure"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `jvm.gc.name` | `string` | stable |  | Name of the garbage collector. | `"G1 Young Generation"`, `"G1 Old Generation"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `jvm.memory.pool.name` | `string` | stable |  | Name of the memory pool. | `"G1 Old Gen"`, `"G1 Eden space"`, `"G1 Survivor Space"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `jvm.memory.type` | `enum` | stable |  | The type of memory. | `"heap"`, `"non_heap"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `jvm.thread.daemon` | `boolean` | stable |  | Whether the thread is daemon or not. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `jvm.thread.state` | `enum` | stable |  | State of the thread. | `"runnable"`, `"blocked"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `jvm.memory.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `heap` | stable |  | Heap memory. |
| `non_heap` | stable |  | Non-heap memory |

### `jvm.thread.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `blocked` | stable |  | A thread that is blocked waiting for a monitor lock is in this state. |
| `new` | stable |  | A thread that has not yet started is in this state. |
| `runnable` | stable |  | A thread executing in the Java virtual machine is in this state. |
| `terminated` | stable |  | A thread that has exited is in this state. |
| `timed_waiting` | stable |  | A thread that is waiting for another thread to perform an action for up to a specified waiting time is in this state. |
| `waiting` | stable |  | A thread that is waiting indefinitely for another thread to perform a particular action is in this state. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `jvm.buffer.count` | updowncounter | `{buffer}` | development |  | Number of buffers in the pool. |
| `jvm.buffer.memory.limit` | updowncounter | `By` | development |  | Measure of total memory capacity of buffers. |
| `jvm.buffer.memory.usage` | updowncounter | `By` | development | Replaced by `jvm.buffer.memory.used`. | Deprecated, use `jvm.buffer.memory.used` instead. |
| `jvm.buffer.memory.used` | updowncounter | `By` | development |  | Measure of memory used by buffers. |
| `jvm.class.count` | updowncounter | `{class}` | stable |  | Number of classes currently loaded. |
| `jvm.class.loaded` | counter | `{class}` | stable |  | Number of classes loaded since JVM start. |
| `jvm.class.unloaded` | counter | `{class}` | stable |  | Number of classes unloaded since JVM start. |
| `jvm.cpu.count` | updowncounter | `{cpu}` | stable |  | Number of processors available to the Java virtual machine. |
| `jvm.cpu.recent_utilization` | gauge | `1` | stable |  | Recent CPU utilization for the process as reported by the JVM. |
| `jvm.cpu.time` | counter | `s` | stable |  | CPU time used by the process as reported by the JVM. |
| `jvm.file_descriptor.count` | updowncounter | `{file_descriptor}` | development |  | Number of open file descriptors as reported by the JVM. |
| `jvm.file_descriptor.limit` | updowncounter | `{file_descriptor}` | development |  | Measure of max open file descriptors as reported by the JVM. |
| `jvm.gc.duration` | histogram | `s` | stable |  | Duration of JVM garbage collection actions. |
| `jvm.memory.committed` | updowncounter | `By` | stable |  | Measure of memory committed. |
| `jvm.memory.init` | updowncounter | `By` | development |  | Measure of initial memory requested. |
| `jvm.memory.limit` | updowncounter | `By` | stable |  | Measure of max obtainable memory. |
| `jvm.memory.used` | updowncounter | `By` | stable |  | Measure of memory used. |
| `jvm.memory.used_after_last_gc` | updowncounter | `By` | stable |  | Measure of memory used, as measured after the most recent garbage collection event on this pool. |
| `jvm.system.cpu.load_1m` | gauge | `{run_queue_item}` | development |  | Average CPU load of the whole system for the last minute as reported by the JVM. |
| `jvm.system.cpu.utilization` | gauge | `1` | development |  | Recent CPU utilization for the whole system as reported by the JVM. |
| `jvm.thread.count` | updowncounter | `{thread}` | stable |  | Number of executing platform threads. |
