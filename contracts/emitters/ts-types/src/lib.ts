import { createTypeSpecLibrary, paramMessage } from "@typespec/compiler";

export const $lib = createTypeSpecLibrary({
  name: "@ancplua/typespec-emit-ts-types",
  diagnostics: {
    "unmapped-type": {
      severity: "error",
      messages: {
        default: paramMessage`ANcpLua-TSTY-001: unmapped type '${"name"}'`,
      },
    },
  },
} as const);

export const { reportDiagnostic } = $lib;
