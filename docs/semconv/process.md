# `process` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `process.args_count` | `int` | release_candidate |  | Length of the process.command_args array | `4` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.command` | `string` | release_candidate |  | The command used to launch the process (i.e. the command name). On Linux based systems, can be set to the zeroth string in `proc/[pid]/cmdline`. On Windows, can be set to the first parameter extracted from `GetCommandLineW`. | `"cmd/otelcol"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.command_args` | `string[]` | release_candidate |  | All the command arguments (including the command/executable itself) as received by the process. On Linux-based systems (and some other Unixoid systems supporting procfs), can be set according to the list of null-delimited strings extracted from `proc/[pid]/cmdline`. For libc-based executables, this would be the full argv vector passed to `main`. SHOULD NOT be collected by default unless there is sanitization that excludes sensitive data. | `["cmd/otecol","--config=config.yaml"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.command_line` | `string` | release_candidate |  | The full command used to launch the process as a single string representing the full command. On Windows, can be set to the result of `GetCommandLineW`. Do not set this if you have to assemble it just for monitoring; use `process.command_args` instead. SHOULD NOT be collected by default unless there is sanitization that excludes sensitive data. | `"C:\\cmd\\otecol --config=\"my directory\\config.yaml\""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.context_switch.type` | `enum` | release_candidate |  | Specifies whether the context switches for this data point were voluntary or involuntary. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.context_switch_type` | `enum` | development | Replaced by `process.context_switch.type`. | "Deprecated, use `process.context_switch.type` instead." |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.cpu.state` | `enum` | development | Replaced by `cpu.mode`. | Deprecated, use `cpu.mode` instead. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.creation.time` | `string` | release_candidate |  | The date and time the process was created, in ISO 8601 format. | `"2023-11-21T09:25:34.853Z"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.environment_variable` | `template[string]` | release_candidate |  | Process environment variables, `<key>` being the environment variable name, the value being the environment variable value. | `"ubuntu"`, `"/usr/local/bin:/usr/bin"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.executable.build_id.gnu` | `string` | release_candidate |  | The GNU build ID as found in the `.note.gnu.build-id` ELF section (hex string). | `"c89b11207f6479603b0d49bf291c092c2b719293"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.executable.build_id.go` | `string` | release_candidate |  | The Go build ID as retrieved by `go tool buildid <go executable>`. | `"foh3mEXu7BLZjsN9pOwG/kATcXlYVCDEFouRMQed_/WwRFB1hPo9LBkekthSPG/x8hMC8emW2cCjXD0_1aY"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.executable.build_id.htlhash` | `string` | release_candidate |  | Deterministic build ID for executables. | `"600DCAFE4A110000F2BF38C493F5FB92"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.executable.build_id.profiling` | `string` | development | Replaced by `process.executable.build_id.htlhash`. | "Deprecated, use `process.executable.build_id.htlhash` instead." | `"600DCAFE4A110000F2BF38C493F5FB92"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.executable.name` | `string` | release_candidate |  | The name of the process executable. On Linux based systems, this SHOULD be set to the base name of the target of `/proc/[pid]/exe`. On Windows, this SHOULD be set to the base name of `GetProcessImageFileNameW`. | `"otelcol"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.executable.path` | `string` | release_candidate |  | The full path to the process executable. On Linux based systems, can be set to the target of `proc/[pid]/exe`. On Windows, can be set to the result of `GetProcessImageFileNameW`. | `"/usr/bin/cmd/otelcol"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.exit.code` | `int` | release_candidate |  | The exit code of the process. | `127` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.exit.time` | `string` | release_candidate |  | The date and time the process exited, in ISO 8601 format. | `"2023-11-21T09:26:12.315Z"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.group_leader.pid` | `int` | release_candidate |  | The PID of the process's group leader. This is also the process group ID (PGID) of the process. | `23` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.interactive` | `boolean` | release_candidate |  | Whether the process is connected to an interactive shell. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.linux.cgroup` | `string` | release_candidate |  | The control group associated with the process. | `"1:name=systemd:/user.slice/user-1000.slice/session-3.scope"`, `"0::/user.slice/user-1000.slice/user@1000.service/tmux-spawn-0267755b-4639-4a27-90ed-f19f88e53748.scope"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.owner` | `string` | release_candidate |  | The username of the user that owns the process. | `"root"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.paging.fault_type` | `enum` | development | Replaced by `system.paging.fault.type`. | Deprecated, use `system.paging.fault.type` instead. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.parent_pid` | `int` | release_candidate |  | Parent Process identifier (PPID). | `111` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.pid` | `int` | release_candidate |  | Process identifier (PID). | `1234` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.real_user.id` | `int` | release_candidate |  | The real user ID (RUID) of the process. | `1000` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.real_user.name` | `string` | release_candidate |  | The username of the real user of the process. | `"operator"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.runtime.description` | `string` | release_candidate |  | An additional description about the runtime of the process, for example a specific vendor customization of the runtime environment. | `"Eclipse OpenJ9 Eclipse OpenJ9 VM openj9-0.21.0"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.runtime.name` | `string` | release_candidate |  | The name of the runtime of this process. | `"OpenJDK Runtime Environment"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.runtime.version` | `string` | release_candidate |  | The version of the runtime of this process, as returned by the runtime without modification. | `"14.0.2"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.saved_user.id` | `int` | release_candidate |  | The saved user ID (SUID) of the process. | `1002` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.saved_user.name` | `string` | release_candidate |  | The username of the saved user. | `"operator"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.session_leader.pid` | `int` | release_candidate |  | The PID of the process's session leader. This is also the session ID (SID) of the process. | `14` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.state` | `enum` | release_candidate |  | The process state, e.g., [Linux Process State Codes](https://man7.org/linux/man-pages/man1/ps.1.html#PROCESS_STATE_CODES) | `"running"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.title` | `string` | release_candidate |  | Process title (proctitle) | `"cat /etc/hostname"`, `"xfce4-session"`, `"bash"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.user.id` | `int` | release_candidate |  | The effective user ID (EUID) of the process. | `1001` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.user.name` | `string` | release_candidate |  | The username of the effective user of the process. | `"root"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.vpid` | `int` | release_candidate |  | Virtual process identifier. | `12` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `process.working_directory` | `string` | release_candidate |  | The working directory of the process. | `"/root"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `process.context_switch.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `involuntary` | release_candidate |  | `involuntary`. |
| `voluntary` | release_candidate |  | `voluntary`. |

