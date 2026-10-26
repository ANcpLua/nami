import type { EncodeData, ModelProperty, Numeric, Program, Scalar, Type } from "@typespec/compiler";
import { getEncode, getMaxValueAsNumeric, getMinValueAsNumeric, getNamespaceFullName, isArrayModelType, resolveEncodedName } from "@typespec/compiler";

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
