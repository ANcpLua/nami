// Redundancy — the same model declared twice.
//
// Two models that declare the same properties, with the same wire names, types
// and optionality, are one shape with two names. Each becomes a generated type,
// a schema and a serializer, and nothing keeps the two declarations in step.
//
// The contract mirrors shapes across layers on purpose, and says so in the
// source. Those cases are not copies and are not reported (all three exist at
// 48bdb08):
//
//   model SourcesStatusInput extends ToolReadInput {}           inherits everything
//   model MetricQuantileValue { ...S.StoredQuantileValue; }     spreads everything
//   @@storageProjection(MetricExponentialBuckets, StoredExponentialBuckets)
//                                                               a declared projection pair
//
// So a model takes part only if it declares at least
// `MIN_DUPLICATE_SHAPE_PROPERTIES` properties itself, and a pair is skipped when
// one derives from the other or a projection decorator names both. A storage
// row never takes part: two tables with the same columns are still two tables.

import type { Model, Program } from "@typespec/compiler";
import {
  createRule,
  isArrayModelType,
  isRecordModelType,
  isTemplateDeclaration,
  paramMessage,
  resolveEncodedName,
  walkPropertiesInherited,
} from "@typespec/compiler";
import { compareText, contractGraph, emitDecorator, emitDecorators, fullName, typeKey } from "../contract.js";
import { MIN_DUPLICATE_SHAPE_PROPERTIES, TABLE_DECORATOR } from "../policy.js";

/** Wire name, optionality and type of every effective property, in a fixed order. */
function shapeOf(program: Program, model: Model): { signature: string; size: number } {
  const properties = [...walkPropertiesInherited(model)].map(
    (property) =>
      `${resolveEncodedName(program, property, "application/json")}${property.optional ? "?" : ""}: ${typeKey(property.type)}`,
  );
  properties.sort(compareText);
  return { signature: properties.join("; "), size: properties.length };
}

function derivesFrom(model: Model, other: Model, visited = new Set<Model>()): boolean {
  if (model === other) return true;
  if (visited.has(model)) return false;
  visited.add(model);
  if (model.baseModel && derivesFrom(model.baseModel, other, visited)) return true;
  return model.sourceModels.some((source) => derivesFrom(source.model, other, visited));
}

/** Whether an emitter decorator on one model names the other: a declared mirror, not a copy. */
function namesInDecorator(model: Model, other: Model): boolean {
  return emitDecorators(model).some((decorator) =>
    decorator.application.args.some((argument) => (argument.value as unknown) === other),
  );
}

function related(left: Model, right: Model): boolean {
  return (
    derivesFrom(left, right) || derivesFrom(right, left) || namesInDecorator(left, right) || namesInDecorator(right, left)
  );
}

export const duplicateModelShapeRule = createRule({
  name: "duplicate-model-shape",
  severity: "warning",
  description: "No two independent models declare the same properties with the same wire names, types and optionality.",
  messages: {
    default: paramMessage`Model '${"model"}' declares the same ${"count"} properties as '${"first"}': the same wire names, types and optionality. Use '${"first"}', or derive one from the other (extends, spread or 'is') so the relationship is stated and cannot drift.`,
  },
  create(context) {
    return {
      exit: () => {
        const program = context.program;
        const groups = new Map<string, { size: number; models: Model[] }>();
        // Declarations arrive sorted by full name, so each group is too.
        for (const declaration of contractGraph(program).declarations) {
          if (declaration.kind !== "Model" || isTemplateDeclaration(declaration)) continue;
          if (isArrayModelType(declaration) || isRecordModelType(declaration)) continue;
          if (emitDecorator(declaration, TABLE_DECORATOR)) continue;
          const own = [...declaration.properties.values()].filter((property) => property.sourceProperty === undefined);
          if (own.length < MIN_DUPLICATE_SHAPE_PROPERTIES) continue;
          const shape = shapeOf(program, declaration);
          const group = groups.get(shape.signature);
          if (group) group.models.push(declaration);
          else groups.set(shape.signature, { size: shape.size, models: [declaration] });
        }

        const duplicates = [...groups.values()].filter((group) => group.models.length > 1);
        duplicates.sort((left, right) => compareText(fullName(left.models[0]!), fullName(right.models[0]!)));
        for (const group of duplicates) {
          for (const [index, model] of group.models.entries()) {
            const first = group.models.slice(0, index).find((earlier) => !related(earlier, model));
            if (!first) continue;
            context.reportDiagnostic({
              target: model,
              format: { model: fullName(model), first: fullName(first), count: String(group.size) },
            });
          }
        }
      },
    };
  },
});