### `process.context_switch_type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `involuntary` | development |  | `involuntary`. |
| `voluntary` | development |  | `voluntary`. |

### `process.cpu.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `system` | development |  | `system`. |
| `user` | development |  | `user`. |
| `wait` | development |  | `wait`. |

### `process.paging.fault_type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `major` | development |  | `major`. |
| `minor` | development |  | `minor`. |

### `process.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `defunct` | release_candidate |  | `defunct`. |
| `running` | release_candidate |  | `running`. |
| `sleeping` | release_candidate |  | `sleeping`. |
| `stopped` | release_candidate |  | `stopped`. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `process.context_switches` | counter | `{context_switch}` | release_candidate |  | Number of times the process has been context switched. |
| `process.cpu.time` | counter | `s` | release_candidate |  | Total CPU seconds broken down by different CPU modes. |
| `process.cpu.utilization` | gauge | `1` | release_candidate |  | Difference in process.cpu.time since the last measurement, divided by the elapsed time and number of CPUs available to the process. |
| `process.disk.io` | counter | `By` | release_candidate |  | Disk bytes transferred. |
| `process.disk.operations` | counter | `{operation}` | development |  | Number of disk operations performed by the process. |
| `process.memory.usage` | updowncounter | `By` | release_candidate |  | The amount of physical memory in use. |
| `process.memory.utilization` | gauge | `1` | development |  | Percentage of total physical memory that is used by the process. |
| `process.memory.virtual` | updowncounter | `By` | release_candidate |  | The amount of committed virtual memory. |
| `process.network.io` | counter | `By` | release_candidate |  | Network bytes transferred. |
| `process.open_file_descriptor.count` | updowncounter | `{file_descriptor}` | development | Replaced by `process.unix.file_descriptor.count`. | Deprecated, use `process.unix.file_descriptor.count` instead. |
| `process.paging.faults` | counter | `{fault}` | release_candidate |  | Number of page faults the process has made. |
| `process.signals_pending` | updowncounter | `{signal}` | development |  | Number of pending signals for the process. |
| `process.thread.count` | updowncounter | `{thread}` | release_candidate |  | Process threads count. |
| `process.unix.file_descriptor.count` | updowncounter | `{file_descriptor}` | release_candidate |  | Number of UNIX file descriptors in use by the process. |
| `process.uptime` | gauge | `s` | release_candidate |  | The time the process has been running. |
| `process.windows.handle.count` | updowncounter | `{handle}` | release_candidate |  | Number of handles held by the process. |
