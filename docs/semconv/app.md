# `app` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `app.build_id` | `string` | development |  | Unique identifier for a particular build or compilation of the application. | `"6cff0a7e-cefc-4668-96f5-1273d8b334d0"`, `"9f2b833506aa6973a92fde9733e6271f"`, `"my-app-1.0.0-code-123"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `app.crash.id` | `string` | development |  | A unique identifier representing an instance of an end user facing app crash. | `"083d3d2d-9a0e-47f8-be3d-bc3c5538ba38"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `app.installation.id` | `string` | development |  | A unique identifier representing the installation of an application on a specific device | `"2ab2916d-a51f-4ac8-80ee-45ac31a28092"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `app.jank.frame_count` | `int` | development |  | A number of frame renders that experienced jank. | `9`, `42` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `app.jank.period` | `double` | development |  | The time period, in seconds, for which this jank is being reported. | `1.0`, `5.0`, `10.24` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `app.jank.threshold` | `double` | development |  | The minimum rendering threshold for this jank, in seconds. | `0.016`, `0.7`, `1.024` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `app.screen.coordinate.x` | `int` | development |  | The x (horizontal) coordinate of a screen coordinate, in screen pixels. | `0`, `131` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `app.screen.coordinate.y` | `int` | development |  | The y (vertical) component of a screen coordinate, in screen pixels. | `12`, `99` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `app.screen.id` | `string` | development |  | An identifier that uniquely differentiates this screen from other screens in the same application. | `"f9bc787d-ff05-48ad-90e1-fca1d46130b3"`, `"com.example.app.MainActivity"`, `"com.example.shop.ProductDetailFragment"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `app.screen.name` | `string` | development |  | The name of an application screen. | `"MainActivity"`, `"ProductDetailFragment"`, `"ProfileView"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `app.widget.id` | `string` | development |  | An identifier that uniquely differentiates this widget from other widgets in the same application. | `"f9bc787d-ff05-48ad-90e1-fca1d46130b3"`, `"submit_order_1829"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `app.widget.name` | `string` | development |  | The name of an application widget. | `"submit"`, `"attack"`, `"Clear Cart"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
