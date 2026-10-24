# `android` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `android.app.state` | `enum` | development |  | This attribute represents the state of the application. | `"created"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `android.os.api_level` | `string` | development |  | Uniquely identifies the framework API revision offered by a version (`os.version`) of the Android operating system. More information can be found in the [Android API levels documentation](https://developer.android.com/guide/topics/manifest/uses-sdk-element#ApiLevels). | `"33"`, `"32"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `android.state` | `enum` | development | Replaced by `android.app.state`. | Deprecated. Use `android.app.state` attribute instead. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `android.app.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `background` | development |  | Any time after Activity.onPause() or, if the app has no Activity, Context.stopService() has been called when the app was in the foreground state. |
| `created` | development |  | Any time before Activity.onResume() or, if the app has no Activity, Context.startService() has been called in the app for the first time. |
| `foreground` | development |  | Any time after Activity.onResume() or, if the app has no Activity, Context.startService() has been called when the app was in either the created or background states. |

### `android.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `background` | development |  | Any time after Activity.onPause() or, if the app has no Activity, Context.stopService() has been called when the app was in the foreground state. |
| `created` | development |  | Any time before Activity.onResume() or, if the app has no Activity, Context.startService() has been called in the app for the first time. |
| `foreground` | development |  | Any time after Activity.onResume() or, if the app has no Activity, Context.startService() has been called when the app was in either the created or background states. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
