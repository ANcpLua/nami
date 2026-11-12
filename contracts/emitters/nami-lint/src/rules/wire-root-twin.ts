// Duplication — one message, two wire roots (cleanup ledger C05).
//
// `emit-config.tsp` declares both `EngineRequest` and `EngineCommand` as wire
// roots. `EngineRequest` is one flat model with `method: "call" | "health" |
// "cancel"` and every other field optional; `EngineCommand` is the discriminated
// union of `EngineCall`, `EngineHealth` and `EngineCancel` over the same
// `method`. They are the same message written twice. Both enter the contract
// revision, each side of the engine protocol can read a different one, and a
// field added to one is silently missing from the other.
//
// The test is structural and exact. A wire-root model M is the flat twin of a
// wire-root discriminated union U when
//
//   - M has a property named like U's discriminator whose type is exactly the
//     set of U's variant tags, and
//   - every variant's wire names are among M's, and together they are all of M's.
//
// Nothing is inferred from names, and a model that merely overlaps a union is
// not reported.

import type { Model, Program, Type, Union } from "@typespec/compiler";
import { createRule, getDiscriminatedUnion, paramMessage, resolveEncodedName, walkPropertiesInherited } from "@typespec/compiler";
import { compareText, contractGraph, fullName } from "../contract.js";

/** The string values a property type admits when it is a closed set of string literals. */
function literalValues(type: Type): Set<string> | undefined {
  if (type.kind === "String") return new Set([type.value]);
  if (type.kind !== "Union") return undefined;
  const values = new Set<string>();
  for (const variant of type.variants.values()) {
    if (variant.type.kind !== "String") return undefined;
    values.add(variant.type.value);
  }
  return values;
}

function wireNames(program: Program, model: Model): Set<string> {
  const names = new Set<string>();
  for (const property of walkPropertiesInherited(model)) {
    names.add(resolveEncodedName(program, property, "application/json"));
  }
  return names;
}

interface Twin {
  readonly discriminator: string;
  readonly tags: readonly string[];
  readonly properties: number;
}

function flatTwin(program: Program, model: Model, union: Union): Twin | undefined {
  const [discriminated] = getDiscriminatedUnion(program, union);
  // With an envelope the wire shape is `{ tag, value }`; no flat model matches it.
  if (!discriminated || discriminated.options.envelope !== "none") return undefined;
  const discriminator = discriminated.options.discriminatorPropertyName;
  const variants = [...discriminated.variants.entries()];
  if (variants.length === 0 || variants.some(([, variant]) => variant.kind !== "Model" || variant === model)) return undefined;

  const property = [...walkPropertiesInherited(model)].find((candidate) => candidate.name === discriminator);
  const values = property && literalValues(property.type);
  const tags = variants.map(([tag]) => tag).sort(compareText);
  if (!values || values.size !== tags.length || tags.some((tag) => !values.has(tag))) return undefined;

  const flat = wireNames(program, model);
  const covered = new Set<string>();
  for (const [, variant] of variants) {
    for (const name of wireNames(program, variant as Model)) {
      if (!flat.has(name)) return undefined;
      covered.add(name);
    }
  }
  return covered.size === flat.size ? { discriminator, tags, properties: flat.size } : undefined;
}

export const wireRootTwinRule = createRule({
  name: "wire-root-twin",
  severity: "warning",
  description: "One message has one wire root: a flat model and the discriminated union of the same message are not both roots.",
  messages: {
    default: paramMessage`Wire roots '${"model"}' and '${"union"}' describe one message. '${"model"}' is the flat form of the discriminated union: its '${"discriminator"}' takes exactly the variant tags (${"tags"}), and the variants' properties together are exactly its ${"properties"} properties. Two roots for one message both enter the contract revision and can drift apart; keep the one both implementations read.`,
  },
  create(context) {
    return {
      exit: () => {
        const program = context.program;
        const roots = contractGraph(program).wireRoots;
        const unions = roots.filter((root): root is Union => root.kind === "Union");
        for (const model of roots) {
          if (model.kind !== "Model") continue;
          for (const union of unions) {
            const twin = flatTwin(program, model, union);
            if (!twin) continue;
            context.reportDiagnostic({
              target: model,
              format: {
                model: fullName(model),
                union: fullName(union),
                discriminator: twin.discriminator,
                tags: twin.tags.map((tag) => `"${tag}"`).join(", "),
                properties: String(twin.properties),
              },
            });
          }
        }
      },
    };
  },
});
