# `k8s` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `k8s.cluster.name` | `string` | stable |  | The name of the cluster. | `"opentelemetry-cluster"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.cluster.uid` | `string` | stable |  | A pseudo-ID for the cluster, set to the UID of the `kube-system` namespace. | `"218fc5a9-a5f1-4b54-aa05-46717d0ab26d"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.container.ephemeral_storage.fs_type` | `enum` | development |  | The type of file system component for ephemeral storage. | `"rootfs"`, `"logs"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.container.name` | `string` | stable |  | The name of the Container from Pod specification, must be unique within a Pod. Container runtime usually uses different globally unique name (`container.name`). | `"redis"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.container.restart_count` | `int` | stable |  | Number of times the container was restarted. This attribute can be used to identify a particular container (running or stopped) within a container spec. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.container.status.last_terminated_reason` | `string` | development |  | Last terminated reason of the Container. | `"Evicted"`, `"Error"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.container.status.reason` | `enum` | development |  | The reason for the container state. Corresponds to the `reason` field of the: [K8s ContainerStateWaiting](https://kubernetes.io/docs/reference/generated/kubernetes-api/v1.34/#containerstatewaiting-v1-core) or [K8s ContainerStateTerminated](https://kubernetes.io/docs/reference/generated/kubernetes-api/v1.34/#containerstateterminated-v1-core) | `"ContainerCreating"`, `"CrashLoopBackOff"`, `"CreateContainerConfigError"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.container.status.state` | `enum` | development |  | The state of the container. [K8s ContainerState](https://kubernetes.io/docs/reference/generated/kubernetes-api/v1.34/#containerstate-v1-core) | `"terminated"`, `"running"`, `"waiting"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.cronjob.annotation` | `template[string]` | stable |  | The cronjob annotation placed on the CronJob, the `<key>` being the annotation name, the value being the annotation value. | `"4"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.cronjob.label` | `template[string]` | stable |  | The label placed on the CronJob, the `<key>` being the label name, the value being the label value. | `"weekly"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.cronjob.name` | `string` | stable |  | The name of the CronJob. | `"opentelemetry"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.cronjob.uid` | `string` | stable |  | The UID of the CronJob. | `"275ecb36-5aa8-4c2a-9c47-d8bb681b9aff"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.daemonset.annotation` | `template[string]` | stable |  | The annotation placed on the DaemonSet, the `<key>` being the annotation name, the value being the annotation value, even if the value is empty. | `"1"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.daemonset.label` | `template[string]` | stable |  | The label placed on the DaemonSet, the `<key>` being the label name, the value being the label value, even if the value is empty. | `"guestbook"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.daemonset.name` | `string` | stable |  | The name of the DaemonSet. | `"opentelemetry"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.daemonset.uid` | `string` | stable |  | The UID of the DaemonSet. | `"275ecb36-5aa8-4c2a-9c47-d8bb681b9aff"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.deployment.annotation` | `template[string]` | stable |  | The annotation placed on the Deployment, the `<key>` being the annotation name, the value being the annotation value, even if the value is empty. | `"1"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.deployment.label` | `template[string]` | stable |  | The label placed on the Deployment, the `<key>` being the label name, the value being the label value, even if the value is empty. | `"guestbook"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.deployment.name` | `string` | stable |  | The name of the Deployment. | `"opentelemetry"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.deployment.uid` | `string` | stable |  | The UID of the Deployment. | `"275ecb36-5aa8-4c2a-9c47-d8bb681b9aff"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.hpa.metric.type` | `string` | development |  | The type of metric source for the horizontal pod autoscaler. | `"Resource"`, `"ContainerResource"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.hpa.name` | `string` | development |  | The name of the horizontal pod autoscaler. | `"opentelemetry"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.hpa.scaletargetref.api_version` | `string` | development |  | The API version of the target resource to scale for the HorizontalPodAutoscaler. | `"apps/v1"`, `"autoscaling/v2"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.hpa.scaletargetref.kind` | `string` | development |  | The kind of the target resource to scale for the HorizontalPodAutoscaler. | `"Deployment"`, `"StatefulSet"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.hpa.scaletargetref.name` | `string` | development |  | The name of the target resource to scale for the HorizontalPodAutoscaler. | `"my-deployment"`, `"my-statefulset"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.hpa.uid` | `string` | development |  | The UID of the horizontal pod autoscaler. | `"275ecb36-5aa8-4c2a-9c47-d8bb681b9aff"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.hugepage.size` | `string` | development |  | The size (identifier) of the K8s huge page. | `"2Mi"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.job.annotation` | `template[string]` | stable |  | The annotation placed on the Job, the `<key>` being the annotation name, the value being the annotation value, even if the value is empty. | `"1"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.job.label` | `template[string]` | stable |  | The label placed on the Job, the `<key>` being the label name, the value being the label value, even if the value is empty. | `"ci"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.job.name` | `string` | stable |  | The name of the Job. | `"opentelemetry"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.job.uid` | `string` | stable |  | The UID of the Job. | `"275ecb36-5aa8-4c2a-9c47-d8bb681b9aff"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.namespace.annotation` | `template[string]` | stable |  | The annotation placed on the Namespace, the `<key>` being the annotation name, the value being the annotation value, even if the value is empty. | `"0"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.namespace.label` | `template[string]` | stable |  | The label placed on the Namespace, the `<key>` being the label name, the value being the label value, even if the value is empty. | `"default"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.namespace.name` | `string` | stable |  | The name of the namespace that the pod is running in. | `"default"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.namespace.phase` | `enum` | development |  | The phase of the K8s namespace. | `"active"`, `"terminating"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.node.annotation` | `template[string]` | stable |  | The annotation placed on the Node, the `<key>` being the annotation name, the value being the annotation value, even if the value is empty. | `"0"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.node.condition.status` | `enum` | development |  | The status of the condition, one of True, False, Unknown. | `"true"`, `"false"`, `"unknown"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.node.condition.type` | `enum` | development |  | The condition type of a K8s Node. | `"Ready"`, `"DiskPressure"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.node.label` | `template[string]` | stable |  | The label placed on the Node, the `<key>` being the label name, the value being the label value, even if the value is empty. | `"arm64"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.node.name` | `string` | stable |  | The name of the Node. | `"node-1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.node.system_container.name` | `string` | development |  | The name of the system container running on the K8s Node. | `"kubelet"`, `"runtime"`, `"pods"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.node.uid` | `string` | stable |  | The UID of the Node. | `"1eb3a0c6-0477-4080-a9cb-0cb7db65c6a2"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.persistentvolume.annotation` | `template[string]` | development |  | The annotation placed on the PersistentVolume, the `<key>` being the annotation name, the value being the annotation value, even if the value is empty. | `"kubernetes.io/aws-ebs"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.persistentvolume.label` | `template[string]` | development |  | The label placed on the PersistentVolume, the `<key>` being the label name, the value being the label value, even if the value is empty. | `"ssd"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.persistentvolume.name` | `string` | development |  | The name of the PersistentVolume. | `"pv-data-01"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.persistentvolume.reclaim_policy` | `enum` | development |  | The reclaim policy of the PersistentVolume. | `"Delete"`, `"Retain"`, `"Recycle"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.persistentvolume.status.phase` | `enum` | development |  | The phase of the PersistentVolume. | `"Pending"`, `"Available"`, `"Bound"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.persistentvolume.uid` | `string` | development |  | The UID of the PersistentVolume. | `"275ecb36-5aa8-4c2a-9c47-d8bb681b9aff"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.persistentvolumeclaim.annotation` | `template[string]` | development |  | The annotation placed on the PersistentVolumeClaim, the `<key>` being the annotation name, the value being the annotation value, even if the value is empty. | `"kubernetes.io/aws-ebs"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.persistentvolumeclaim.label` | `template[string]` | development |  | The label placed on the PersistentVolumeClaim, the `<key>` being the label name, the value being the label value, even if the value is empty. | `"my-app"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.persistentvolumeclaim.name` | `string` | development |  | The name of the PersistentVolumeClaim. | `"pvc-data-01"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.persistentvolumeclaim.status.phase` | `enum` | development |  | The phase of the PersistentVolumeClaim. | `"Pending"`, `"Bound"`, `"Lost"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.persistentvolumeclaim.uid` | `string` | development |  | The UID of the PersistentVolumeClaim. | `"275ecb36-5aa8-4c2a-9c47-d8bb681b9aff"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.pod.annotation` | `template[string]` | stable |  | The annotation placed on the Pod, the `<key>` being the annotation name, the value being the annotation value. | `"true"`, `"x64"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.pod.hostname` | `string` | stable |  | Specifies the hostname of the Pod. | `"collector-gateway"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.pod.ip` | `string` | stable |  | IP address allocated to the Pod. | `"172.18.0.2"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.pod.label` | `template[string]` | stable |  | The label placed on the Pod, the `<key>` being the label name, the value being the label value. | `"my-app"`, `"x64"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.pod.labels` | `template[string]` | development | Replaced by `k8s.pod.label`. | Deprecated, use `k8s.pod.label` instead. | `"my-app"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.pod.name` | `string` | stable |  | The name of the Pod. | `"opentelemetry-pod-autoconf"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.pod.start_time` | `string` | stable |  | The start timestamp of the Pod. | `"2025-12-04T08:41:03Z"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.pod.status.phase` | `enum` | development |  | The phase for the pod. Corresponds to the `phase` field of the: [K8s PodStatus](https://kubernetes.io/docs/reference/generated/kubernetes-api/v1.33/#podstatus-v1-core) | `"Pending"`, `"Running"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.pod.status.reason` | `enum` | development |  | The reason for the pod state. Corresponds to the `reason` field of the: [K8s PodStatus](https://kubernetes.io/docs/reference/generated/kubernetes-api/v1.33/#podstatus-v1-core) | `"Evicted"`, `"NodeAffinity"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.pod.uid` | `string` | stable |  | The UID of the Pod. | `"275ecb36-5aa8-4c2a-9c47-d8bb681b9aff"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.replicaset.annotation` | `template[string]` | stable |  | The annotation placed on the ReplicaSet, the `<key>` being the annotation name, the value being the annotation value, even if the value is empty. | `"0"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.replicaset.label` | `template[string]` | stable |  | The label placed on the ReplicaSet, the `<key>` being the label name, the value being the label value, even if the value is empty. | `"guestbook"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.replicaset.name` | `string` | stable |  | The name of the ReplicaSet. | `"opentelemetry"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.replicaset.uid` | `string` | stable |  | The UID of the ReplicaSet. | `"275ecb36-5aa8-4c2a-9c47-d8bb681b9aff"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.replicationcontroller.name` | `string` | development |  | The name of the replication controller. | `"opentelemetry"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.replicationcontroller.uid` | `string` | development |  | The UID of the replication controller. | `"275ecb36-5aa8-4c2a-9c47-d8bb681b9aff"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.resourcequota.name` | `string` | development |  | The name of the resource quota. | `"opentelemetry"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.resourcequota.resource_name` | `string` | development |  | The name of the K8s resource a resource quota defines. | `"count/replicationcontrollers"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.resourcequota.uid` | `string` | development |  | The UID of the resource quota. | `"275ecb36-5aa8-4c2a-9c47-d8bb681b9aff"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.service.annotation` | `template[string]` | development |  | The annotation placed on the Service, the `<key>` being the annotation name, the value being the annotation value, even if the value is empty. | `"true"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.service.endpoint.address_type` | `enum` | development |  | The address type of the service endpoint. | `"IPv4"`, `"IPv6"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.service.endpoint.condition` | `enum` | development |  | The condition of the service endpoint. | `"ready"`, `"serving"`, `"terminating"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.service.endpoint.zone` | `string` | development |  | The zone of the service endpoint. | `"us-east-1a"`, `"us-west-2b"`, `"zone-a"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.service.label` | `template[string]` | development |  | The label placed on the Service, the `<key>` being the label name, the value being the label value, even if the value is empty. | `"my-service"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.service.name` | `string` | development |  | The name of the Service. | `"my-service"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.service.publish_not_ready_addresses` | `boolean` | development |  | Whether the Service publishes not-ready endpoints. | `true`, `false` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.service.selector` | `template[string]` | development |  | The selector key-value pair placed on the Service, the `<key>` being the selector key, the value being the selector value. | `"my-app"`, `"v1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.service.traffic_distribution` | `string` | development |  | The traffic distribution policy for the Service. | `"PreferSameZone"`, `"PreferSameNode"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.service.type` | `enum` | development |  | The type of the Kubernetes Service. | `"ClusterIP"`, `"NodePort"`, `"LoadBalancer"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.service.uid` | `string` | development |  | The UID of the Service. | `"275ecb36-5aa8-4c2a-9c47-d8bb681b9aff"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.statefulset.annotation` | `template[string]` | stable |  | The annotation placed on the StatefulSet, the `<key>` being the annotation name, the value being the annotation value, even if the value is empty. | `"1"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.statefulset.label` | `template[string]` | stable |  | The label placed on the StatefulSet, the `<key>` being the label name, the value being the label value, even if the value is empty. | `"guestbook"`, `""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.statefulset.name` | `string` | stable |  | The name of the StatefulSet. | `"opentelemetry"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.statefulset.uid` | `string` | stable |  | The UID of the StatefulSet. | `"275ecb36-5aa8-4c2a-9c47-d8bb681b9aff"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.storageclass.name` | `string` | development |  | The name of K8s [StorageClass](https://kubernetes.io/docs/reference/generated/kubernetes-api/v1.34/#storageclass-v1-storage-k8s-io) object. | `"gold.storageclass.storage.k8s.io"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.volume.name` | `string` | development |  | The name of the K8s volume. | `"volume0"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `k8s.volume.type` | `enum` | development |  | The type of the K8s volume. | `"emptyDir"`, `"persistentVolumeClaim"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `k8s.container.ephemeral_storage.fs_type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `logs` | development |  | For the container's log files usage (stdout/stderr). |
| `rootfs` | development |  | For the container's writable layer usage. |

