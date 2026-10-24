# `url` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `url.domain` | `string` | development |  | Domain extracted from the `url.full`, such as "opentelemetry.io". | `"www.foo.bar"`, `"opentelemetry.io"`, `"3.12.167.2"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `url.extension` | `string` | development |  | The file extension extracted from the `url.full`, excluding the leading dot. | `"png"`, `"gz"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `url.fragment` | `string` | stable |  | The [URI fragment](https://www.rfc-editor.org/rfc/rfc3986#section-3.5) component | `"SemConv"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `url.full` | `string` | stable |  | Absolute URL describing a network resource according to [RFC3986](https://www.rfc-editor.org/rfc/rfc3986) | `"https://www.foo.bar/search?q=OpenTelemetry#SemConv"`, `"//localhost"` | `nami.persisted`: `false`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `false` |
| `url.original` | `string` | development |  | Unmodified original URL as seen in the event source. | `"https://www.foo.bar/search?q=OpenTelemetry#SemConv"`, `"search?q=OpenTelemetry"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `url.path` | `string` | stable |  | The [URI path](https://www.rfc-editor.org/rfc/rfc3986#section-3.3) component | `"/search"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `url.port` | `int` | development |  | Port extracted from the `url.full` | `443` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `url.query` | `string` | stable |  | The [URI query](https://www.rfc-editor.org/rfc/rfc3986#section-3.4) component | `"q=OpenTelemetry"` | `nami.persisted`: `false`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `false` |
| `url.registered_domain` | `string` | development |  | The highest registered URL domain, stripped of the subdomain. | `"example.com"`, `"foo.co.uk"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `url.scheme` | `string` | stable |  | The [URI scheme](https://www.rfc-editor.org/rfc/rfc3986#section-3.1) component identifying the used protocol. | `"https"`, `"ftp"`, `"telnet"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `url.subdomain` | `string` | development |  | The subdomain portion of a fully qualified domain name includes all of the names except the hostname under the registered_domain. In a partially qualified domain, or if the qualification level of the full name cannot be determined, subdomain contains all of the names below the registered domain. | `"east"`, `"sub2.sub1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `url.template` | `string` | development |  | The low-cardinality template of an [absolute path reference](https://www.rfc-editor.org/rfc/rfc3986#section-4.2). | `"/users/{id}"`, `"/users/:id"`, `"/users?id={id}"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `url.top_level_domain` | `string` | development |  | The effective top level domain (eTLD), also known as the domain suffix, is the last part of the domain name. For example, the top level domain for example.com is `com`. | `"com"`, `"co.uk"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
