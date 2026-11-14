# `system` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `system.cpu.logical_number` | `int` | development | Replaced by `cpu.logical_number`. | Deprecated, use `cpu.logical_number` instead. | `1` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `system.cpu.state` | `enum` | development | Replaced by `cpu.mode`. | Deprecated, use `cpu.mode` instead. | `"idle"`, `"interrupt"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `system.device` | `string` | development | Replaced by `system.device.name`. | Deprecated, use `system.device.name` instead. | `"(identifier)"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `system.device.name` | `string` | release_candidate |  | The OS level identifier for a device, such as disk, partition or network interface. | `"/dev/sda"`, `"eth0"`, `"\\network-drive"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `system.filesystem.mode` | `string` | release_candidate |  | The filesystem mode | `"rw, ro"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `system.filesystem.mountpoint` | `string` | release_candidate |  | The filesystem mount path | `"/mnt/data"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `system.filesystem.state` | `enum` | release_candidate |  | The filesystem state | `"used"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `system.filesystem.type` | `enum` | release_candidate |  | The filesystem type | `"ext4"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `system.memory.linux.hugepages.state` | `enum` | release_candidate |  | The Linux HugePages memory state | `"free"`, `"used"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `system.memory.linux.slab.state` | `enum` | release_candidate |  | The Linux Slab memory state | `"reclaimable"`, `"unreclaimable"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `system.memory.state` | `enum` | development |  | The memory state | `"used"`, `"available"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `system.network.state` | `enum` | development | Replaced by `network.connection.state`. | Deprecated, use `network.connection.state` instead. | `"close_wait"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `system.paging.direction` | `enum` | release_candidate |  | The paging access direction | `"in"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `system.paging.fault.type` | `enum` | release_candidate |  | The paging fault type | `"minor"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `system.paging.state` | `enum` | release_candidate |  | The memory paging state | `"free"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `system.paging.type` | `enum` | development | Replaced by `system.paging.fault.type`. | Deprecated, use `system.paging.fault.type` instead. | `"minor"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `system.process.status` | `enum` | development | Replaced by `process.state`. | Deprecated, use `process.state` instead. | `"running"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `system.processes.status` | `enum` | development | Replaced by `process.state`. | Deprecated, use `process.state` instead. | `"running"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |

### `system.cpu.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `idle` | development |  | `idle`. |
| `interrupt` | development |  | `interrupt`. |
| `iowait` | development |  | `iowait`. |
| `nice` | development |  | `nice`. |
| `steal` | development |  | `steal`. |
| `system` | development |  | `system`. |
| `user` | development |  | `user`. |

### `system.filesystem.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `free` | release_candidate |  | `free`. |
| `reserved` | release_candidate |  | `reserved`. |
| `used` | release_candidate |  | `used`. |

### `system.filesystem.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `exfat` | release_candidate |  | `exfat`. |
| `ext4` | release_candidate |  | `ext4`. |
| `fat32` | release_candidate |  | `fat32`. |
| `hfsplus` | release_candidate |  | `hfsplus`. |
| `ntfs` | release_candidate |  | `ntfs`. |
| `refs` | release_candidate |  | `refs`. |

### `system.memory.linux.hugepages.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `free` | release_candidate |  | `free`. |
| `used` | release_candidate |  | `used`. |

### `system.memory.linux.slab.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `reclaimable` | release_candidate |  | `reclaimable`. |
| `unreclaimable` | release_candidate |  | `unreclaimable`. |

### `system.memory.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `available` | development |  | An estimate of how much memory is available for starting new applications, without causing swapping. |
| `buffers` | development | Removed, report memory usage with the `used` and `available` states instead. | `buffers`. |
| `cached` | development | Removed, report memory usage with the `used` and `available` states instead. | `cached`. |
| `free` | development | Removed, report memory usage with the `used` and `available` states instead. | `free`. |
| `shared` | development | Removed, report shared memory usage with `metric.system.memory.linux.shared` metric | `shared`. |
| `used` | release_candidate |  | Actual used virtual memory in bytes. |

### `system.network.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `close` | development |  | `close`. |
| `close_wait` | development |  | `close_wait`. |
| `closing` | development |  | `closing`. |
| `delete` | development |  | `delete`. |
| `established` | development |  | `established`. |
| `fin_wait_1` | development |  | `fin_wait_1`. |
| `fin_wait_2` | development |  | `fin_wait_2`. |
| `last_ack` | development |  | `last_ack`. |
| `listen` | development |  | `listen`. |
| `syn_recv` | development |  | `syn_recv`. |
| `syn_sent` | development |  | `syn_sent`. |
| `time_wait` | development |  | `time_wait`. |

### `system.paging.direction` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `in` | release_candidate |  | `in`. |
| `out` | release_candidate |  | `out`. |

### `system.paging.fault.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `major` | release_candidate |  | `major`. |
| `minor` | release_candidate |  | `minor`. |

### `system.paging.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `free` | release_candidate |  | `free`. |
| `used` | release_candidate |  | `used`. |

### `system.paging.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `major` | development |  | `major`. |
| `minor` | development |  | `minor`. |

### `system.process.status` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `defunct` | development |  | `defunct`. |
| `running` | development |  | `running`. |
| `sleeping` | development |  | `sleeping`. |
| `stopped` | development |  | `stopped`. |

