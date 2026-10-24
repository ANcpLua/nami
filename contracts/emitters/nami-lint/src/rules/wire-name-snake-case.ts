// INV-3 — one casing convention for every JSON body property in the contract.
//
// The contract used to run two regimes side by side: OTel-projection models
// carried explicit snake/dotted `@encodedName`, everything else shipped its raw
// camelCase TypeSpec name. Identity scalars lived in both, so the same value
// reached clients under two or three different keys. This rule abolishes the
// camelCase-passthrough regime: a body property either declares a snake_case
// wire name or already is one.

import type { ModelProperty } from "@typespec/compiler";
import { createAddDecoratorCodeFix, createRule, paramMessage, resolveEncodedName } from "@typespec/compiler";
import { getHeaderFieldName, isCookieParam, isHeader, isStatusCode } from "@typespec/http";
import { PRODUCT_NAMESPACE, SNAKE_CASE, expectedWireName, isInNamespace } from "../policy.js";

export const wireNameFixId = "nami-add-wire-name";

export const wireNameSnakeCaseRule = createRule({
  name: "wire-name-snake-case",
  severity: "warning",
  description:
    "Every JSON body property in the nami product contract ships a snake_case wire name.",
  messages: {
    dotted: paramMessage`Property '${"property"}' ships the dotted wire name '${"wire"}'. Dotted semantic-convention keys belong to the OTLP ingestion boundary, which this contract does not own; a product DTO uses '${"expected"}'. Dots also read as nesting to jq, JSONPath, and TypeScript member access.`,
    camel: paramMessage`Property '${"property"}' ships the camelCase wire name '${"wire"}'. Add @encodedName("application/json", "${"expected"}") — the product contract has one casing convention and no property may fall back to its TypeSpec name.`,
    default: paramMessage`Property '${"property"}' ships the wire name '${"wire"}', which is not snake_case. Use '${"expected"}'.`,
  },
  create(context) {
    const seen = new Set<unknown>();

    return {
      modelProperty: (property: ModelProperty) => {
        if (!isInNamespace(property, PRODUCT_NAMESPACE)) return;
        if (seen.has(property.node)) return;
        seen.add(property.node);

        // Only a named model becomes a schema. A parameter declared inline on an
        // operation lives in an anonymous model and has no JSON representation
        // at all — its wire name comes from @query/@path, which is checked by
        // http-param-snake-case. Requiring an @encodedName there would add a
        // decorator that never affects anything.
        if (!property.model?.name) return;

        // A header's wire name is its HTTP field name, which follows HTTP's own
        // conventions (`X-Trace-Id`, `Last-Event-ID`) and is deliberately not
        // snake_case. Status codes and cookies likewise never reach the body.
        // Query and path properties are NOT skipped: a named model of query or
        // path parameters is also emitted as a standalone schema, where the JSON
        // name is what ships, so it must agree with the parameter name that
        // http-param-snake-case sets.
        const program = context.program;
        if (isStatusCode(program, property) || isCookieParam(program, property)) return;
        if (isHeader(program, property)) {
          // Resolved rather than assumed, so the skip is a decision about a
          // known name instead of a hole in the rule's coverage.
          if (getHeaderFieldName(program, property) !== undefined) return;
        }

        const wire = resolveEncodedName(program, property, "application/json");
        const expected = expectedWireName(property.model.name, property.name);
        if (wire === expected) return;

        const messageId = wire.includes(".")
          ? "dotted"
          : wire === property.name && !SNAKE_CASE.test(wire) ? "camel" : "default";
        context.reportDiagnostic({
          messageId,
          target: property,
          format: { property: property.name, wire, expected },
          codefixes: wire === property.name && !property.decorators.some(item => item.decorator.name === "$encodedName")
            ? [{ ...createAddDecoratorCodeFix(property, "encodedName", ['"application/json"', JSON.stringify(expected)]),
                id: wireNameFixId, label: `Declare JSON wire name '${expected}'` }]
            : undefined,
        });
      },
    };
  },
});
