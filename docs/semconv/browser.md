# `browser` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `browser.brands` | `string[]` | development |  | Array of brand name and version separated by a space | `[" Not A;Brand 99","Chromium 99","Chrome 99"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `browser.document.url.full` | `string` | development |  | Absolute URL of the current browser document according to [RFC3986](https://www.rfc-editor.org/rfc/rfc3986). | `"https://www.example.com/search?q=OpenTelemetry#SemConv"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `browser.language` | `string` | development |  | Preferred language of the user using the browser | `"en"`, `"en-US"`, `"fr"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `browser.mobile` | `boolean` | development |  | A boolean that is true if the browser is running on a mobile device |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `browser.platform` | `string` | development |  | The platform on which the browser is running | `"Windows"`, `"macOS"`, `"Android"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `browser.web_vital.delta` | `double` | development |  | The delta between the current value and the last-reported value. See [delta](https://github.com/GoogleChrome/web-vitals?tab=readme-ov-file#report-only-the-delta-of-changes). | `0.2` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `browser.web_vital.id` | `string` | development |  | A unique ID representing this particular metric instance. | `"v3-1677874579383-6381583661209"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `browser.web_vital.name` | `enum` | development |  | Name of the web vital. | `"cls"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `browser.web_vital.navigation_type` | `enum` | development |  | The type of navigation, as reported by the [Navigation Timing API](https://developer.mozilla.org/docs/Web/API/PerformanceNavigationTiming/type), with additional values reported by the web-vitals library. | `"navigate"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `browser.web_vital.rating` | `enum` | development |  | The rating of the web vital value against the "good", "needs improvement", and "poor" thresholds defined for the metric. | `"good"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `browser.web_vital.value` | `double` | development |  | Value of the web vital. | `1.0` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `browser.web_vital.name` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `cls` | development |  | Cumulative Layout Shift. See [cls](https://web.dev/articles/cls). |
| `fcp` | development |  | First Contentful Paint. See [fcp](https://web.dev/articles/fcp). |
| `fid` | development | Replaced by Interaction to Next Paint (`inp`), which became a Core Web Vital in March 2024. See [inp](https://web.dev/articles/inp). | First Input Delay. See [fid](https://web.dev/articles/fid). |
| `inp` | development |  | Interaction to Next Paint. See [inp](https://web.dev/articles/inp). |
| `lcp` | development |  | Largest Contentful Paint. See [lcp](https://web.dev/articles/lcp). |
| `ttfb` | development |  | Time to First Byte. See [ttfb](https://web.dev/articles/ttfb). |

### `browser.web_vital.navigation_type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `back-forward` | development |  | Navigation through the browser's history traversal (e.g. back/forward buttons). |
| `back-forward-cache` | development |  | Navigation restoring a page from the back/forward cache (bfcache). |
| `navigate` | development |  | Navigation started by clicking a link, entering a URL, form submission, or a script operation. |
| `prerender` | development |  | Navigation to a page that was prerendered. |
| `reload` | development |  | Navigation through a reload operation or a `Location.reload()` call. |
| `restore` | development |  | Navigation restoring a page that was previously discarded by the browser. |

### `browser.web_vital.rating` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `good` | development |  | The metric value is within the "good" threshold. |
| `needs-improvement` | development |  | The metric value is within the "needs improvement" threshold. |
| `poor` | development |  | The metric value is within the "poor" threshold. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
