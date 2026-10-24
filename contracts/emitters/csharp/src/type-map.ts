import type { Model, Program, Scalar, Type, Union } from "@typespec/compiler";
import { getDiscriminatedUnion, isArrayModelType, isRecordModelType, getFormat } from "@typespec/compiler";
import { getCsharpNamespace, hasCsharpPolymorphic } from "./decorators.js";
import { hasCsharpBrand } from "./decorators.js";
import { reportDiagnostic } from "./lib.js";

const SCALAR_MAP: Record<string, string> = {
  "int8": "sbyte",
  "int16": "short",
  "int32": "int",
  "int64": "long",
  "uint8": "byte",
  "uint16": "ushort",
  "uint32": "uint",
  "uint64": "ulong",
  "float32": "float",
  "float64": "double",
  "decimal": "decimal",
  "decimal128": "decimal",
  "boolean": "bool",
  "bytes": "ReadOnlyMemory<byte>",
  "utcDateTime": "DateTimeOffset",
  "offsetDateTime": "DateTimeOffset",
  "plainDate": "DateOnly",
  "plainTime": "TimeOnly",
  "duration": "TimeSpan",
  "url": "Uri",
};

export function mapType(program: Program, type: Type): string {
  switch (type.kind) {
    case "Scalar":
      return mapScalar(program, type as Scalar);
    case "Model":
      if (isArrayModelType(type)) {
        const inner = mapType(program, type.indexer!.value);
        return `IReadOnlyList<${inner}>`;
      }
      if (isRecordModelType(type)) {
        const inner = mapType(program, type.indexer!.value);
        return `IReadOnlyDictionary<string, ${inner}>`;
      }
      return qualifyModelOrEnum(program, type);
    case "Enum":
      return qualifyModelOrEnum(program, type);
    case "ModelProperty":
      // TypeSpec 1.13 constraint-based member access (e.g. `P.id`): the property
      // reference stands for the referenced property's type.
      return mapType(program, (type as { type: Type }).type);
    case "EnumMember":
      return qualifyModelOrEnum(program, (type as { enum: Type }).enum);
    case "Union": {
      if (hasCsharpPolymorphic(program, type)) return qualifyModelOrEnum(program, type);
      if (isNamedFloatingPointUnion(type)) return "double";
      if (isDiscriminatedUnion(program, type)) return qualifyModelOrEnum(program, type);
      const mixed = mixedUnionShape(program, type);
      if (mixed) return `${qualifyModelOrEnum(program, type)}${mixed.hasNull ? "?" : ""}`;
      const members = [...type.variants.values()].map((variant) => variant.type);
      const nonNull = members.filter((member) => member.kind !== "Intrinsic" || member.name !== "null");
      if (nonNull.length !== members.length) {
        // Nullability must not erase a named model/union (or a primitive) into object.
        // For example, MetricPointData | null is still the polymorphic point contract.
        const mapped = new Set(nonNull.map((member) => mapType(program, member)));
        if (mapped.size === 1) {
          const inner = [...mapped][0]!;
          return inner.endsWith("?") ? inner : `${inner}?`;
        }
        return "object?";
      }
      // Preserve homogeneous primitive literal unions as their wire primitive. Structural
      // unions such as LogBody and AttributeValue still require a generated discriminated
      // representation before they can be stronger than object.
      if ([...type.variants.values()].every((variant) => variant.type.kind === "String")) return "string";
      if ([...type.variants.values()].every((variant) => variant.type.kind === "Boolean")) return "bool";
      if ([...type.variants.values()].every((variant) => variant.type.kind === "Number")) return "double";
      return "object";
    }
    case "Boolean":
      return "bool";
    case "String":
      return "string";
    case "Number":
      return "double";
    case "Intrinsic":
      if ((type as { name?: string }).name === "null") return "null";
      if ((type as { name?: string }).name === "unknown") return "JsonElement";
      if ((type as { name?: string }).name === "void") return "void";
      if ((type as { name?: string }).name === "never") return "void";
      reportDiagnostic(program, { code: "unmapped-type", target: type, format: { name: `intrinsic:${(type as { name?: string }).name ?? "?"}` } });
      return "object";
    default:
      reportDiagnostic(program, { code: "unmapped-type", target: type, format: { name: type.kind } });
      return "object";
  }
}

/** A union declared with `@discriminated`: its variants are models and the variant name is the wire tag. */
export function isDiscriminatedUnion(program: Program, union: Union): boolean {
  return getDiscriminatedUnion(program, union)[0] !== undefined;
}

export type MixedUnionShape = {
  hasNull: boolean;
  primitives: Array<"string" | "boolean" | "number">;
  arrayOfSelf: boolean;
  /** The one object-shaped member: a model, or a discriminated union of models. */
  objectMember?: { name: string; type: Model | Union };
};

/**
 * A named union of bare JSON primitives (and optionally an array of itself) plus one
 * object-shaped member, such as AttributeValue. Primitive variants dispatch on the JSON
 * token kind; the object member dispatches on its own discriminator.
 */
