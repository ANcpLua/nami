# `cloudfoundry` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `cloudfoundry.app.id` | `string` | development |  | The guid of the application. | `"218fc5a9-a5f1-4b54-aa05-46717d0ab26d"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cloudfoundry.app.instance.id` | `string` | development |  | The index of the application instance. 0 when just one instance is active. | `"0"`, `"1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cloudfoundry.app.name` | `string` | development |  | The name of the application. | `"my-app-name"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cloudfoundry.org.id` | `string` | development |  | The guid of the CloudFoundry org the application is running in. | `"218fc5a9-a5f1-4b54-aa05-46717d0ab26d"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cloudfoundry.org.name` | `string` | development |  | The name of the CloudFoundry organization the app is running in. | `"my-org-name"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cloudfoundry.process.id` | `string` | development |  | The UID identifying the process. | `"218fc5a9-a5f1-4b54-aa05-46717d0ab26d"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cloudfoundry.process.type` | `string` | development |  | The type of process. | `"web"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cloudfoundry.space.id` | `string` | development |  | The guid of the CloudFoundry space the application is running in. | `"218fc5a9-a5f1-4b54-aa05-46717d0ab26d"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cloudfoundry.space.name` | `string` | development |  | The name of the CloudFoundry space the application is running in. | `"my-space-name"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cloudfoundry.system.id` | `string` | development |  | A guid or another name describing the event source. | `"cf/gorouter"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `cloudfoundry.system.instance.id` | `string` | development |  | A guid describing the concrete instance of the event source. | `"218fc5a9-a5f1-4b54-aa05-46717d0ab26d"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
