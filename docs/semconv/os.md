# `os` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `os.build_id` | `string` | development |  | Unique identifier for a particular build or compilation of the operating system. | `"TQ3C.230805.001.B2"`, `"20E247"`, `"22621"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `os.description` | `string` | development |  | Human readable (not intended to be parsed) OS version information, like e.g. reported by `ver` or `lsb_release -a` commands. | `"Microsoft Windows [Version 10.0.18363.778]"`, `"Ubuntu 18.04.1 LTS"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `os.name` | `string` | development |  | Human readable operating system name. | `"iOS"`, `"Android"`, `"Ubuntu"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `os.type` | `enum` | development |  | The operating system type. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `os.version` | `string` | development |  | The version string of the operating system as defined in [Version Attributes](/docs/resource/README.md#version-attributes). | `"14.2.1"`, `"18.04.1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `os.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `aix` | development |  | AIX (Advanced Interactive eXecutive) |
| `darwin` | development |  | Apple Darwin |
| `dragonflybsd` | development |  | DragonFly BSD |
| `freebsd` | development |  | FreeBSD |
| `hpux` | development |  | HP-UX (Hewlett Packard UNIX) |
| `linux` | development |  | Linux |
| `netbsd` | development |  | NetBSD |
| `openbsd` | development |  | OpenBSD |
| `solaris` | development |  | SunOS, Oracle Solaris |
| `windows` | development |  | Microsoft Windows |
| `z_os` | development | Replaced by `zos`. | Deprecated. Use `zos` instead. |
| `zos` | development |  | IBM z/OS |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
