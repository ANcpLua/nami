# `device` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `device.id` | `string` | development |  | A unique identifier representing the device | `"123456789012345"`, `"01:23:45:67:89:AB"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `device.manufacturer` | `string` | development |  | The name of the device manufacturer | `"Apple"`, `"Samsung"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `device.model.identifier` | `string` | development |  | The model identifier for the device | `"iPhone3,4"`, `"SM-G920F"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `device.model.name` | `string` | development |  | The marketing name for the device model | `"iPhone 6s Plus"`, `"Samsung Galaxy S6"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |

## Events

| Name | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `device.app.lifecycle` | development |  | This event represents an occurrence of a lifecycle transition on Android or iOS platform. |