export function mixedUnionShape(program: Program, union: Union): MixedUnionShape | undefined {
  if (!union.name || isNamedFloatingPointUnion(union)) return undefined;
  const shape: MixedUnionShape = { hasNull: false, primitives: [], arrayOfSelf: false };
  for (const [name, variant] of union.variants) {
    const t = variant.type;
    if (t.kind === "Intrinsic" && t.name === "null") { shape.hasNull = true; continue; }
    if (t.kind === "Scalar") {
      const root = rootScalarName(t);
      if (root === "string") { shape.primitives.push("string"); continue; }
      if (root === "boolean") { shape.primitives.push("boolean"); continue; }
      if (NUMERIC_SCALARS.has(root)) { shape.primitives.push("number"); continue; }
      return undefined;
    }
    if (t.kind === "Model" && isArrayModelType(t)) {
      if (t.indexer?.value === union) { shape.arrayOfSelf = true; continue; }
      return undefined;
    }
    const isObjectMember = (t.kind === "Model" && !isRecordModelType(t)) ||
      (t.kind === "Union" && isDiscriminatedUnion(program, t));
    if (!isObjectMember || shape.objectMember) return undefined;
    shape.objectMember = { name: String(name), type: t as Model | Union };
  }
  const hasPrimitiveSide = shape.primitives.length > 0 || shape.arrayOfSelf;
  return hasPrimitiveSide && shape.objectMember ? shape : undefined;
}

const NUMERIC_SCALARS = new Set(["int8", "int16", "int32", "int64", "uint8", "uint16", "uint32", "uint64", "float32", "float64", "decimal", "decimal128"]);

function rootScalarName(scalar: Scalar): string {
  return scalar.baseScalar ? rootScalarName(scalar.baseScalar) : scalar.name;
}

export function isNamedFloatingPointUnion(type: Type): boolean {
  if (type.kind !== "Union") return false;

  let hasFiniteDouble = false;
  const namedValues = new Set<string>();
  for (const variant of type.variants.values()) {
    if (variant.type.kind === "Scalar" && isFloat64Scalar(variant.type)) {
      if (hasFiniteDouble) return false;
      hasFiniteDouble = true;
      continue;
    }
    if (variant.type.kind === "String") {
      namedValues.add(variant.type.value);
      continue;
    }
    return false;
  }

  const canonicalNamedValues = new Set(["NaN", "Infinity", "-Infinity"]);
  return hasFiniteDouble &&
    namedValues.size > 0 &&
    [...namedValues].every((value) => canonicalNamedValues.has(value));
}

function isFloat64Scalar(scalar: Scalar): boolean {
  if (scalar.name === "float64") return true;
  return scalar.baseScalar ? isFloat64Scalar(scalar.baseScalar) : false;
}

function mapScalar(program: Program, scalar: Scalar): string {
  if (hasCsharpBrand(program, scalar)) return qualifyModelOrEnum(program, scalar);
  if (scalar.name === "string") {
    const format = getFormat(program, scalar as unknown as Parameters<typeof getFormat>[1]);
    if (format === "uuid") return "Guid";
    if (format === "url" || format === "uri") return "Uri";
    return "string";
  }
  const direct = SCALAR_MAP[scalar.name];
  if (direct) return direct;
  if (scalar.baseScalar) return mapScalar(program, scalar.baseScalar);
  reportDiagnostic(program, { code: "unmapped-type", target: scalar, format: { name: `scalar:${scalar.name}` } });
  return "object";
}

function qualifyModelOrEnum(program: Program, type: Type): string {
  const asModel = type as { name?: string; templateMapper?: { args: readonly Type[] } };
  let name = asModel.name ?? "object";
  if (asModel.templateMapper?.args?.length) {
    // Templated model: use the instantiated name (`CursorPageTrace`) so it matches the
    // name the emitter produces for the type declaration.
    const parts = [name];
    for (const arg of asModel.templateMapper.args) {
      if ("name" in arg && typeof (arg as { name?: string }).name === "string") {
        parts.push(pascal((arg as { name: string }).name));
      }
    }
    name = parts.join("");
  }
  const ownNs = getCsharpNamespace(program, type);
  if (ownNs) return `${ownNs}.${name}`;
  const parentNs = climbForNamespace(program, type);
  return parentNs ? `${parentNs}.${name}` : name;
}

function pascal(s: string): string {
  return s.length === 0 ? s : s[0].toUpperCase() + s.slice(1);
}

function climbForNamespace(program: Program, type: Type): string | undefined {
  let cursor: { namespace?: { name?: string } & { namespace?: unknown } } | undefined =
    type as { namespace?: { name?: string } & { namespace?: unknown } };
  while (cursor?.namespace) {
    const ns = cursor.namespace as unknown as Type;
    const mapped = getCsharpNamespace(program, ns);
    if (mapped) return mapped;
    cursor = ns as unknown as typeof cursor;
  }
  return undefined;
}
