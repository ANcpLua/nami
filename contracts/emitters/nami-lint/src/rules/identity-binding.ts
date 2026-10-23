// INV-1 and INV-2 — one identity, one type, one name, one key.
//
// The migration this rule guards existed because nothing tied an identity's
// scalar to the name it is spelled with. `SessionId` reached clients as
// `session.id`, `session_id`, and `sessionId`; `traceId` was declared as a bare
// `string` in errors.tsp and pagination.tsp, losing the 32-hex-character
// validation it has everywhere else. Three things are checked:
//
//   type -> name  a property carrying a shared identity scalar is the bare token
//                 (`traceId`) or a qualified edge ending in the scalar
//                 (`parentSpanId`, `previousSessionId`, `exampleTraceId`)
//   name -> type  a property using one of an identity's *exactly listed*
//                 reserved names is typed as that scalar, never a bare `string`
//   name -> key   an identity's wire key is exactly snake(name); unlike an
//                 ordinary property it may not be renamed on the wire
//
// Header properties are deliberately not skipped: `CollectorServerErrorResponse.traceId`
// is both a header and an identity, and the same header on the old ProblemDetails
// body was one of the bare-`string` defects.

import type { Model, ModelProperty } from "@typespec/compiler";
import {
  createRule,
  getTypeName,
  isArrayModelType,
  isKey,
  paramMessage,
  resolveEncodedName,
} from "@typespec/compiler";
import { isCookieParam, isHeader, isStatusCode } from "@typespec/http";
import {
  COMMON_NAMESPACE,
  PRODUCT_NAMESPACE,
  describeAllowedNames,
  identityOfProperty,
  isAllowedIdentityName,
  isInNamespace,
  reservedNameFor,
  snakeCase,
} from "../policy.js";

export const identityBindingRule = createRule({
  name: "identity-binding",
  severity: "warning",
  description:
    "A nami identity has one scalar, one property name, and one wire key everywhere it appears.",
  messages: {
    name: paramMessage`Property '${"property"}' carries the shared identity '${"scalar"}' but is named neither ${"allowed"}. One identity is spelled one way across the whole contract, so a generic correlation helper is possible and grep finds every use.`,
    type: paramMessage`Property '${"property"}' uses the name reserved for the '${"scalar"}' identity but is typed '${"actual"}'. Type it as '${"canonical"}' so it keeps that scalar's pattern and length validation instead of accepting any string.`,
    elementType: paramMessage`Property '${"property"}' uses the name reserved for the '${"scalar"}' identity but its elements are typed '${"actual"}'. Type it as '${"canonical"}[]'.`,
    rename: paramMessage`Identity property '${"property"}' is renamed on the wire to '${"wire"}'. An ordinary property may be renamed; an identity may not, because a second spelling is exactly the defect this contract removed. Use '${"expected"}'.`,
  },
  create(context) {
    const seen = new Set<unknown>();

    return {
      modelProperty: (property: ModelProperty) => {
        if (!isInNamespace(property, PRODUCT_NAMESPACE)) return;
        if (isStatusCode(context.program, property)) return;
        if (seen.has(property.node)) return;
        seen.add(property.node);

        const carried = identityOfProperty(property);
        const reserved = reservedNameFor(property.name);

        if (carried) {
          // type -> name. A resource's own key may be the bare `id` of its
          // model, which is the ordinary REST idiom and unambiguous in context.
          if (
            !isKey(context.program, property) &&
            !isAllowedIdentityName(carried.identity, carried.array, property.name)
          ) {
            context.reportDiagnostic({
              messageId: "name",
              target: property,
              format: {
                property: property.name,
                scalar: carried.identity.scalar,
                allowed: describeAllowedNames(carried.identity, carried.array),
              },
            });
          }

          // name -> key, checked only where a JSON key actually exists. A header
          // carries its HTTP field name, and a parameter declared inline on an
          // operation lives in an anonymous model and is named by @query/@path —
          // http-param-snake-case owns those. Asserting a JSON key for either
          // would be asserting something the wire never carries.
          const hasJsonKey =
            Boolean(property.model?.name) &&
            !isHeader(context.program, property) &&
            !isCookieParam(context.program, property);
          if (hasJsonKey) {
            const wire = resolveEncodedName(context.program, property, "application/json");
            const expected = snakeCase(property.name);
            if (wire !== expected) {
              context.reportDiagnostic({
                messageId: "rename",
                target: property,
                format: { property: property.name, wire, expected },
              });
            }
          }
        }

        // name -> type. Checked for headers too: CollectorServerErrorResponse.traceId
        // is a header whose value is still a TraceId and must be typed as one.
        if (reserved && !carried) {
          const isArray = property.type.kind === "Model" && isArrayModelType(property.type as Model);
          const actual = isArray
            ? getTypeName((property.type as Model).indexer!.value)
            : getTypeName(property.type);
          context.reportDiagnostic({
            messageId: reserved.array || isArray ? "elementType" : "type",
            target: property,
            format: {
              property: property.name,
              scalar: reserved.identity.scalar,
              canonical: `${COMMON_NAMESPACE}.${reserved.identity.scalar}`,
              actual,
            },
          });
        }
      },
    };
  },
});
