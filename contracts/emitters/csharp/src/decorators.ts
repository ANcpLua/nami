import type { Model, ModelProperty, Namespace, Program, Type, Union } from "@typespec/compiler";
import { getAutoDecoratorTargets, getAutoDecoratorValue, hasAutoDecorator } from "@typespec/compiler";
import type { ColumnOptions, OutputOptions, TableOptions } from "./outputs.js";

/** The namespace of lib/main.tsp: the compiler keys an `auto dec` by its fully qualified name. */
const NAMESPACE = "Nami.Api.Schema.Emit.CSharp";

/**
 * What the compiler stores for each `auto dec` of lib/main.tsp: one record per application, keyed by the parameter
 * names declared there. An optional parameter that was not passed is `undefined`; a decorator without parameters
 * stores `{}`. `catalogValue` is absent: it validates its argument in JavaScript and keeps its own state key.
 */
export interface DecoratorArguments {
  csharpNamespace: { ns: string };
  csharpBrand: Record<string, never>;
  csharpPolymorphic: { discriminator?: string };
  csharpOutput: { options: OutputOptions };
  csharpForm: { form: string };
  csharpType: { type: string };
  duckDbTable: { options: TableOptions };
  duckDbColumn: { options: ColumnOptions };
  tool: { name: string; result: Model; error: Model };
  wireRoot: Record<string, never>;
  slotValue: Record<string, never>;
  storageProjection: { destination: Model };
  storageProjectionUsing: { method: string };
  modelProjection: { source: Model; owner: Model | Namespace; additional?: Model };
  projectionSource: { source: ModelProperty };
  catalogProjection: { values: Union };
  environment: { name: string };
  configuredBy: { key: ModelProperty };
  csharpSetter: { setter: "set" | "internal set" | "init" };
  csharpInternal: Record<string, never>;
  attributeProjection: { destination: Union };
  csharpEnum: { type: string; flags?: boolean };
  storageAttributeProjection: { destination: Union };
}
type DecoratorName = keyof DecoratorArguments;

/** Whether the decorator is applied to the target. */
export function isApplied(program: Program, name: DecoratorName, target: Type): boolean {
  return hasAutoDecorator(program, `${NAMESPACE}.${name}`, target);
}

/** The arguments of the decorator's application on the target; `undefined` when it is not applied. */
export function argumentsOf<K extends DecoratorName>(program: Program, name: K, target: Type): DecoratorArguments[K] | undefined {
  return getAutoDecoratorValue(program, `${NAMESPACE}.${name}`, target) as DecoratorArguments[K] | undefined;
}

/** Every target of the decorator with its arguments, in the order the compiler applied them. */
export function applications<K extends DecoratorName>(program: Program, name: K): [Type, DecoratorArguments[K]][] {
  return [...getAutoDecoratorTargets(program, `${NAMESPACE}.${name}`)] as [Type, DecoratorArguments[K]][];
}

/** A type's own C# namespace: `@csharpNamespace`, else the namespace of a `@csharpOutput` on it. */
export function getCsharpNamespace(program: Program, target: Type): string | undefined {
  return argumentsOf(program, "csharpNamespace", target)?.ns ?? argumentsOf(program, "csharpOutput", target)?.options.namespace;
}

/** The C# namespace a type is emitted in: its own `@csharpNamespace`, else that of its nearest enclosing namespace. */
export function csharpNamespaceOf(program: Program, target: Type): string | undefined {
  const own = getCsharpNamespace(program, target);
  if (own) return own;
  for (let cursor = (target as { namespace?: Namespace }).namespace; cursor; cursor = cursor.namespace) {
    const mapped = getCsharpNamespace(program, cursor);
    if (mapped) return mapped;
  }
  return undefined;
}
