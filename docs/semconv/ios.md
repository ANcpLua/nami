# `ios` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `ios.app.state` | `enum` | development |  | This attribute represents the state of the application. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `ios.state` | `enum` | development | Replaced by `ios.app.state`. | Deprecated. Use the `ios.app.state` attribute. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `ios.app.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `active` | development |  | The app has become `active`. Associated with UIKit notification `applicationDidBecomeActive`. |
| `background` | development |  | The app is now in the background. This value is associated with UIKit notification `applicationDidEnterBackground`. |
| `foreground` | development |  | The app is now in the foreground. This value is associated with UIKit notification `applicationWillEnterForeground`. |
| `inactive` | development |  | The app is now `inactive`. Associated with UIKit notification `applicationWillResignActive`. |
| `terminate` | development |  | The app is about to terminate. Associated with UIKit notification `applicationWillTerminate`. |

### `ios.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `active` | development |  | The app has become `active`. Associated with UIKit notification `applicationDidBecomeActive`. |
| `background` | development |  | The app is now in the background. This value is associated with UIKit notification `applicationDidEnterBackground`. |
| `foreground` | development |  | The app is now in the foreground. This value is associated with UIKit notification `applicationWillEnterForeground`. |
| `inactive` | development |  | The app is now `inactive`. Associated with UIKit notification `applicationWillResignActive`. |
| `terminate` | development |  | The app is about to terminate. Associated with UIKit notification `applicationWillTerminate`. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
