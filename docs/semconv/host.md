# `host` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `host.arch` | `enum` | development |  | The CPU architecture the host system is running on. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `host.cpu.cache.l2.size` | `int` | development |  | The amount of level 2 memory cache available to the processor (in Bytes). | `12288000` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `host.cpu.family` | `string` | development |  | Family or generation of the CPU. | `"6"`, `"PA-RISC 1.1e"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `host.cpu.model.id` | `string` | development |  | Model identifier. It provides more granular information about the CPU, distinguishing it from other CPUs within the same family. | `"6"`, `"9000/778/B180L"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `host.cpu.model.name` | `string` | development |  | Model designation of the processor. | `"11th Gen Intel(R) Core(TM) i7-1185G7 @ 3.00GHz"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `host.cpu.stepping` | `string` | development |  | Stepping or core revisions. | `"1"`, `"r1p1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `host.cpu.vendor.id` | `string` | development |  | Processor manufacturer identifier. A maximum 12-character string. | `"GenuineIntel"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `host.id` | `string` | development |  | Unique host ID. For Cloud, this must be the instance_id assigned by the cloud provider. For non-containerized systems, this should be the `machine-id`. See the table below for the sources to use to determine the `machine-id` based on operating system. | `"fdbf79e8af94cb7f9e8df36789187052"` | `nami.persisted`: `false`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `false` |
| `host.image.id` | `string` | development |  | VM image ID or host OS image ID. For Cloud, this value is from the provider. | `"ami-07b06b442921831e5"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `host.image.name` | `string` | development |  | Name of the VM image or OS install the host was instantiated from. | `"infra-ami-eks-worker-node-7d4ec78312"`, `"CentOS-8-x86_64-1905"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `host.image.version` | `string` | development |  | The version string of the VM image or host OS as defined in [Version Attributes](/docs/resource/README.md#version-attributes). | `"0.1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `host.ip` | `string[]` | development |  | Available IP addresses of the host, excluding loopback interfaces. | `["192.168.1.140","fe80::abc2:4a28:737a:609e"]` | `nami.persisted`: `false`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `false` |
| `host.mac` | `string[]` | development |  | Available MAC addresses of the host, excluding loopback interfaces. | `["AC-DE-48-23-45-67","AC-DE-48-23-45-67-01-9F"]` | `nami.persisted`: `false`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `false` |
| `host.name` | `string` | development |  | Name of the host. On UNIX systems, it may contain what the hostname command returns, or the fully qualified hostname, or another name specified by the user. | `"opentelemetry-test"` | `nami.persisted`: `false`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `false` |
| `host.type` | `string` | development |  | Type of host. For Cloud, this must be the machine type. | `"n1-standard-1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `host.arch` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `amd64` | development |  | AMD64 |
| `arm32` | development |  | ARM32 |
| `arm64` | development |  | ARM64 |
| `ia64` | development |  | Itanium |
| `ppc32` | development |  | 32-bit PowerPC |
| `ppc64` | development |  | 64-bit PowerPC |
| `s390x` | development |  | IBM z/Architecture |
| `x86` | development |  | 32-bit x86 |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