### `system.processes.status` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `defunct` | development |  | `defunct`. |
| `running` | development |  | `running`. |
| `sleeping` | development |  | `sleeping`. |
| `stopped` | development |  | `stopped`. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `system.cpu.frequency` | gauge | `Hz` | development |  | Operating frequency of the logical CPU in Hertz. |
| `system.cpu.logical.count` | updowncounter | `{cpu}` | development |  | Reports the number of logical (virtual) processor cores created by the operating system to manage multitasking. |
| `system.cpu.physical.count` | updowncounter | `{cpu}` | development |  | Reports the number of actual physical processor cores on the hardware. |
| `system.cpu.time` | counter | `s` | development |  | Seconds each logical CPU spent on each mode. |
| `system.cpu.utilization` | gauge | `1` | development |  | For each logical CPU, the utilization is calculated as the change in cumulative CPU time (cpu.time) over a measurement interval, divided by the elapsed time. |
| `system.disk.io` | counter | `By` | development |  | Disk bytes transferred. |
| `system.disk.io_time` | counter | `s` | development |  | Time disk spent activated. |
| `system.disk.limit` | updowncounter | `By` | development |  | The total storage capacity of the disk. |
| `system.disk.merged` | counter | `{operation}` | development |  | The number of disk reads/writes merged into single physical disk access operations. |
| `system.disk.operation_time` | counter | `s` | development |  | Sum of the time each operation took to complete. |
| `system.disk.operations` | counter | `{operation}` | development |  | Disk operations count. |
| `system.filesystem.limit` | updowncounter | `By` | development |  | The total storage capacity of the filesystem. |
| `system.filesystem.lock.count` | updowncounter | `{lock}` | development |  | Filesystem lock counts. |
| `system.filesystem.usage` | updowncounter | `By` | development |  | Reports a filesystem's space usage across different states. |
| `system.filesystem.utilization` | gauge | `1` | development |  | Fraction of filesystem bytes used. |
| `system.linux.memory.available` | counter | `{packet}` | development | Replaced by `system.memory.linux.available`. | The number of packets transferred. |
| `system.linux.memory.slab.usage` | counter | `{packet}` | development | Replaced by `system.memory.linux.slab.usage`. | The number of packets transferred. |
| `system.memory.limit` | updowncounter | `By` | development |  | Total virtual memory available in the system. |
| `system.memory.linux.available` | updowncounter | `By` | development |  | An estimate of how much memory is available for starting new applications, without causing swapping. |
| `system.memory.linux.hugepages.limit` | updowncounter | `{page}` | development |  | Total number of hugepages available. |
| `system.memory.linux.hugepages.page_size` | updowncounter | `By` | development |  | System hugepage size in bytes. |
| `system.memory.linux.hugepages.reserved` | updowncounter | `{page}` | development |  | Number of reserved hugepages. |
| `system.memory.linux.hugepages.surplus` | updowncounter | `{page}` | development |  | Number of surplus hugepages. |
| `system.memory.linux.hugepages.usage` | updowncounter | `{page}` | development |  | Number of hugepages in use by state. |
| `system.memory.linux.hugepages.utilization` | gauge | `1` | development |  | Percentage of hugepages in use by state. |
| `system.memory.linux.shared` | updowncounter | `By` | development |  | Shared memory used (mostly by tmpfs). |
| `system.memory.linux.slab.usage` | updowncounter | `By` | development |  | Reports the memory used by the Linux kernel for managing caches of frequently used objects. |
| `system.memory.shared` | updowncounter | `By` | development | Replaced by `system.memory.linux.shared`. | Deprecated, use `system.memory.linux.shared` instead. |
| `system.memory.usage` | updowncounter | `By` | development |  | Reports memory by state. |
| `system.memory.utilization` | gauge | `1` | development |  | Fraction of memory bytes by state. |
| `system.network.connection.count` | updowncounter | `{connection}` | development |  | The number of connections. |
| `system.network.connections` | updowncounter | `{connection}` | development | Replaced by `system.network.connection.count`. | Deprecated, use `system.network.connection.count` instead. |
| `system.network.dropped` | counter | `{packet}` | development | Replaced by `system.network.packet.dropped`. | Count of packets that are dropped or discarded even though there was no error. |
| `system.network.errors` | counter | `{error}` | development |  | Count of network errors detected. |
| `system.network.io` | counter | `By` | development |  | The number of bytes transmitted and received. |
| `system.network.packet.count` | counter | `{packet}` | development |  | The number of packets transferred. |
| `system.network.packet.dropped` | counter | `{packet}` | development |  | Count of packets that are dropped or discarded even though there was no error. |
| `system.network.packets` | counter | `{packet}` | development | Replaced by `system.network.packet.count`. | The number of packets transferred. |
| `system.paging.faults` | counter | `{fault}` | development |  | The number of page faults. |
| `system.paging.operations` | counter | `{operation}` | development |  | The number of paging operations. |
| `system.paging.usage` | updowncounter | `By` | development |  | UNIX swap or windows pagefile usage. |
| `system.paging.utilization` | gauge | `1` | development |  | Swap (UNIX) or pagefile (windows) utilization. |
| `system.process.count` | updowncounter | `{process}` | development |  | Total number of processes in each state. |
| `system.process.created` | counter | `{process}` | development |  | Total number of processes created over uptime of the host. |
| `system.process.limit` | updowncounter | `{thread}` | development |  | The maximum number of concurrent processes/tasks allowed by the operating system. |
| `system.uptime` | gauge | `s` | development |  | The time the system has been running. |
