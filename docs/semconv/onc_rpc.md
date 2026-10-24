# `onc_rpc` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `onc_rpc.procedure.name` | `string` | development |  | ONC/Sun RPC procedure name. | `"OPEN"`, `"READ"`, `"GETATTR"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `onc_rpc.procedure.number` | `int` | development |  | ONC/Sun RPC procedure number. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `onc_rpc.program.name` | `string` | development |  | ONC/Sun RPC program name. | `"portmapper"`, `"nfs"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `onc_rpc.version` | `int` | development |  | ONC/Sun RPC program version. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
