import { createTypeSpecLibrary, paramMessage } from "@typespec/compiler";

export const $lib = createTypeSpecLibrary({
  name: "@ancplua/typespec-emit-csharp",
  diagnostics: {
    "unmapped-type": {
      severity: "error",
      messages: {
        default: paramMessage`ANcpLua-EMIT-001: unmapped type '${"name"}' — add a mapping or decorate the model with @csharpNamespace`,
      },
    },
    "missing-namespace": {
      severity: "error",
      messages: {
        default: paramMessage`ANcpLua-EMIT-002: model '${"name"}' has no @csharpNamespace (inherited or direct)`,
      },
    },
  },
  state: {
    enumType: { description: "Underlying CLR type of a generated enum" },
    attributeIngestionProjection: { description: "Typed-slot to stored-value projection" },
    attributeProjection: { description: "Recursive stored-to-API value projection" },
    environment: { description: "Environment name, independent from the JSON property name" },
    projection: { description: "Structural ingestion to storage mapping" },
    projectionUsing: { description: "Named representation conversion" },
    slot: { description: "Typed slot representation of a domain value union" },
    tool: { description: "Validated tool input, result and error association" },
    wireRoot: { description: "Agreed wire revision root" },
    output: { description: "Explicit consumer project, namespace and generated file" },
    form: { description: "Consumer C# declaration form" },
    propertyType: { description: "Explicit consumer CLR property representation" },
    table: { description: "DuckDB table metadata" },
    column: { description: "DuckDB column metadata and stable order" },
    csharpNamespace: { description: "C# namespace override on a model/namespace/enum" },
    csharpBrand: { description: "Emit the target string scalar as a distinct C# value type" },
    csharpPolymorphic: { description: "Emit a model union as a strongly typed C# polymorphic contract" },
  },
} as const);

export const { reportDiagnostic, createDiagnostic, stateKeys } = $lib;
