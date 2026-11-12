// Redundancy — an enum or union that lists one value twice.
//
// The compiler rejects two members with the same name. It accepts two members
// with the same value:
//
//   enum Status { ok: "ok", fine: "ok" }      // compiles clean
//   union Level { "low", "high", "low" }      // compiles clean
//   severity: "info" | "warn" | "info";       // compiles clean
//
// A duplicate enum value cannot be read back (which member is "ok"?) and
// becomes two C# enum members with one number. A duplicate union variant adds
// nothing and survives as a repeated schema entry.
//
// Enum members are compared by the value they are serialized as, through
// `resolveEncodedEnumMemberValue`. Since TypeSpec 1.18.0 `@encodedName` on an
// enum member sets that value, and the compiler reports its own collisions as
// `encoded-name-conflict` errors; this rule covers the plain values the
// compiler still accepts. It uses the `enumMember` navigation callback, which
// the 1.18.0 release notes list as fixed for linter rules.
//
// A discriminated union is skipped: there the variant name is the wire tag, and
// two tags may carry the same payload type on purpose.

import type { EnumMember, Type, Union } from "@typespec/compiler";
import {
  createRule,
  getDiscriminatedUnion,
  getLocationContext,
  getTypeName,
  paramMessage,
  resolveEncodedEnumMemberValue,
} from "@typespec/compiler";
import { SyntaxKind } from "@typespec/compiler/ast";
import type { UnionExpressionNode } from "@typespec/compiler/ast";
import { isContractType } from "../contract.js";

/** Literals compare by value; every other type compares by identity. */
function variantKey(type: Type): unknown {
  switch (type.kind) {
    case "String":
      return `string:${type.value}`;
    case "Number":
      return `number:${type.valueAsString}`;
    case "Boolean":
      return `boolean:${type.value}`;
    default:
      return type;
  }
}

export const duplicateMemberValueRule = createRule({
  name: "duplicate-member-value",
  severity: "warning",
  description: "An enum has no two members with one serialized value, and a union has no variant twice.",
  messages: {
    enumValue: paramMessage`Enum member '${"member"}' is serialized as ${"value"}, the value '${"first"}' already has. A reader cannot tell the two apart; give it its own value or remove it.`,
    unionVariant: paramMessage`Union variant ${"position"} repeats ${"value"}, which variant ${"first"} already lists. Remove the duplicate.`,
  },
  create(context) {
    const program = context.program;
    const seenUnions = new Set<unknown>();
    const serialized = (member: EnumMember): string => {
      const value = resolveEncodedEnumMemberValue(program, member, "application/json");
      return typeof value === "string" ? JSON.stringify(value) : String(value);
    };

    return {
      enumMember: (member: EnumMember) => {
        if (!isContractType(program, member.enum)) return;
        const value = serialized(member);
        for (const earlier of member.enum.members.values()) {
          if (earlier === member) return;
          if (serialized(earlier) !== value) continue;
          context.reportDiagnostic({
            messageId: "enumValue",
            target: member,
            format: { member: member.name, value, first: earlier.name },
          });
          return;
        }
      },

      union: (union: Union) => {
        // An instantiated template may repeat a variant only for one argument.
        if (union.templateMapper) return;
        if (union.name ? !isContractType(program, union) : getLocationContext(program, union).type !== "project") return;
        if (seenUnions.has(union.node ?? union)) return;
        seenUnions.add(union.node ?? union);
        if (getDiscriminatedUnion(program, union)[0]) return;

        const firstByKey = new Map<unknown, number>();
        let position = 0;
        for (const variant of union.variants.values()) {
          position++;
          const key = variantKey(variant.type);
          const first = firstByKey.get(key);
          if (first === undefined) {
            firstByKey.set(key, position);
            continue;
          }
          // A variant of a union expression (`"a" | "b"`) has no node of its own; point at its operand.
          const expression = union.node?.kind === SyntaxKind.UnionExpression ? (union.node as UnionExpressionNode) : undefined;
          context.reportDiagnostic({
            messageId: "unionVariant",
            target: variant.node ? variant : (expression?.options[position - 1] ?? union),
            format: { position: String(position), value: `'${getTypeName(variant.type)}'`, first: String(first) },
          });
        }
      },
    };
  },
});
