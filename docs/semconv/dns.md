# `dns` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `dns.answers` | `string[]` | development |  | The list of IPv4 or IPv6 addresses resolved during DNS lookup. | `["10.0.0.1","2001:0db8:85a3:0000:0000:8a2e:0370:7334"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `dns.question.name` | `string` | development |  | The name being queried. | `"www.example.com"`, `"opentelemetry.io"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `dns.lookup.duration` | histogram | `s` | development |  | Measures the time taken to perform a DNS lookup. |
