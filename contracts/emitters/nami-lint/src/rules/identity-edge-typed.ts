// INV-2, for the half of the identity table `identity-binding` cannot reach.
//
// `identity-binding` checks the exactly-listed `reserved` names and pins each to
// one scalar. Most edge names are not reserved, and cannot be: `ciRunId` is a
// `SessionId` on the ci_log shapes, and a run of another kind may carry its own
// scalar. Reserving it for one would reject the other.
//
// So `ciRunId: string` was legal — the name is registered as an identity edge, the
// reserved lookup misses it, and nothing else looks at the type. That is a run
// identity with no `@minLength`/`@maxLength`, which is the unvalidated bare
// `string` the identity convention exists to remove; per the contract's own
// rationale a malformed run id then yields an empty result rather than an error.
//
// This rule asserts the one thing that holds across every edge regardless of
// which identity it points at: the property is typed as a scalar this contract
// declares, never a built-in. Choosing *which* declared scalar stays a modelling
// decision.

import type { Model, ModelProperty } from "@typespec/compiler";
import { createRule, getTypeName, isArrayModelType, paramMessage } from "@typespec/compiler";
import { PRODUCT_NAMESPACE, edgeNameFor, isDeclaredScalar, isInNamespace } from "../policy.js";

export const identityEdgeTypedRule = createRule({
  name: "identity-edge-typed",
  severity: "warning",
  description:
    "A property named after a registered identity edge is typed as a declared scalar, never a built-in.",
  messages: {
    default: paramMessage`Property '${"property"}' is a registered edge of the '${"scalar"}' identity but is typed '${"actual"}'. Type it as the declared scalar for the identity it points at, so it keeps that scalar's pattern and length validation instead of accepting any string.`,
  },
  create(context) {
    const seen = new Set<unknown>();

    return {
      modelProperty: (property: ModelProperty) => {
        if (!isInNamespace(property, PRODUCT_NAMESPACE)) return;
        if (seen.has(property.node)) return;
        seen.add(property.node);

        const identity = edgeNameFor(property.name);
        if (!identity) return;

        // One level of array, matching how an identity is resolved elsewhere.
        const type = property.type;
        const element =
          type.kind === "Model" && isArrayModelType(type as Model)
            ? (type as Model).indexer!.value
            : type;
        if (isDeclaredScalar(element)) return;

        context.reportDiagnostic({
          target: property,
          format: {
            property: property.name,
            scalar: identity.scalar,
            actual: getTypeName(element),
          },
        });
      },
    };
  },
});
