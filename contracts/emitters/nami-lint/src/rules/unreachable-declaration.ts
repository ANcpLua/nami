// Redundancy — a declaration nothing can consume.
//
// A declaration is reached when something outside the TypeSpec sources consumes
// it, directly or through another declaration:
//
//   - a tool, a wire root, a route, a storage row or a projection names it
//   - it sits in a namespace with an output profile (`@csharpOutput`): that
//     whole namespace is written to one generated file for hand-written code
//   - an in-repo emitter generates a type from it for a project outside
//     `contracts/`: every model, enum and union of a namespace mapped with
//     `@csharpNamespace`, and a scalar with a brand decorator
//
// The last root is what keeps the rule honest. The TypeSpec program cannot see
// hand-written C# or TypeScript, so it cannot know whether a generated type is
// used. It is: at 48bdb08 no tool reaches `OTel.Metrics.MetricPointData` or
// `OTel.Metrics.MetricNumberValue`, and test code uses both
// (`tests/Nami.UnitTests/ContractTests.cs:58` and `:168`,
// `tests/Nami.IntegrationTests/UnionCompilerTests.cs:31` and `:32`,
// `tests/Nami.IntegrationTests/MetricIdentityTests.cs:6`). A rule that called
// them dead would recommend a deletion that breaks the build.
//
// What the rule reports by default is therefore a declaration that nothing in
// the contract references and from which no type is generated. In this contract
// that is an unbranded scalar: C# and TypeScript use its base type, and only a
// schema entry carries its name. At 48bdb08 there were 23: 22 in
// `common/types.tsp` and `Values.InstrumentId`.
//
// Reachability is generous on purpose. A spread source, an `is` source, a
// template and its arguments, a base, a model derived from a used
// `@discriminator` base and a type named by any decorator all count as uses.
// The identity scalars of `policy.ts` are roots too: the identity rules reserve
// property names for them whether or not a property uses one today.
//
// Only the top of an unreached subgraph is reported; what goes with it is named
// in its message.
//
// For an audit, the generated types can be taken out of the roots for one run:
//
//   tsp compile main.tsp --no-emit --linter-rule \
//     '@ancplua/typespec-nami-lint/unreachable-declaration={"generatedTypes":"report"}'
//
// That also lists every declaration no tool, wire root, route, storage row or
// projection reaches although a type is generated from it, with a message that
// says to look for a hand-written use first. It is a list to work through with
// the consumers at hand, never the gate.

import { createRule, paramMessage } from "@typespec/compiler";
import type { Declaration } from "../contract.js";
import { compareText, contractGraph, fullName } from "../contract.js";

const LISTED_DEPENDENTS = 6;

export const unreachableDeclarationRule = createRule({
  name: "unreachable-declaration",
  severity: "warning",
  description:
    "Every contract declaration is used by another declaration, a tool, wire root, route, storage row or projection, or has a generated type that code outside the contract can use.",
  optionSchema: {
    type: "object",
    additionalProperties: false,
    properties: {
      generatedTypes: { type: "string", enum: ["reached", "report"] },
    },
  },
  defaultOptions: { generatedTypes: "reached" as "reached" | "report" },
  messages: {
    default: paramMessage`${"kind"} '${"name"}' is used by no declaration, tool, wire root, route, storage row or projection, and no emitter generates a type from it; at most a schema entry carries its name. Remove it unless code looks that entry up by name, or reference it from the declaration that needs it.${"dependents"}`,
    generated: paramMessage`${"kind"} '${"name"}' is not reachable from any tool, wire root, route, storage row or projection. The emitters still generate a type from it for code outside contracts/, so it is unused only if no hand-written code uses that type: look for such a use before removing it.${"dependents"}`,
  },
  create(context) {
    return {
      exit: () => {
        const graph = contractGraph(context.program);
        const generatedTypes = context.options.generatedTypes !== "report";
        const dead = graph.declarations.filter((declaration) => !graph.isReachable(declaration, generatedTypes));
        const deadSet = new Set<Declaration>(dead);
        const deadDependencies = new Map<Declaration, Set<Declaration>>();
        for (const declaration of dead) {
          deadDependencies.set(
            declaration,
            new Set(graph.dependencies(declaration).filter((dependency) => deadSet.has(dependency))),
          );
        }

        // An unreached declaration that another unreached declaration uses is not
        // reported on its own. Inside a cycle the first name in sort order speaks for it.
        const hangsOffAnother = (declaration: Declaration): boolean => {
          for (const other of dead) {
            if (other === declaration || !deadDependencies.get(other)!.has(declaration)) continue;
            if (!deadDependencies.get(declaration)!.has(other)) return true;
            if (compareText(fullName(other), fullName(declaration)) < 0) return true;
          }
          return false;
        };

        for (const declaration of dead) {
          if (hangsOffAnother(declaration)) continue;
          const names = [...deadDependencies.get(declaration)!].map((dependent) => `'${dependent.name}'`);
          const listed = names.slice(0, LISTED_DEPENDENTS).join(", ");
          const more = names.length - LISTED_DEPENDENTS;
          const dependents =
            names.length === 0
              ? ""
              : ` Only unreached declarations use ${more > 0 ? `${listed} and ${more} more` : listed}, so ${names.length === 1 ? "it goes" : "they go"} with it.`;
          context.reportDiagnostic({
            messageId: graph.hasGeneratedType(declaration) ? "generated" : "default",
            target: declaration,
            format: { kind: declaration.kind, name: fullName(declaration), dependents },
          });
        }
      },
    };
  },
});
