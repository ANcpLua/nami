import { createTypeSpecLibrary, defineTypeInfoProvider, getNamespaceFullName, paramMessage, resolveEncodedName } from "@typespec/compiler";

export const $lib = createTypeSpecLibrary({
  name: "@ancplua/nami-agent-control",
  diagnostics: {
    "artifact-leftover": { severity: "warning", messages: { default: paramMessage`Owned diagnostic artifact remains: ${"path"}.` } },
    "stale-plan": { severity: "error", messages: { default: "The source, artifact inventory or policy changed. Create a new plan." } },
    "invalid-state": { severity: "error", messages: { default: paramMessage`The verified policy refuses ${"event"} from ${"phase"}.` } },
    "unsafe-target": { severity: "error", messages: { default: "A requested target is outside declared artifact ownership." } },
    "validation-failed": { severity: "error", messages: { default: "Post-apply validation failed. The transaction was not verified." } },
    "setup-failed": { severity: "error", messages: { default: paramMessage`Tool setup failed: ${"detail"}.` } },
    "internal-error": { severity: "error", messages: { default: "The control library failed unexpectedly; no verified result is claimed." } },
  },
} as const);

export const { createDiagnostic, reportDiagnostic } = $lib;

// Read-only; invoked by TypeSpec/IDE consumers on demand, never during compilation.
export const $provideTypeInfo = defineTypeInfoProvider(({ program, target }) => {
  if (target.kind !== "ModelProperty") return undefined;
  const model = target.model;
  if (!model?.namespace) return undefined;
  const owner = getNamespaceFullName(model.namespace);
  if (owner !== "Nami.Api.Contracts" && !owner.startsWith("Nami.Api.Contracts.")) return undefined;
  return { content: `Nami contract: ${model.name}.${target.name}\n\nJSON key: \`${resolveEncodedName(program, target, "application/json")}\`\n\nRequired: ${!target.optional}` };
});
