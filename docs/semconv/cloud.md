# `cloud` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `cloud.account.id` | `string` | development |  | The cloud account ID the resource is assigned to. | `"111111111111"`, `"opentelemetry"`, `"22222222-2222-2222-2222-222222222222"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cloud.availability_zone` | `string` | development |  | Cloud regions often have multiple, isolated locations known as zones to increase availability. Availability zone represents the zone where the resource is running. | `"us-east-1c"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cloud.platform` | `enum` | development |  | The cloud platform in use. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cloud.provider` | `enum` | development |  | Name of the cloud provider. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cloud.region` | `string` | development |  | The geographical region within a cloud provider. When associated with a resource, this attribute specifies the region where the resource operates. When calling services or APIs deployed on a cloud, this attribute identifies the region where the called destination is deployed. | `"us-central1"`, `"us-east-1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cloud.resource_id` | `string` | development |  | Cloud provider-specific native identifier of the monitored cloud resource (e.g. an [ARN](https://docs.aws.amazon.com/general/latest/gr/aws-arns-and-namespaces.html) on AWS, a [fully qualified resource ID](https://learn.microsoft.com/rest/api/resources/resources/get-by-id) on Azure, a [full resource name](https://google.aip.dev/122#full-resource-names) on GCP) | `"arn:aws:lambda:REGION:ACCOUNT_ID:function:my-function"`, `"//run.googleapis.com/projects/PROJECT_ID/locations/LOCATION_ID/services/SERVICE_ID"`, `"/subscriptions/<SUBSCRIPTION_GUID>/resourceGroups/<RG>/providers/Microsoft.Web/sites/<FUNCAPP>/functions/<FUNC>"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `cloud.platform` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `akamai_cloud.compute` | development |  | Akamai Cloud Compute |
| `alibaba_cloud_ecs` | development |  | Alibaba Cloud Elastic Compute Service |
| `alibaba_cloud_fc` | development |  | Alibaba Cloud Function Compute |
| `alibaba_cloud_openshift` | development |  | Red Hat OpenShift on Alibaba Cloud |
| `aws_app_runner` | development |  | AWS App Runner |
| `aws_ec2` | development |  | AWS Elastic Compute Cloud |
| `aws_ecs` | development |  | AWS Elastic Container Service |
| `aws_eks` | development |  | AWS Elastic Kubernetes Service |
| `aws_elastic_beanstalk` | development |  | AWS Elastic Beanstalk |
| `aws_lambda` | development |  | AWS Lambda |
| `aws_openshift` | development |  | Red Hat OpenShift on AWS (ROSA) |
| `azure.aks` | development |  | Azure Kubernetes Service |
| `azure.app_service` | development |  | Azure App Service |
| `azure.container_apps` | development |  | Azure Container Apps |
| `azure.container_instances` | development |  | Azure Container Instances |
| `azure.functions` | development |  | Azure Functions |
| `azure.openshift` | development |  | Azure Red Hat OpenShift |
| `azure.vm` | development |  | Azure Virtual Machines |
| `azure_aks` | development | Replaced by `azure.aks`. | Azure Kubernetes Service |
| `azure_app_service` | development | Replaced by `azure.app_service`. | Azure App Service |
| `azure_container_apps` | development | Replaced by `azure.container_apps`. | Azure Container Apps |
| `azure_container_instances` | development | Replaced by `azure.container_instances`. | Azure Container Instances |
| `azure_functions` | development | Replaced by `azure.functions`. | Azure Functions |
| `azure_openshift` | development | Replaced by `azure.openshift`. | Azure Red Hat OpenShift |
| `azure_vm` | development | Replaced by `azure.vm`. | Azure Virtual Machines |
| `gcp.agent_engine` | development |  | Google Vertex AI Agent Engine |
| `gcp_app_engine` | development |  | Google Cloud App Engine (GAE) |
| `gcp_bare_metal_solution` | development |  | Google Bare Metal Solution (BMS) |
| `gcp_cloud_functions` | development |  | Google Cloud Functions (GCF) |
| `gcp_cloud_run` | development |  | Google Cloud Run |
| `gcp_compute_engine` | development |  | Google Cloud Compute Engine (GCE) |
| `gcp_kubernetes_engine` | development |  | Google Cloud Kubernetes Engine (GKE) |
| `gcp_openshift` | development |  | Red Hat OpenShift on Google Cloud |
| `hetzner.cloud_server` | development |  | Server on Hetzner Cloud |
| `ibm_cloud_openshift` | development |  | Red Hat OpenShift on IBM Cloud |
| `oracle_cloud_compute` | development |  | Compute on Oracle Cloud Infrastructure (OCI) |
| `oracle_cloud_oke` | development |  | Kubernetes Engine (OKE) on Oracle Cloud Infrastructure (OCI) |
| `scaleway_cloud_compute` | development |  | Compute on Scaleway Cloud |
| `tencent_cloud_cvm` | development |  | Tencent Cloud Cloud Virtual Machine (CVM) |
| `tencent_cloud_eks` | development |  | Tencent Cloud Elastic Kubernetes Service (EKS) |
| `tencent_cloud_scf` | development |  | Tencent Cloud Serverless Cloud Function (SCF) |
| `vultr.cloud_compute` | development |  | Vultr Cloud Compute |

### `cloud.provider` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `akamai_cloud` | development |  | Akamai Cloud |
| `alibaba_cloud` | development |  | Alibaba Cloud |
| `aws` | development |  | Amazon Web Services |
| `azure` | development |  | Microsoft Azure |
| `gcp` | development |  | Google Cloud Platform |
| `heroku` | development |  | Heroku Platform as a Service |
| `hetzner` | development |  | Hetzner |
| `ibm_cloud` | development |  | IBM Cloud |
| `oracle_cloud` | development |  | Oracle Cloud Infrastructure (OCI) |
| `scaleway_cloud` | development |  | Scaleway Cloud |
| `tencent_cloud` | development |  | Tencent Cloud |
| `vultr` | development |  | Vultr |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
