// INV-4 — query and path parameters use the same single casing as bodies.
//
// `@encodedName("application/json", …)` does nothing to a query parameter:
// @typespec/http reads the name from `@query`/`@path` and never consults the
// JSON encoded name. The contract previously carried such decorators on
// StreamParams, so the source claimed `trace_id` while the wire shipped
// `traceId`. Parameters are renamed at the decorator that actually owns them,
// and this rule keeps them aligned with the body convention.

import type { ModelProperty, Namespace, Operation } from "@typespec/compiler";
import { createRule, paramMessage } from "@typespec/compiler";
import { getPathParamName, getQueryParamName } from "@typespec/http";
import { PRODUCT_NAMESPACE, isInNamespace, snakeCase } from "../policy.js";

export const httpParamSnakeCaseRule = createRule({
  name: "http-param-snake-case",
  severity: "warning",
  description:
    "Every query and path parameter in the nami product contract uses the single snake_case convention.",
  messages: {
    default: paramMessage`${"kind"} parameter '${"property"}' ships as '${"wire"}'. Rename it at the decorator that owns the wire name: @${"decorator"}("${"expected"}").`,
  },
  create(context) {
    function owningNamespace(operation: Operation): Namespace | undefined {
      return operation.interface?.namespace ?? operation.namespace;
    }

    function check(property: ModelProperty, kind: "Query" | "Path", wire: string): void {
      const expected = snakeCase(property.name);
      if (wire === expected) return;
      context.reportDiagnostic({
        target: property,
        format: {
          kind,
          property: property.name,
          wire,
          expected,
          decorator: kind.toLowerCase(),
        },
      });
    }

    const seen = new Set<unknown>();

    function checkProperty(property: ModelProperty): void {
      if (seen.has(property.node)) return;
      seen.add(property.node);

      const query = getQueryParamName(context.program, property);
      if (query !== undefined) check(property, "Query", query);

      const path = getPathParamName(context.program, property);
      if (path !== undefined) check(property, "Path", path);
    }

    return {
      // Parameters declared inline on an operation. Their anonymous parameter
      // model has no namespace, so scope comes from the operation's container.
      operation: (operation: Operation) => {
        const namespace = owningNamespace(operation);
        if (!namespace || !isInNamespace(namespace, PRODUCT_NAMESPACE)) return;
        for (const property of operation.parameters.properties.values()) {
          checkProperty(property);
        }
      },

      // Parameters declared on a named model that operations spread.
      modelProperty: (property: ModelProperty) => {
        if (!isInNamespace(property, PRODUCT_NAMESPACE)) return;
        checkProperty(property);
      },
    };
  },
});
