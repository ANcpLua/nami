# `nodejs` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `nodejs.eventloop.state` | `enum` | development |  | The state of event loop time. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `nodejs.eventloop.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `active` | development |  | Active time. |
| `idle` | development |  | Idle time. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `nodejs.eventloop.delay.max` | gauge | `s` | development |  | Event loop maximum delay. |
| `nodejs.eventloop.delay.mean` | gauge | `s` | development |  | Event loop mean delay. |
| `nodejs.eventloop.delay.min` | gauge | `s` | development |  | Event loop minimum delay. |
| `nodejs.eventloop.delay.p50` | gauge | `s` | development |  | Event loop 50 percentile delay. |
| `nodejs.eventloop.delay.p90` | gauge | `s` | development |  | Event loop 90 percentile delay. |
| `nodejs.eventloop.delay.p99` | gauge | `s` | development |  | Event loop 99 percentile delay. |
| `nodejs.eventloop.delay.stddev` | gauge | `s` | development |  | Event loop standard deviation delay. |
| `nodejs.eventloop.time` | counter | `s` | development |  | Cumulative duration of time the event loop has been in each state. |
| `nodejs.eventloop.utilization` | gauge | `1` | development |  | Event loop utilization. |
