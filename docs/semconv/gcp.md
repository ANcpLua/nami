# `gcp` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `gcp.apphub.application.container` | `string` | development |  | The container within GCP where the AppHub application is defined. | `"projects/my-container-project"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.apphub.application.id` | `string` | development |  | The name of the application as configured in AppHub. | `"my-application"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.apphub.application.location` | `string` | development |  | The GCP zone or region where the application is defined. | `"us-central1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.apphub.service.criticality_type` | `enum` | development |  | Criticality of a service indicates its importance to the business. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.apphub.service.environment_type` | `enum` | development |  | Environment of a service is the stage of a software lifecycle. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.apphub.service.id` | `string` | development |  | The name of the service as configured in AppHub. | `"my-service"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.apphub.workload.criticality_type` | `enum` | development |  | Criticality of a workload indicates its importance to the business. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.apphub.workload.environment_type` | `enum` | development |  | Environment of a workload is the stage of a software lifecycle. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.apphub.workload.id` | `string` | development |  | The name of the workload as configured in AppHub. | `"my-workload"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.apphub_destination.application.container` | `string` | development |  | The container within GCP where the AppHub destination application is defined. | `"projects/my-container-project"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.apphub_destination.application.id` | `string` | development |  | The name of the destination application as configured in AppHub. | `"my-application"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.apphub_destination.application.location` | `string` | development |  | The GCP zone or region where the destination application is defined. | `"us-central1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.apphub_destination.service.criticality_type` | `enum` | development |  | Criticality of a destination workload indicates its importance to the business as specified in [AppHub type enum](https://cloud.google.com/app-hub/docs/reference/rest/v1/Attributes#type) |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.apphub_destination.service.environment_type` | `enum` | development |  | Software lifecycle stage of a destination service as defined [AppHub environment type](https://cloud.google.com/app-hub/docs/reference/rest/v1/Attributes#type_1) |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.apphub_destination.service.id` | `string` | development |  | The name of the destination service as configured in AppHub. | `"my-service"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.apphub_destination.workload.criticality_type` | `enum` | development |  | Criticality of a destination workload indicates its importance to the business as specified in [AppHub type enum](https://cloud.google.com/app-hub/docs/reference/rest/v1/Attributes#type) |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.apphub_destination.workload.environment_type` | `enum` | development |  | Environment of a destination workload is the stage of a software lifecycle as provided in the [AppHub environment type](https://cloud.google.com/app-hub/docs/reference/rest/v1/Attributes#type_1) |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.apphub_destination.workload.id` | `string` | development |  | The name of the destination workload as configured in AppHub. | `"my-workload"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.client.service` | `string` | development |  | Identifies the Google Cloud service for which the official client library is intended. | `"appengine"`, `"run"`, `"firestore"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.cloud_run.job.execution` | `string` | development |  | The name of the Cloud Run [execution](https://cloud.google.com/run/docs/managing/job-executions) being run for the Job, as set by the [`CLOUD_RUN_EXECUTION`](https://cloud.google.com/run/docs/container-contract#jobs-env-vars) environment variable. | `"job-name-xxxx"`, `"sample-job-mdw84"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.cloud_run.job.task_index` | `int` | development |  | The index for a task within an execution as provided by the [`CLOUD_RUN_TASK_INDEX`](https://cloud.google.com/run/docs/container-contract#jobs-env-vars) environment variable. | `0`, `1` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.gce.instance.hostname` | `string` | development |  | The hostname of a GCE instance. This is the full value of the default or [custom hostname](https://cloud.google.com/compute/docs/instances/custom-hostname-vm). | `"my-host1234.example.com"`, `"sample-vm.us-west1-b.c.my-project.internal"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.gce.instance.labels` | `template[string]` | development |  | GCE instance labels, `<key>` being the label name and the value being the label value. | `"observability"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.gce.instance.name` | `string` | development |  | The instance name of a GCE instance. This is the value provided by `host.name`, the visible name of the instance in the Cloud Console UI, and the prefix for the default hostname of the instance as defined by the [default internal DNS name](https://cloud.google.com/compute/docs/internal-dns#instance-fully-qualified-domain-names). | `"instance-1"`, `"my-vm-name"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.gce.instance_group_manager.name` | `string` | development |  | The name of the Instance Group Manager (IGM) that manages this VM, if any. | `"web-igm"`, `"my-managed-group"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.gce.instance_group_manager.region` | `string` | development |  | The region of a **regional** Instance Group Manager (e.g., `us-central1`). Set this **only** when the IGM is regional. | `"us-central1"`, `"europe-west1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `gcp.gce.instance_group_manager.zone` | `string` | development |  | The zone of a **zonal** Instance Group Manager (e.g., `us-central1-a`). Set this **only** when the IGM is zonal. | `"us-central1-a"`, `"europe-west1-b"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `gcp.apphub.service.criticality_type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `HIGH` | development |  | High impact. |
| `LOW` | development |  | Low impact. |
| `MEDIUM` | development |  | Medium impact. |
| `MISSION_CRITICAL` | development |  | Mission critical service. |

### `gcp.apphub.service.environment_type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `DEVELOPMENT` | development |  | Development environment. |
| `PRODUCTION` | development |  | Production environment. |
| `STAGING` | development |  | Staging environment. |
| `TEST` | development |  | Test environment. |

### `gcp.apphub.workload.criticality_type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `HIGH` | development |  | High impact. |
| `LOW` | development |  | Low impact. |
| `MEDIUM` | development |  | Medium impact. |
| `MISSION_CRITICAL` | development |  | Mission critical service. |

### `gcp.apphub.workload.environment_type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `DEVELOPMENT` | development |  | Development environment. |
| `PRODUCTION` | development |  | Production environment. |
| `STAGING` | development |  | Staging environment. |
| `TEST` | development |  | Test environment. |

### `gcp.apphub_destination.service.criticality_type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `HIGH` | development |  | High impact. |
| `LOW` | development |  | Low impact. |
| `MEDIUM` | development |  | Medium impact. |
| `MISSION_CRITICAL` | development |  | Mission critical service. |

### `gcp.apphub_destination.service.environment_type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `DEVELOPMENT` | development |  | Development environment. |
| `PRODUCTION` | development |  | Production environment. |
| `STAGING` | development |  | Staging environment. |
| `TEST` | development |  | Test environment. |

### `gcp.apphub_destination.workload.criticality_type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `HIGH` | development |  | High impact. |
| `LOW` | development |  | Low impact. |
| `MEDIUM` | development |  | Medium impact. |
| `MISSION_CRITICAL` | development |  | Mission critical service. |

### `gcp.apphub_destination.workload.environment_type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `DEVELOPMENT` | development |  | Development environment. |
| `PRODUCTION` | development |  | Production environment. |
| `STAGING` | development |  | Staging environment. |
| `TEST` | development |  | Test environment. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
