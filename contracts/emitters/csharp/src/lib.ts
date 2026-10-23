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
    csharpNamespace: { description: "C# namespace override on a model/namespace/enum" },
    csharpBrand: { description: "Emit the target string scalar as a distinct C# value type" },
    csharpPolymorphic: { description: "Emit a model union as a strongly typed C# polymorphic contract" },
  },
} as const);

export const { reportDiagnostic, createDiagnostic, stateKeys } = $lib;
