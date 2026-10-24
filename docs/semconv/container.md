# `container` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `container.command` | `string` | development |  | The command used to run the container (i.e. the command name). | `"otelcontribcol"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `container.command_args` | `string[]` | development |  | All the command arguments (including the command/executable itself) run by the container. | `["otelcontribcol","--config","config.yaml"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `container.command_line` | `string` | development |  | The full command run by the container as a single string representing the full command. | `"otelcontribcol --config config.yaml"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `container.cpu.state` | `enum` | development | Replaced by `cpu.mode`. | Deprecated, use `cpu.mode` instead. | `"user"`, `"kernel"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `container.csi.plugin.name` | `string` | development |  | The name of the CSI ([Container Storage Interface](https://github.com/container-storage-interface/spec)) plugin used by the volume. | `"pd.csi.storage.gke.io"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `container.csi.volume.id` | `string` | development |  | The unique volume ID returned by the CSI ([Container Storage Interface](https://github.com/container-storage-interface/spec)) plugin. | `"projects/my-gcp-project/zones/my-gcp-zone/disks/my-gcp-disk"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `container.id` | `string` | stable |  | Container ID. Usually a UUID, as for example used to [identify Docker containers](https://docs.docker.com/engine/containers/run/#container-identification). The UUID might be abbreviated. | `"a3bf90e006b2"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `container.image.id` | `string` | development |  | Runtime specific image identifier. Usually a hash algorithm followed by a UUID. | `"sha256:19c92d0a00d1b66d897bceaa7319bee0dd38a10a851c60bcec9474aa3f01e50f"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `container.image.name` | `string` | stable |  | Name of the image the container was built on. | `"gcr.io/opentelemetry/operator"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `container.image.repo_digests` | `string[]` | stable |  | Repository digests of the container image as provided by the container runtime. | `["example@sha256:afcc7f1ac1b49db317a7196c902e61c6c3c4607d63599ee1a82d702d249a0ccb","internal.registry.example.com:5000/example@sha256:b69959407d21e8a062e0416bf13405bb2b71ed7a84dde4158ebafacfa06f5578"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `container.image.tags` | `string[]` | stable |  | Container image tags. An example can be found in [Docker Image Inspect](https://docs.docker.com/reference/api/engine/version/v1.52/#tag/Image/operation/ImageInspect). Should be only the `<tag>` section of the full name for example from `registry.example.com/my-org/my-image:<tag>`. | `["v1.27.1","3.5.7-0"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `container.label` | `template[string]` | development |  | Container labels, `<key>` being the label name, the value being the label value. | `"nginx"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `container.labels` | `template[string]` | development | Replaced by `container.label`. | Deprecated, use `container.label` instead. | `"nginx"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `container.name` | `string` | development |  | Container name used by container runtime. | `"opentelemetry-autoconf"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `container.runtime` | `string` | development | Replaced by `container.runtime.name`. | The container runtime managing this container. | `"docker"`, `"containerd"`, `"rkt"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `container.runtime.description` | `string` | development |  | A description about the runtime which could include, for example details about the CRI/API version being used or other customizations. | `"docker://19.3.1 - CRI: 1.22.0"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `container.runtime.name` | `string` | development |  | The container runtime managing this container. | `"docker"`, `"containerd"`, `"rkt"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `container.runtime.version` | `string` | development |  | The version of the runtime of this process, as returned by the runtime without modification. | `"1.0.0"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `container.cpu.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `kernel` | development |  | When tasks of the cgroup are in kernel mode (Linux). When all container processes are in kernel mode (Windows). |
| `system` | development |  | When CPU is used by the system (host OS) |
| `user` | development |  | When tasks of the cgroup are in user mode (Linux). When all container processes are in user mode (Windows). |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `container.cpu.time` | counter | `s` | release_candidate |  | Total CPU time consumed. |
| `container.cpu.usage` | gauge | `{cpu}` | development |  | Container's CPU usage, measured in CPUs. Range from 0 to the number of allocatable CPUs. |
| `container.disk.io` | counter | `By` | development |  | Disk bytes for the container. |
| `container.filesystem.available` | updowncounter | `By` | development |  | Container filesystem available bytes. |
| `container.filesystem.capacity` | updowncounter | `By` | development |  | Container filesystem capacity. |
| `container.filesystem.usage` | updowncounter | `By` | development |  | Container filesystem usage. |
| `container.memory.available` | updowncounter | `By` | release_candidate |  | Container memory available. |
| `container.memory.paging.faults` | counter | `{fault}` | development | Replaced by `container.paging.faults`. | Deprecated, use `container.paging.faults` instead. |
| `container.memory.rss` | updowncounter | `By` | release_candidate |  | Container memory RSS. |
| `container.memory.usage` | updowncounter | `By` | release_candidate |  | Memory usage of the container. |
| `container.memory.working_set` | updowncounter | `By` | release_candidate |  | Container memory working set. |
| `container.network.io` | counter | `By` | development |  | Network bytes for the container. |
| `container.paging.faults` | counter | `{fault}` | development |  | Container memory paging faults. |
| `container.uptime` | gauge | `s` | development |  | The time the container has been running. |
