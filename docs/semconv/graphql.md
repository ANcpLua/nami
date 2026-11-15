# `graphql` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `graphql.document` | `string` | development |  | The GraphQL document being executed. | `"query findBookById { bookById(id: ?) { name } }"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `graphql.operation.name` | `string` | development |  | The name of the operation being executed. | `"findBookById"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |
| `graphql.operation.type` | `enum` | development |  | The type of the operation being executed. | `"query"`, `"mutation"`, `"subscription"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.45.0"` |

### `graphql.operation.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `mutation` | development |  | GraphQL mutation |
| `query` | development |  | GraphQL query |
| `subscription` | development |  | GraphQL subscription |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |

## Spans

| Type | Kind | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- |
| `graphql.server` | server | development |  | This span represents an incoming operation on a GraphQL server implementation. |
