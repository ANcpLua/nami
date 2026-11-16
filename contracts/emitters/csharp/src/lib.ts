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
  // Every other decorator of lib/main.tsp is an `auto dec`: the compiler keeps its state (see decorators.ts).
  state: {
    catalogValue: { description: "Weaver catalog symbol supplying a projected attribute key" },
  },
} as const);

export const { reportDiagnostic, createDiagnostic, stateKeys } = $lib;
