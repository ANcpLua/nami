# `az` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `az.namespace` | `string` | development | Replaced by `azure.resource_provider.namespace`. | Deprecated, use `azure.resource_provider.namespace` instead. | `"Microsoft.Storage"`, `"Microsoft.KeyVault"`, `"Microsoft.ServiceBus"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `az.service_request_id` | `string` | development | Replaced by `azure.service.request.id`. | Deprecated, use `azure.service.request.id` instead. | `"00000000-0000-0000-0000-000000000000"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |

## Events

| Name | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `az.resource.log` | development | Replaced by `azure.resource.log`. | Deprecated. Use `azure.resource.log` instead. |