### `k8s.container.status.reason` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `Completed` | development |  | The container has completed execution. |
| `ContainerCannotRun` | development |  | The container cannot run. |
| `ContainerCreating` | development |  | The container is being created. |
| `CrashLoopBackOff` | development |  | The container is in a crash loop back off state. |
| `CreateContainerConfigError` | development |  | There was an error creating the container configuration. |
| `ErrImagePull` | development |  | There was an error pulling the container image. |
| `Error` | development |  | There was an error with the container. |
| `ImagePullBackOff` | development |  | The container image pull is in back off state. |
| `OOMKilled` | development |  | The container was killed due to out of memory. |

### `k8s.container.status.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `running` | development |  | The container is running. |
| `terminated` | development |  | The container has terminated. |
| `waiting` | development |  | The container is waiting. |

### `k8s.namespace.phase` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `active` | development |  | Active namespace phase as described by [K8s API](https://pkg.go.dev/k8s.io/api@v0.31.3/core/v1#NamespacePhase) |
| `terminating` | development |  | Terminating namespace phase as described by [K8s API](https://pkg.go.dev/k8s.io/api@v0.31.3/core/v1#NamespacePhase) |

### `k8s.node.condition.status` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `false` | development |  | `false`. |
| `true` | development |  | `true`. |
| `unknown` | development |  | `unknown`. |

### `k8s.node.condition.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `DiskPressure` | development |  | Pressure exists on the disk size—that is, if the disk capacity is low |
| `MemoryPressure` | development |  | Pressure exists on the node memory—that is, if the node memory is low |
| `NetworkUnavailable` | development |  | The network for the node is not correctly configured |
| `PIDPressure` | development |  | Pressure exists on the processes—that is, if there are too many processes on the node |
| `Ready` | development |  | The node is healthy and ready to accept pods |

### `k8s.persistentvolume.reclaim_policy` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `Delete` | development |  | The volume will be deleted when released from its claim. |
| `Recycle` | development |  | The volume will be recycled (basic scrub) when released from its claim. |
| `Retain` | development |  | The volume will be retained when released from its claim. |

### `k8s.persistentvolume.status.phase` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `Available` | development |  | The volume is available and not yet bound to a claim. |
| `Bound` | development |  | The volume is bound to a claim. |
| `Failed` | development |  | The volume has failed its automatic reclamation. |
| `Pending` | development |  | The volume is being provisioned. |
| `Released` | development |  | The claim has been deleted but the volume is not yet available. |

### `k8s.persistentvolumeclaim.status.phase` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `Bound` | development |  | The claim is bound to a volume. |
| `Lost` | development |  | The claim has lost its underlying volume (the volume does not exist anymore). |
| `Pending` | development |  | The claim has not yet been bound to a volume. |

### `k8s.pod.status.phase` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `Failed` | development |  | All containers in the pod have terminated, and at least one container has terminated in a failure (exited with a non-zero exit code or was stopped by the system). |
| `Pending` | development |  | The pod has been accepted by the system, but one or more of the containers has not been started. This includes time before being bound to a node, as well as time spent pulling images onto the host. |
| `Running` | development |  | The pod has been bound to a node and all of the containers have been started. At least one container is still running or is in the process of being restarted. |
| `Succeeded` | development |  | All containers in the pod have voluntarily terminated with a container exit code of 0, and the system is not going to restart any of these containers. |
| `Unknown` | development |  | For some reason the state of the pod could not be obtained, typically due to an error in communicating with the host of the pod. |

### `k8s.pod.status.reason` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `Evicted` | development |  | The pod is evicted. |
| `NodeAffinity` | development |  | The pod is in a status because of its node affinity |
| `NodeLost` | development |  | The reason on a pod when its state cannot be confirmed as kubelet is unresponsive on the node it is (was) running. |
| `Shutdown` | development |  | The node is shutdown |
| `UnexpectedAdmissionError` | development |  | The pod was rejected admission to the node because of an error during admission that could not be categorized. |

### `k8s.service.endpoint.address_type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `FQDN` | development |  | FQDN address type |
| `IPv4` | development |  | IPv4 address type |
| `IPv6` | development |  | IPv6 address type |

### `k8s.service.endpoint.condition` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `ready` | development |  | The endpoint is ready to receive new connections. |
| `serving` | development |  | The endpoint is currently handling traffic. |
| `terminating` | development |  | The endpoint is in the process of shutting down. |

### `k8s.service.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `ClusterIP` | development |  | ClusterIP service type |
| `ExternalName` | development |  | ExternalName service type |
| `LoadBalancer` | development |  | LoadBalancer service type |
| `NodePort` | development |  | NodePort service type |

### `k8s.volume.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `configMap` | development |  | A [configMap](https://kubernetes.io/docs/concepts/storage/volumes/#configmap) volume |
| `downwardAPI` | development |  | A [downwardAPI](https://kubernetes.io/docs/concepts/storage/volumes/#downwardapi) volume |
| `emptyDir` | development |  | An [emptyDir](https://kubernetes.io/docs/concepts/storage/volumes/#emptydir) volume |
| `local` | development |  | A [local](https://kubernetes.io/docs/concepts/storage/volumes/#local) volume |
| `persistentVolumeClaim` | development |  | A [persistentVolumeClaim](https://kubernetes.io/docs/concepts/storage/volumes/#persistentvolumeclaim) volume |
| `secret` | development |  | A [secret](https://kubernetes.io/docs/concepts/storage/volumes/#secret) volume |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `k8s.container.cpu.limit` | updowncounter | `{cpu}` | development | Replaced by `k8s.container.cpu.limit.desired`. | Deprecated, use `k8s.container.cpu.limit.desired` and `k8s.container.cpu.limit.current` instead. |
| `k8s.container.cpu.limit.current` | updowncounter | `{cpu}` | development |  | Maximum CPU resource limit currently configured for a running container. |
| `k8s.container.cpu.limit.desired` | updowncounter | `{cpu}` | development |  | Maximum CPU resource limit as defined by the container spec. |
| `k8s.container.cpu.limit.utilization` | gauge | `1` | development |  | The ratio of container CPU usage to its current CPU limit. |
| `k8s.container.cpu.limit_utilization` | gauge | `1` | development | Replaced by `k8s.container.cpu.limit.utilization`. | Deprecated, use `k8s.container.cpu.limit.utilization` instead. |
| `k8s.container.cpu.request` | updowncounter | `{cpu}` | development | Replaced by `k8s.container.cpu.request.desired`. | Deprecated, use `k8s.container.cpu.request.desired` and `k8s.container.cpu.request.current` instead. |
| `k8s.container.cpu.request.current` | updowncounter | `{cpu}` | development |  | CPU resource requested currently configured for a running container. |
| `k8s.container.cpu.request.desired` | updowncounter | `{cpu}` | development |  | CPU resource requested as defined by the container spec. |
| `k8s.container.cpu.request.utilization` | gauge | `1` | development |  | The ratio of container CPU usage to its current CPU request. |
| `k8s.container.cpu.request_utilization` | gauge | `1` | development | Replaced by `k8s.container.cpu.request.utilization`. | Deprecated, use `k8s.container.cpu.request.utilization` instead. |
| `k8s.container.ephemeral_storage.limit` | updowncounter | `By` | development |  | Maximum ephemeral storage resource limit set for the container. |
| `k8s.container.ephemeral_storage.request` | updowncounter | `By` | development |  | Ephemeral storage resource requested for the container. |
| `k8s.container.ephemeral_storage.usage` | updowncounter | `By` | development |  | The ephemeral storage used by a container. |
| `k8s.container.memory.limit` | updowncounter | `By` | development | Replaced by `k8s.container.memory.limit.desired`. | Deprecated, use `k8s.container.memory.limit.desired` and `k8s.container.memory.limit.current` instead. |
| `k8s.container.memory.limit.current` | updowncounter | `By` | development |  | Maximum memory resource limit currently configured for a running container. |
| `k8s.container.memory.limit.desired` | updowncounter | `By` | development |  | Maximum memory resource limit as defined by the container spec. |
| `k8s.container.memory.request` | updowncounter | `By` | development | Replaced by `k8s.container.memory.request.desired`. | Deprecated, use `k8s.container.memory.request.desired` and `k8s.container.memory.request.current` instead. |
| `k8s.container.memory.request.current` | updowncounter | `By` | development |  | Memory resource request currently configured for a running container. |
| `k8s.container.memory.request.desired` | updowncounter | `By` | development |  | Memory resource requested as defined by the container spec. |
| `k8s.container.ready` | updowncounter | `{container}` | development |  | Indicates whether the container is currently marked as ready to accept traffic, based on its readiness probe (1 = ready, 0 = not ready). |
| `k8s.container.restart.count` | updowncounter | `{restart}` | development |  | Describes how many times the container has restarted (since the last counter reset). |
| `k8s.container.status.reason` | updowncounter | `{container}` | development |  | Describes the number of K8s containers that are currently in a state for a given reason. |
| `k8s.container.status.state` | updowncounter | `{container}` | development |  | Describes the number of K8s containers that are currently in a given state. |
| `k8s.container.storage.limit` | updowncounter | `By` | development |  | Maximum storage resource limit set for the container. |
| `k8s.container.storage.request` | updowncounter | `By` | development |  | Storage resource requested for the container. |
| `k8s.cronjob.active_jobs` | updowncounter | `{job}` | development | Replaced by `k8s.cronjob.job.active`. | Deprecated, use `k8s.cronjob.job.active` instead. |
| `k8s.cronjob.job.active` | updowncounter | `{job}` | development |  | The number of actively running jobs for a cronjob. |
| `k8s.daemonset.current_scheduled_nodes` | updowncounter | `{node}` | development | Replaced by `k8s.daemonset.node.current_scheduled`. | Deprecated, use `k8s.daemonset.node.current_scheduled` instead. |
| `k8s.daemonset.desired_scheduled_nodes` | updowncounter | `{node}` | development | Replaced by `k8s.daemonset.node.desired_scheduled`. | Deprecated, use `k8s.daemonset.node.desired_scheduled` instead. |
| `k8s.daemonset.misscheduled_nodes` | updowncounter | `{node}` | development | Replaced by `k8s.daemonset.node.misscheduled`. | Deprecated, use `k8s.daemonset.node.misscheduled` instead. |
| `k8s.daemonset.node.current_scheduled` | updowncounter | `{node}` | development |  | Number of nodes that are running at least 1 daemon pod and are supposed to run the daemon pod. |
| `k8s.daemonset.node.desired_scheduled` | updowncounter | `{node}` | development |  | Number of nodes that should be running the daemon pod (including nodes currently running the daemon pod). |
| `k8s.daemonset.node.misscheduled` | updowncounter | `{node}` | development |  | Number of nodes that are running the daemon pod, but are not supposed to run the daemon pod. |
| `k8s.daemonset.node.ready` | updowncounter | `{node}` | development |  | Number of nodes that should be running the daemon pod and have one or more of the daemon pod running and ready. |
| `k8s.daemonset.ready_nodes` | updowncounter | `{node}` | development | Replaced by `k8s.daemonset.node.ready`. | Deprecated, use `k8s.daemonset.node.ready` instead. |
| `k8s.deployment.available_pods` | updowncounter | `{pod}` | development | Replaced by `k8s.deployment.pod.available`. | Deprecated, use `k8s.deployment.pod.available` instead. |
| `k8s.deployment.desired_pods` | updowncounter | `{pod}` | development | Replaced by `k8s.deployment.pod.desired`. | Deprecated, use `k8s.deployment.pod.desired` instead. |
| `k8s.deployment.pod.available` | updowncounter | `{pod}` | development |  | Total number of available replica pods (ready for at least minReadySeconds) targeted by this deployment. |
| `k8s.deployment.pod.desired` | updowncounter | `{pod}` | development |  | Number of desired replica pods in this deployment. |
| `k8s.hpa.current_pods` | updowncounter | `{pod}` | development | Replaced by `k8s.hpa.pod.current`. | Deprecated, use `k8s.hpa.pod.current` instead. |
| `k8s.hpa.desired_pods` | updowncounter | `{pod}` | development | Replaced by `k8s.hpa.pod.desired`. | Deprecated, use `k8s.hpa.pod.desired` instead. |
| `k8s.hpa.max_pods` | updowncounter | `{pod}` | development | Replaced by `k8s.hpa.pod.max`. | Deprecated, use `k8s.hpa.pod.max` instead. |
| `k8s.hpa.metric.target.cpu.average_utilization` | gauge | `1` | development |  | Target average utilization, in percentage, for CPU resource in HPA config. |
| `k8s.hpa.metric.target.cpu.average_value` | gauge | `{cpu}` | development |  | Target average value for CPU resource in HPA config. |
| `k8s.hpa.metric.target.cpu.value` | gauge | `{cpu}` | development |  | Target value for CPU resource in HPA config. |
| `k8s.hpa.min_pods` | updowncounter | `{pod}` | development | Replaced by `k8s.hpa.pod.min`. | Deprecated, use `k8s.hpa.pod.min` instead. |
| `k8s.hpa.pod.current` | updowncounter | `{pod}` | development |  | Current number of replica pods managed by this horizontal pod autoscaler, as last seen by the autoscaler. |
| `k8s.hpa.pod.desired` | updowncounter | `{pod}` | development |  | Desired number of replica pods managed by this horizontal pod autoscaler, as last calculated by the autoscaler. |
| `k8s.hpa.pod.max` | updowncounter | `{pod}` | development |  | The upper limit for the number of replica pods to which the autoscaler can scale up. |
| `k8s.hpa.pod.min` | updowncounter | `{pod}` | development |  | The lower limit for the number of replica pods to which the autoscaler can scale down. |
| `k8s.job.active_pods` | updowncounter | `{pod}` | development | Replaced by `k8s.job.pod.active`. | Deprecated, use `k8s.job.pod.active` instead. |
| `k8s.job.desired_successful_pods` | updowncounter | `{pod}` | development | Replaced by `k8s.job.pod.desired_successful`. | Deprecated, use `k8s.job.pod.desired_successful` instead. |
| `k8s.job.failed_pods` | updowncounter | `{pod}` | development | Replaced by `k8s.job.pod.failed`. | Deprecated, use `k8s.job.pod.failed` instead. |
| `k8s.job.max_parallel_pods` | updowncounter | `{pod}` | development | Replaced by `k8s.job.pod.max_parallel`. | Deprecated, use `k8s.job.pod.max_parallel` instead. |
| `k8s.job.pod.active` | updowncounter | `{pod}` | development |  | The number of pending and actively running pods for a job. |
| `k8s.job.pod.desired_successful` | updowncounter | `{pod}` | development |  | The desired number of successfully finished pods the job should be run with. |
| `k8s.job.pod.failed` | updowncounter | `{pod}` | development |  | The number of pods which reached phase Failed for a job. |
| `k8s.job.pod.max_parallel` | updowncounter | `{pod}` | development |  | The max desired number of pods the job should run at any given time. |
| `k8s.job.pod.successful` | updowncounter | `{pod}` | development |  | The number of pods which reached phase Succeeded for a job. |
| `k8s.job.successful_pods` | updowncounter | `{pod}` | development | Replaced by `k8s.job.pod.successful`. | Deprecated, use `k8s.job.pod.successful` instead. |
| `k8s.namespace.phase` | updowncounter | `{namespace}` | development |  | Describes number of K8s namespaces that are currently in a given phase. |
| `k8s.node.allocatable.cpu` | updowncounter | `{cpu}` | development | Replaced by `k8s.node.cpu.allocatable`. | Deprecated, use `k8s.node.cpu.allocatable` instead. |
| `k8s.node.allocatable.ephemeral_storage` | updowncounter | `By` | development | Replaced by `k8s.node.ephemeral_storage.allocatable`. | Deprecated, use `k8s.node.ephemeral_storage.allocatable` instead. |
| `k8s.node.allocatable.memory` | updowncounter | `By` | development | Replaced by `k8s.node.memory.allocatable`. | Deprecated, use `k8s.node.memory.allocatable` instead. |
| `k8s.node.allocatable.pods` | updowncounter | `{pod}` | development | Replaced by `k8s.node.pod.allocatable`. | Deprecated, use `k8s.node.pod.allocatable` instead. |
| `k8s.node.condition.status` | updowncounter | `{node}` | development |  | Describes the condition of a particular Node. |
| `k8s.node.cpu.allocatable` | updowncounter | `{cpu}` | development |  | Amount of CPU allocatable on the node. |
| `k8s.node.cpu.time` | counter | `s` | release_candidate |  | Total CPU time consumed. |
| `k8s.node.cpu.usage` | gauge | `{cpu}` | development |  | Node's CPU usage, measured in CPUs. Range from 0 to the number of allocatable CPUs. |
| `k8s.node.ephemeral_storage.allocatable` | updowncounter | `By` | development |  | Amount of ephemeral-storage allocatable on the node. |
| `k8s.node.filesystem.available` | updowncounter | `By` | development |  | Node filesystem available bytes. |
| `k8s.node.filesystem.capacity` | updowncounter | `By` | development |  | Node filesystem capacity. |
| `k8s.node.filesystem.inode.count` | updowncounter | `{inode}` | development |  | The total inodes in the node's root filesystem. |
| `k8s.node.filesystem.inode.free` | updowncounter | `{inode}` | development |  | The free inodes in the node's root filesystem. |
| `k8s.node.filesystem.usage` | updowncounter | `By` | development |  | Node filesystem usage. |
| `k8s.node.memory.allocatable` | updowncounter | `By` | development |  | Amount of memory allocatable on the node. |
| `k8s.node.memory.available` | updowncounter | `By` | release_candidate |  | Node memory available. |
| `k8s.node.memory.paging.faults` | counter | `{fault}` | development | Replaced by `k8s.node.paging.faults`. | Deprecated, use `k8s.node.paging.faults` instead. |
| `k8s.node.memory.rss` | updowncounter | `By` | release_candidate |  | Node memory RSS. |
| `k8s.node.memory.usage` | updowncounter | `By` | release_candidate |  | Memory usage of the Node. |
| `k8s.node.memory.working_set` | updowncounter | `By` | release_candidate |  | Node memory working set. |
| `k8s.node.network.errors` | counter | `{error}` | development |  | Node network errors. |
| `k8s.node.network.io` | counter | `By` | development |  | Network bytes for the Node. |
| `k8s.node.paging.faults` | counter | `{fault}` | development |  | Node memory paging faults. |
| `k8s.node.pod.allocatable` | updowncounter | `{pod}` | development |  | Amount of pods allocatable on the node. |
| `k8s.node.system_container.cpu.time` | counter | `s` | development |  | Node's system container CPU time. |
| `k8s.node.system_container.cpu.usage` | gauge | `{cpu}` | development |  | Node's system container CPU usage, measured in CPUs. |
| `k8s.node.system_container.memory.usage` | updowncounter | `By` | development |  | Node's system container memory usage. |
| `k8s.node.system_container.memory.working_set` | updowncounter | `By` | development |  | The amount of working set memory. |
| `k8s.node.uptime` | gauge | `s` | development |  | The time the Node has been running. |
| `k8s.persistentvolume.status.phase` | updowncounter | `{persistentvolume}` | development |  | Number of PersistentVolumes in a given phase. |
| `k8s.persistentvolume.storage.capacity` | updowncounter | `By` | development |  | The storage capacity of the PersistentVolume. |
| `k8s.persistentvolumeclaim.status.phase` | updowncounter | `{persistentvolumeclaim}` | development |  | Number of PersistentVolumeClaims in a given phase. |
| `k8s.persistentvolumeclaim.storage.capacity` | updowncounter | `By` | development |  | The actual storage capacity provisioned for the PersistentVolumeClaim. |
| `k8s.persistentvolumeclaim.storage.request` | updowncounter | `By` | development |  | The storage requested by the PersistentVolumeClaim. |
| `k8s.pod.cpu.time` | counter | `s` | release_candidate |  | Total CPU time consumed. |
| `k8s.pod.cpu.usage` | gauge | `{cpu}` | development |  | Pod's CPU usage, measured in CPUs. Range from 0 to the number of allocatable CPUs. |
| `k8s.pod.filesystem.available` | updowncounter | `By` | development |  | Pod filesystem available bytes. |
| `k8s.pod.filesystem.capacity` | updowncounter | `By` | development |  | Pod filesystem capacity. |
| `k8s.pod.filesystem.usage` | updowncounter | `By` | development |  | Pod filesystem usage. |
| `k8s.pod.memory.available` | updowncounter | `By` | release_candidate |  | Pod memory available. |
| `k8s.pod.memory.paging.faults` | counter | `{fault}` | development | Replaced by `k8s.pod.paging.faults`. | Deprecated, use `k8s.pod.paging.faults` instead. |
| `k8s.pod.memory.rss` | updowncounter | `By` | release_candidate |  | Pod memory RSS. |
| `k8s.pod.memory.usage` | updowncounter | `By` | release_candidate |  | Memory usage of the Pod. |
| `k8s.pod.memory.working_set` | updowncounter | `By` | release_candidate |  | Pod memory working set. |
| `k8s.pod.network.errors` | counter | `{error}` | development |  | Pod network errors. |
| `k8s.pod.network.io` | counter | `By` | development |  | Network bytes for the Pod. |
| `k8s.pod.paging.faults` | counter | `{fault}` | development |  | Pod memory paging faults. |
| `k8s.pod.status.phase` | updowncounter | `{pod}` | development |  | Describes number of K8s Pods that are currently in a given phase. |
| `k8s.pod.status.reason` | updowncounter | `{pod}` | development |  | Describes the number of K8s Pods that are currently in a state for a given reason. |
| `k8s.pod.uptime` | gauge | `s` | development |  | The time the Pod has been running. |
| `k8s.pod.volume.available` | updowncounter | `By` | development |  | Pod volume storage space available. |
| `k8s.pod.volume.capacity` | updowncounter | `By` | development |  | Pod volume total capacity. |
| `k8s.pod.volume.inode.count` | updowncounter | `{inode}` | development |  | The total inodes in the filesystem of the Pod's volume. |
| `k8s.pod.volume.inode.free` | updowncounter | `{inode}` | development |  | The free inodes in the filesystem of the Pod's volume. |
| `k8s.pod.volume.inode.used` | updowncounter | `{inode}` | development |  | The inodes used by the filesystem of the Pod's volume. |
| `k8s.pod.volume.usage` | updowncounter | `By` | development |  | Pod volume usage. |
| `k8s.replicaset.available_pods` | updowncounter | `{pod}` | development | Replaced by `k8s.replicaset.pod.available`. | Deprecated, use `k8s.replicaset.pod.available` instead. |
| `k8s.replicaset.desired_pods` | updowncounter | `{pod}` | development | Replaced by `k8s.replicaset.pod.desired`. | Deprecated, use `k8s.replicaset.pod.desired` instead. |
| `k8s.replicaset.pod.available` | updowncounter | `{pod}` | development |  | Total number of available replica pods (ready for at least minReadySeconds) targeted by this replicaset. |
| `k8s.replicaset.pod.desired` | updowncounter | `{pod}` | development |  | Number of desired replica pods in this replicaset. |
| `k8s.replication_controller.available_pods` | updowncounter | `{pod}` | development | Replaced by `k8s.replicationcontroller.pod.available`. | Deprecated, use `k8s.replicationcontroller.pod.available` instead. |
| `k8s.replication_controller.desired_pods` | updowncounter | `{pod}` | development | Replaced by `k8s.replicationcontroller.pod.desired`. | Deprecated, use `k8s.replicationcontroller.pod.desired` instead. |
| `k8s.replicationcontroller.available_pods` | updowncounter | `{pod}` | development | Replaced by `k8s.replicationcontroller.pod.available`. | Deprecated, use `k8s.replicationcontroller.pod.available` instead. |
| `k8s.replicationcontroller.desired_pods` | updowncounter | `{pod}` | development | Replaced by `k8s.replicationcontroller.pod.desired`. | Deprecated, use `k8s.replicationcontroller.pod.desired` instead. |
| `k8s.replicationcontroller.pod.available` | updowncounter | `{pod}` | development |  | Total number of available replica pods (ready for at least minReadySeconds) targeted by this replication controller. |
| `k8s.replicationcontroller.pod.desired` | updowncounter | `{pod}` | development |  | Number of desired replica pods in this replication controller. |
| `k8s.resourcequota.cpu.limit.hard` | updowncounter | `{cpu}` | development |  | The CPU limits in a specific namespace. The value represents the configured quota limit of the resource in the namespace. |
| `k8s.resourcequota.cpu.limit.used` | updowncounter | `{cpu}` | development |  | The CPU limits in a specific namespace. The value represents the current observed total usage of the resource in the namespace. |
| `k8s.resourcequota.cpu.request.hard` | updowncounter | `{cpu}` | development |  | The CPU requests in a specific namespace. The value represents the configured quota limit of the resource in the namespace. |
| `k8s.resourcequota.cpu.request.used` | updowncounter | `{cpu}` | development |  | The CPU requests in a specific namespace. The value represents the current observed total usage of the resource in the namespace. |
| `k8s.resourcequota.ephemeral_storage.limit.hard` | updowncounter | `By` | development |  | The sum of local ephemeral storage limits in the namespace. The value represents the configured quota limit of the resource in the namespace. |
| `k8s.resourcequota.ephemeral_storage.limit.used` | updowncounter | `By` | development |  | The sum of local ephemeral storage limits in the namespace. The value represents the current observed total usage of the resource in the namespace. |
| `k8s.resourcequota.ephemeral_storage.request.hard` | updowncounter | `By` | development |  | The sum of local ephemeral storage requests in the namespace. The value represents the configured quota limit of the resource in the namespace. |
| `k8s.resourcequota.ephemeral_storage.request.used` | updowncounter | `By` | development |  | The sum of local ephemeral storage requests in the namespace. The value represents the current observed total usage of the resource in the namespace. |
| `k8s.resourcequota.hugepage_count.request.hard` | updowncounter | `{hugepage}` | development |  | The huge page requests in a specific namespace. The value represents the configured quota limit of the resource in the namespace. |
| `k8s.resourcequota.hugepage_count.request.used` | updowncounter | `{hugepage}` | development |  | The huge page requests in a specific namespace. The value represents the current observed total usage of the resource in the namespace. |
| `k8s.resourcequota.memory.limit.hard` | updowncounter | `By` | development |  | The memory limits in a specific namespace. The value represents the configured quota limit of the resource in the namespace. |
| `k8s.resourcequota.memory.limit.used` | updowncounter | `By` | development |  | The memory limits in a specific namespace. The value represents the current observed total usage of the resource in the namespace. |
| `k8s.resourcequota.memory.request.hard` | updowncounter | `By` | development |  | The memory requests in a specific namespace. The value represents the configured quota limit of the resource in the namespace. |
| `k8s.resourcequota.memory.request.used` | updowncounter | `By` | development |  | The memory requests in a specific namespace. The value represents the current observed total usage of the resource in the namespace. |
| `k8s.resourcequota.object_count.hard` | updowncounter | `{object}` | development |  | The object count limits in a specific namespace. The value represents the configured quota limit of the resource in the namespace. |
| `k8s.resourcequota.object_count.used` | updowncounter | `{object}` | development |  | The object count limits in a specific namespace. The value represents the current observed total usage of the resource in the namespace. |
| `k8s.resourcequota.persistentvolumeclaim_count.hard` | updowncounter | `{persistentvolumeclaim}` | development |  | The total number of PersistentVolumeClaims that can exist in the namespace. The value represents the configured quota limit of the resource in the namespace. |
| `k8s.resourcequota.persistentvolumeclaim_count.used` | updowncounter | `{persistentvolumeclaim}` | development |  | The total number of PersistentVolumeClaims that can exist in the namespace. The value represents the current observed total usage of the resource in the namespace. |
| `k8s.resourcequota.storage.request.hard` | updowncounter | `By` | development |  | The storage requests in a specific namespace. The value represents the configured quota limit of the resource in the namespace. |
| `k8s.resourcequota.storage.request.used` | updowncounter | `By` | development |  | The storage requests in a specific namespace. The value represents the current observed total usage of the resource in the namespace. |
| `k8s.service.endpoint.count` | gauge | `{endpoint}` | development |  | Number of endpoints for a service by condition and address type. |
| `k8s.service.load_balancer.ingress.count` | gauge | `{ingress}` | development |  | Number of load balancer ingress points (external IPs/hostnames) assigned to the service. |
| `k8s.statefulset.current_pods` | updowncounter | `{pod}` | development | Replaced by `k8s.statefulset.pod.current`. | Deprecated, use `k8s.statefulset.pod.current` instead. |
| `k8s.statefulset.desired_pods` | updowncounter | `{pod}` | development | Replaced by `k8s.statefulset.pod.desired`. | Deprecated, use `k8s.statefulset.pod.desired` instead. |
| `k8s.statefulset.pod.current` | updowncounter | `{pod}` | development |  | The number of replica pods created by the statefulset controller from the statefulset version indicated by currentRevision. |
| `k8s.statefulset.pod.desired` | updowncounter | `{pod}` | development |  | Number of desired replica pods in this statefulset. |
| `k8s.statefulset.pod.ready` | updowncounter | `{pod}` | development |  | The number of replica pods created for this statefulset with a Ready Condition. |
| `k8s.statefulset.pod.updated` | updowncounter | `{pod}` | development |  | Number of replica pods created by the statefulset controller from the statefulset version indicated by updateRevision. |
| `k8s.statefulset.ready_pods` | updowncounter | `{pod}` | development | Replaced by `k8s.statefulset.pod.ready`. | Deprecated, use `k8s.statefulset.pod.ready` instead. |
| `k8s.statefulset.updated_pods` | updowncounter | `{pod}` | development | Replaced by `k8s.statefulset.pod.updated`. | Deprecated, use `k8s.statefulset.pod.updated` instead. |
