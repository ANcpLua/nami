# `openshift` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `openshift.clusterquota.name` | `string` | development |  | The name of the cluster quota. | `"opentelemetry"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `openshift.clusterquota.uid` | `string` | development |  | The UID of the cluster quota. | `"275ecb36-5aa8-4c2a-9c47-d8bb681b9aff"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `openshift.clusterquota.cpu.limit.hard` | updowncounter | `{cpu}` | development |  | The enforced hard limit of the resource across all projects. |
| `openshift.clusterquota.cpu.limit.used` | updowncounter | `{cpu}` | development |  | The current observed total usage of the resource across all projects. |
| `openshift.clusterquota.cpu.request.hard` | updowncounter | `{cpu}` | development |  | The enforced hard limit of the resource across all projects. |
| `openshift.clusterquota.cpu.request.used` | updowncounter | `{cpu}` | development |  | The current observed total usage of the resource across all projects. |
| `openshift.clusterquota.ephemeral_storage.limit.hard` | updowncounter | `By` | development |  | The enforced hard limit of the resource across all projects. |
| `openshift.clusterquota.ephemeral_storage.limit.used` | updowncounter | `By` | development |  | The current observed total usage of the resource across all projects. |
| `openshift.clusterquota.ephemeral_storage.request.hard` | updowncounter | `By` | development |  | The enforced hard limit of the resource across all projects. |
| `openshift.clusterquota.ephemeral_storage.request.used` | updowncounter | `By` | development |  | The current observed total usage of the resource across all projects. |
| `openshift.clusterquota.hugepage_count.request.hard` | updowncounter | `{hugepage}` | development |  | The enforced hard limit of the resource across all projects. |
| `openshift.clusterquota.hugepage_count.request.used` | updowncounter | `{hugepage}` | development |  | The current observed total usage of the resource across all projects. |
| `openshift.clusterquota.memory.limit.hard` | updowncounter | `By` | development |  | The enforced hard limit of the resource across all projects. |
| `openshift.clusterquota.memory.limit.used` | updowncounter | `By` | development |  | The current observed total usage of the resource across all projects. |
| `openshift.clusterquota.memory.request.hard` | updowncounter | `By` | development |  | The enforced hard limit of the resource across all projects. |
| `openshift.clusterquota.memory.request.used` | updowncounter | `By` | development |  | The current observed total usage of the resource across all projects. |
| `openshift.clusterquota.object_count.hard` | updowncounter | `{object}` | development |  | The enforced hard limit of the resource across all projects. |
| `openshift.clusterquota.object_count.used` | updowncounter | `{object}` | development |  | The current observed total usage of the resource across all projects. |
| `openshift.clusterquota.persistentvolumeclaim_count.hard` | updowncounter | `{persistentvolumeclaim}` | development |  | The enforced hard limit of the resource across all projects. |
| `openshift.clusterquota.persistentvolumeclaim_count.used` | updowncounter | `{persistentvolumeclaim}` | development |  | The current observed total usage of the resource across all projects. |
| `openshift.clusterquota.storage.request.hard` | updowncounter | `By` | development |  | The enforced hard limit of the resource across all projects. |
| `openshift.clusterquota.storage.request.used` | updowncounter | `By` | development |  | The current observed total usage of the resource across all projects. |
