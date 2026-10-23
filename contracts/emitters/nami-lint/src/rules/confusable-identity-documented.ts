// INV-5 — a scalar that reads like a shared identity states its relationship.
//
// A scalar such as `RunnerSessionId` beside `Common.SessionId` would be a
// deliberately different type: a different lifecycle and a different owner.
// Unless that is recorded, the distinction survives only as long as everyone
// remembers it — and a future author could "helpfully unify" them, or try to
// join on them, with nothing to push back.
//
// This rule requires the confusable scalar's own doc comment to name the type
// it is not, which turns the intent into something the compiler checks.

import type { Scalar } from "@typespec/compiler";
import { createRule, getDoc, paramMessage } from "@typespec/compiler";
import {
  PRODUCT_NAMESPACE,
  confusableIdentityFor,
  isInNamespace,
  relationshipPhrase,
} from "../policy.js";

export const confusableIdentityDocumentedRule = createRule({
  name: "confusable-identity-documented",
  severity: "warning",
  description:
    "A scalar whose name ends with a shared nami identity documents its relationship to that identity.",
  messages: {
    default: paramMessage`Scalar '${"scalar"}' reads as a variant of the shared identity '${"identity"}' but never says how the two relate. Its @doc must name '${"phrase"}' and state whether the two are joinable — otherwise the distinction is only a convention, and the next author is free to merge them.`,
  },
  create(context) {
    const seen = new Set<string>();

    return {
      scalar: (scalar: Scalar) => {
        if (!isInNamespace(scalar, PRODUCT_NAMESPACE)) return;
        if (seen.has(scalar.name)) return;
        seen.add(scalar.name);

        const identity = confusableIdentityFor(scalar);
        if (!identity) return;

        const phrase = relationshipPhrase(identity);
        const doc = getDoc(context.program, scalar) ?? "";
        if (doc.includes(phrase)) return;

        context.reportDiagnostic({
          target: scalar,
          format: { scalar: scalar.name, identity: identity.scalar, phrase },
        });
      },
    };
  },
});
