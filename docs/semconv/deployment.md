# `deployment` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `deployment.environment` | `string` | development | Replaced by `deployment.environment.name`. | Deprecated, use `deployment.environment.name` instead. | `"staging"`, `"production"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `deployment.environment.name` | `enum` | stable |  | Name of the [deployment environment](https://wikipedia.org/wiki/Deployment_environment) (aka deployment tier). | `"staging"`, `"production"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `deployment.id` | `string` | development |  | The ID of the deployment. | `"1208"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `deployment.name` | `string` | development |  | The name of the deployment. | `"deploy my app"`, `"deploy-frontend"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `deployment.status` | `enum` | development |  | The status of the deployment. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `deployment.environment.name` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `development` | stable |  | Development environment |
| `production` | stable |  | Production environment |
| `staging` | stable |  | Staging environment |
| `test` | stable |  | Testing environment |

### `deployment.status` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `failed` | development |  | failed |
| `succeeded` | development |  | succeeded |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
