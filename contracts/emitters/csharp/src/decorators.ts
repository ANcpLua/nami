import type { DecoratorContext, Enum, Model, Namespace, Scalar, Type, Union } from "@typespec/compiler";
import { stateKeys } from "./lib.js";

export function $csharpNamespace(
  context: DecoratorContext,
  target: Model | Enum | Namespace,
  ns: string,
): void {
  context.program.stateMap(stateKeys.csharpNamespace).set(target, ns);
}

export function $csharpBrand(context: DecoratorContext, target: Scalar): void {
  context.program.stateMap(stateKeys.csharpBrand).set(target, true);
}

export function $csharpPolymorphic(
  context: DecoratorContext,
  target: Union,
  discriminator?: string,
): void {
  context.program.stateMap(stateKeys.csharpPolymorphic).set(target, discriminator ?? null);
}

export function getCsharpNamespace(program: { stateMap: (k: symbol) => Map<Type, unknown> }, target: Type): string | undefined {
  return program.stateMap(stateKeys.csharpNamespace).get(target) as string | undefined;
}

/** The C# namespace a type is emitted in: its own `@csharpNamespace`, else that of its nearest enclosing namespace. */
export function csharpNamespaceOf(program: { stateMap: (k: symbol) => Map<Type, unknown> }, target: Type): string | undefined {
  const own = getCsharpNamespace(program, target);
  if (own) return own;
  for (let cursor = (target as { namespace?: Namespace }).namespace; cursor; cursor = cursor.namespace) {
    const mapped = getCsharpNamespace(program, cursor);
    if (mapped) return mapped;
  }
  return undefined;
}

export function hasCsharpBrand(program: { stateMap: (k: symbol) => Map<Type, unknown> }, target: Type): boolean {
  return program.stateMap(stateKeys.csharpBrand).has(target);
}

export function hasCsharpPolymorphic(program: { stateMap: (k: symbol) => Map<Type, unknown> }, target: Type): boolean {
  return program.stateMap(stateKeys.csharpPolymorphic).has(target);
}

export function getCsharpPolymorphicDiscriminator(
  program: { stateMap: (k: symbol) => Map<Type, unknown> },
  target: Union,
): string | null | undefined {
  return program.stateMap(stateKeys.csharpPolymorphic).get(target) as string | null | undefined;
}
