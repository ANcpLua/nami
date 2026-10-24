# `oracle` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `oracle.db.domain` | `string` | release_candidate |  | The database domain associated with the connection. | `"example.com"`, `"corp.internal"`, `"prod.db.local"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `oracle.db.instance.name` | `string` | release_candidate |  | The instance name associated with the connection in an Oracle Real Application Clusters environment. | `"ORCL1"`, `"ORCL2"`, `"ORCL3"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `oracle.db.name` | `string` | release_candidate |  | The database name associated with the connection. | `"ORCL1"`, `"FREE"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `oracle.db.pdb` | `string` | release_candidate |  | The pluggable database (PDB) name associated with the connection. | `"PDB1"`, `"FREEPDB"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `oracle.db.service` | `string` | release_candidate |  | The service name currently associated with the database connection. | `"order-processing-service"`, `"db_low.adb.oraclecloud.com"`, `"db_high.adb.oraclecloud.com"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
