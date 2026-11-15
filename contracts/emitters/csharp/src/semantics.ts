import type { EncodeData, ModelProperty, Namespace, Numeric, Program, Scalar, Type } from "@typespec/compiler";
import { getEncode, getMaxValueAsNumeric, getMinValueAsNumeric, getNamespaceFullName, isArrayModelType, resolveEncodedName } from "@typespec/compiler";
import { isBody, isBodyRoot, isMetadata } from "@typespec/http";

// Naming and selection rules both emitters apply. The TypeScript emitter imports this module from csharp/dist, so a
// rule changes here once instead of drifting between two copies.

/** The TypeSpec numeric scalars: a C# number in a mixed union, a TypeScript `number`. */
export const NUMERIC_SCALARS: ReadonlySet<string> = new Set(["int8", "int16", "int32", "int64", "uint8", "uint16", "uint32", "uint64", "float32", "float64", "decimal", "decimal128"]);

/** The first character upper-cased; the empty string stays empty. */
export function pascal(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}

/** Declared by the compiler's own `TypeSpec` namespace (or one below it), so no emitter writes it. */
export function isInTypeSpec(type: { namespace?: { name?: string; namespace?: unknown } }): boolean {
  let cursor: { name?: string; namespace?: unknown } | undefined = type.namespace;
  while (cursor) {
    if (cursor.name === "TypeSpec") return true;
    cursor = cursor.namespace as typeof cursor;
  }
  return false;
}

/**
 * Headers, parameters and the status code travel beside the body, and a `@body`/`@bodyRoot`
 * property *is* the body: a response envelope made only of these has no type of its own.
 */
export function isHttpMetadata(program: Program, property: ModelProperty): boolean {
  return isMetadata(program, property) || isBody(program, property) || isBodyRoot(program, property);
}

/**
 * A type's emitted name: the name allocated to it for `language`, else its declared name followed by its template
 * arguments' names, so each instantiation is a distinct type (`CursorPage<Trace>` is `CursorPageTrace`).
 */
export function emittedName(program: Program, type: Type, language = "csharp"): string {
  const named = type as Type & { name?: string; templateMapper?: { args: readonly object[] } };
  const parts = [named.name ?? "object"];
  for (const arg of named.templateMapper?.args ?? []) {
    if ("kind" in arg && "name" in arg && typeof arg.name === "string") parts.push(pascal(arg.name));
  }
  return languageName(program, type, parts.join(""), language);
}

/** The published schema name of a contract type: its namespace below `Nami.Api.Contracts`, dotted, then its name. */
export function schemaName(type: { name?: string; namespace?: Namespace }): string {
  const root = "Nami.Api.Contracts";
  const namespace = type.namespace ? getNamespaceFullName(type.namespace) : "";
  const below = namespace === root ? "" : namespace.startsWith(root + ".") ? namespace.slice(root.length + 1) : namespace;
  return below ? `${below}.${type.name ?? ""}` : type.name ?? "";
}

/** One compiler-owned property, with precision and presence preserved for every output. */
export function resolveProperty(program: Program, property: ModelProperty) {
  return {
    property, logicalType: property.type, codeName: property.name,
    jsonName: resolveEncodedName(program, property, "application/json"),
    optional: property.optional, nullable: admitsNull(property.type),
    encoding: effectiveEncoding(program, property.type, getEncode(program, property)),
    minimum: numericBound(program, property, getMinValueAsNumeric),
    maximum: numericBound(program, property, getMaxValueAsNumeric),
    defaultValue: property.defaultValue,
  };
}
export function admitsNull(type: Type): boolean {
  return type.kind === "Intrinsic" && type.name === "null" || type.kind === "Union" && [...type.variants.values()].some(v => admitsNull(v.type));
}
export function nonNullType(type: Type): Type {
  if (type.kind !== "Union") return type;
  const members = [...type.variants.values()].map(v => v.type).filter(t => !admitsNull(t));
  return members.length === 1 ? members[0]! : type;
}
export function effectiveEncoding(program: Program, type: Type, override?: EncodeData): EncodeData | undefined {
  if (override) return override;
  const inner = nonNullType(type);
  if (inner !== type) return effectiveEncoding(program, inner);
  if (type.kind === "Model" && isArrayModelType(type)) return effectiveEncoding(program, type.indexer!.value);
  if (type.kind === "Scalar") {
    for (let current: Scalar | undefined = type; current; current = current.baseScalar) {
      const encoding = getEncode(program, current);
      if (encoding) return encoding;
    }
  }
  return undefined;
}
export function numericBound(program: Program, property: ModelProperty, read: (p: Program, t: Type) => Numeric | undefined): string | undefined {
  const own = read(program, property);
  if (own) return own.toString();
  const type = nonNullType(property.type);
  for (let scalar = type.kind === "Scalar" ? type : undefined; scalar; scalar = scalar.baseScalar) {
    const bound = read(program, scalar);
    if (bound) return bound.toString();
  }
  return undefined;
}
/** Persistent identities include namespaces and template arguments; traversal caches use Type objects. */
export function typeIdentity(type: Type): string {
  if (type.kind === "ModelProperty") return `${type.model ? typeIdentity(type.model) : ""}.${type.name}`;
  if (type.kind === "String") return JSON.stringify(type.value);
  if (type.kind === "Number") return type.valueAsString;
  if (type.kind === "Boolean") return String(type.value);
  const named = type as Type & { name?: string; namespace?: import("@typespec/compiler").Namespace };
  const prefix = named.namespace ? getNamespaceFullName(named.namespace) : "";
  const name = `${prefix ? prefix + "." : ""}${named.name ?? type.kind}`;
  if (type.kind !== "Model" || !type.templateMapper) return name;
  return `${name}<${type.templateMapper.args.map(arg => "kind" in arg ? typeIdentity(arg as Type) : String(arg)).join(",")}>`;
}

const allocatedNames = new WeakMap<Program, Map<string, Map<Type, string>>>();
export function allocateNames(program: Program, candidates: {type: Type; scope: string; name: string}[], language = "csharp"): void {
  const counts = new Map<string, Set<Type>>();
  for (const item of candidates) {
    const key = `${item.scope}:${item.name}`;
    const values = counts.get(key) ?? new Set<Type>(); values.add(item.type); counts.set(key, values);
  }
  const names = new Map<Type,string>();
  for (const item of candidates) names.set(item.type, counts.get(`${item.scope}:${item.name}`)!.size === 1 ? item.name
    : Array.from(typeIdentity(item.type)).map(c => /[A-Za-z0-9]/u.test(c) ? c : `_${c.codePointAt(0)!.toString(16)}_`).join(""));
  const languages = allocatedNames.get(program) ?? new Map<string, Map<Type,string>>();
  languages.set(language,names); allocatedNames.set(program,languages);
}
export function languageName(program: Program, type: Type, fallback: string, language = "csharp"): string {
  return allocatedNames.get(program)?.get(language)?.get(type) ?? fallback;
}

const writtenOutputs = new WeakMap<Program, Set<string>>();
export function recordOutput(program: Program, path: string): string {
  const files = writtenOutputs.get(program) ?? new Set<string>();
  files.add(path); writtenOutputs.set(program, files); return path;
}
export function outputFiles(program: Program): string[] { return [...writtenOutputs.get(program) ?? []]; }
