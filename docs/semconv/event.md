# `event` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `event.name` | `string` | development | The value of this attribute MUST now be set as the value of the EventName field on the LogRecord to indicate that the LogRecord represents an Event. | Identifies the class / type of event. | `"browser.mouse.click"`, `"device.app.lifecycle"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
