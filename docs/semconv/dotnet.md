# `dotnet` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `dotnet.gc.heap.generation` | `enum` | stable |  | Name of the garbage collector managed heap generation. | `"gen0"`, `"gen1"`, `"gen2"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `dotnet.gc.heap.generation` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `gen0` | stable |  | Generation 0 |
| `gen1` | stable |  | Generation 1 |
| `gen2` | stable |  | Generation 2 |
| `loh` | stable |  | Large Object Heap |
| `poh` | stable |  | Pinned Object Heap |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `dotnet.assembly.count` | updowncounter | `{assembly}` | stable |  | The number of .NET assemblies that are currently loaded. |
| `dotnet.exceptions` | counter | `{exception}` | stable |  | The number of exceptions that have been thrown in managed code. |
| `dotnet.gc.collections` | counter | `{collection}` | stable |  | The number of garbage collections that have occurred since the process has started. |
| `dotnet.gc.heap.total_allocated` | counter | `By` | stable |  | The _approximate_ number of bytes allocated on the managed GC heap since the process has started. The returned value does not include any native allocations. |
| `dotnet.gc.last_collection.heap.fragmentation.size` | updowncounter | `By` | stable |  | The heap fragmentation, as observed during the latest garbage collection. |
| `dotnet.gc.last_collection.heap.size` | updowncounter | `By` | stable |  | The managed GC heap size (including fragmentation), as observed during the latest garbage collection. |
| `dotnet.gc.last_collection.memory.committed_size` | updowncounter | `By` | stable |  | The amount of committed virtual memory in use by the .NET GC, as observed during the latest garbage collection. |
| `dotnet.gc.pause.time` | counter | `s` | stable |  | The total amount of time paused in GC since the process has started. |
| `dotnet.jit.compilation.time` | counter | `s` | stable |  | The amount of time the JIT compiler has spent compiling methods since the process has started. |
| `dotnet.jit.compiled_il.size` | counter | `By` | stable |  | Count of bytes of intermediate language that have been compiled since the process has started. |
| `dotnet.jit.compiled_methods` | counter | `{method}` | stable |  | The number of times the JIT compiler (re)compiled methods since the process has started. |
| `dotnet.monitor.lock_contentions` | counter | `{contention}` | stable |  | The number of times there was contention when trying to acquire a monitor lock since the process has started. |
| `dotnet.process.cpu.count` | updowncounter | `{cpu}` | stable |  | The number of processors available to the process. |
| `dotnet.process.cpu.time` | counter | `s` | stable |  | CPU time used by the process. |
| `dotnet.process.memory.working_set` | updowncounter | `By` | stable |  | The number of bytes of physical memory mapped to the process context. |
| `dotnet.thread_pool.queue.length` | updowncounter | `{work_item}` | stable |  | The number of work items that are currently queued to be processed by the thread pool. |
| `dotnet.thread_pool.thread.count` | updowncounter | `{thread}` | stable |  | The number of thread pool threads that currently exist. |
| `dotnet.thread_pool.work_item.count` | counter | `{work_item}` | stable |  | The number of work items that the thread pool has completed since the process has started. |
| `dotnet.timer.count` | updowncounter | `{timer}` | stable |  | The number of timer instances that are currently active. |
