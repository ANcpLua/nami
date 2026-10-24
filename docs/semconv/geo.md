# `geo` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `geo.continent.code` | `enum` | development |  | Two-letter code representing continent’s name. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `geo.country.iso_code` | `string` | development |  | Two-letter ISO Country Code ([ISO 3166-1 alpha2](https://wikipedia.org/wiki/ISO_3166-1#Codes)). | `"CA"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `geo.locality.name` | `string` | development |  | Locality name. Represents the name of a city, town, village, or similar populated place. | `"Montreal"`, `"Berlin"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `geo.location.lat` | `double` | development |  | Latitude of the geo location in [WGS84](https://wikipedia.org/wiki/World_Geodetic_System#WGS84). | `45.505918` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `geo.location.lon` | `double` | development |  | Longitude of the geo location in [WGS84](https://wikipedia.org/wiki/World_Geodetic_System#WGS84). | `-73.61483` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `geo.postal_code` | `string` | development |  | Postal code associated with the location. Values appropriate for this field may also be known as a postcode or ZIP code and will vary widely from country to country. | `"94040"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `geo.region.iso_code` | `string` | development |  | Region ISO code ([ISO 3166-2](https://wikipedia.org/wiki/ISO_3166-2)). | `"CA-QC"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `geo.continent.code` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `AF` | development |  | Africa |
| `AN` | development |  | Antarctica |
| `AS` | development |  | Asia |
| `EU` | development |  | Europe |
| `NA` | development |  | North America |
| `OC` | development |  | Oceania |
| `SA` | development |  | South America |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
