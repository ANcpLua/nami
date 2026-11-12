// Duplication — two properties of one model under one JSON key.
//
// The compiler reports `encoded-name-conflict` when two properties declared or
// spread into the same model share an encoded name. It does not look across
// `extends`: a derived model may give a property the wire name an inherited
// property already ships, and the program compiles clean (checked against
// @typespec/compiler 1.18.0). Every tool input and result in this contract
// extends `ToolReadInput` or `ToolResult`, so that is the case that matters
// here. The outcome is an object schema with one key described twice and a
// generated C# type with two properties bound to the same JSON name.
//
// This rule covers exactly the gap and nothing the compiler already reports:
// it fires where a model's own property collides with an inherited one.

import type { Model, ModelProperty } from "@typespec/compiler";
import { createRule, paramMessage, resolveEncodedName, walkPropertiesInherited } from "@typespec/compiler";
import { isCookieParam, isHeader, isStatusCode } from "@typespec/http";
import { fullName, isContractType } from "../contract.js";

export const duplicateWireNameRule = createRule({
  name: "duplicate-wire-name",
  severity: "warning",
  description: "No property gives itself the JSON wire name that an inherited property of the same model already ships.",
  messages: {
    default: paramMessage`Property '${"property"}' ships the JSON key '${"wire"}', which '${"inherited"}' inherited from '${"base"}' already ships. One object cannot carry one key twice: rename one of them on the wire, or drop the duplicate.`,
  },
  create(context) {
    const seen = new Set<unknown>();

    return {
      model: (model: Model) => {
        const program = context.program;
        // Without a base every collision is inside one model, which the compiler reports itself.
        if (!model.name || !model.baseModel || !isContractType(program, model)) return;
        if (seen.has(model.node)) return;
        seen.add(model.node);

        // Own properties come first, then each base in turn; an override hides the base property.
        const byWire = new Map<string, ModelProperty>();
        for (const property of walkPropertiesInherited(model)) {
          // Headers, cookies and status codes never become JSON keys.
          if (isHeader(program, property) || isCookieParam(program, property) || isStatusCode(program, property)) continue;
          const wire = resolveEncodedName(program, property, "application/json");
          const first = byWire.get(wire);
          if (!first) {
            byWire.set(wire, property);
            continue;
          }
          // Reported once, by the model whose own property introduces the collision.
          if (first.model !== model || property.model === model) continue;
          context.reportDiagnostic({
            target: first,
            format: {
              property: first.name,
              wire,
              inherited: property.name,
              base: property.model ? fullName(property.model) : "a base model",
            },
          });
        }
      },
    };
  },
});
