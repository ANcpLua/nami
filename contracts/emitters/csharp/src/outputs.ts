import { recordOutput } from "./semantics.js";
import { emitSlotValue } from "./slots.js";
import type { DecoratorContext, Enum, Model, ModelProperty, Namespace, Program, Type, Value, Scalar, Union } from "@typespec/compiler";
import { emitFile, getDiscriminator, isArrayModelType, isRecordModelType, navigateProgram, walkPropertiesInherited } from "@typespec/compiler";
import { resolve } from "node:path";
import { stateKeys } from "./lib.js";
import { isNamedFloatingPointUnion, mapType, mixedUnionShape } from "./type-map.js";
import { renderMixedUnion } from "./mixed-union.js";
import { resolveProperty, typeIdentity } from "./semantics.js";

export interface OutputOptions { directory: string; namespace: string; file: string; json?: boolean; context?: string; typescript?: boolean; schema?: boolean; mutable?: boolean; strictJson?: boolean }
export interface TableOptions { name: string; indexes?: string; uniqueIndexes?: string; conflict?: "ignore" | "update"; parameterSets?: string; derived?: boolean; bootstrap?: boolean; appenderEligible?: boolean; arrowEligible?: boolean }
export interface ColumnOptions { order: number; sqlType?: string; primaryKeyOrdinal?: number; defaultSql?: string; excludeFromInsert?: boolean; privateInit?: boolean; sequenceName?: string; merge?: "replace" | "coalesce" | "min" | "max" | "minPresent" | "longest" }
export function $csharpEnum(ctx: DecoratorContext, target: Enum, type: string, flags = false): void {
  ctx.program.stateMap(stateKeys.enumType).set(target,type);
  if (flags) ctx.program.stateMap(stateKeys.enumFlags).set(target,true);
}
export function $configuredBy(ctx: DecoratorContext, target: ModelProperty, key: ModelProperty): void {
  ctx.program.stateMap(stateKeys.configuredBy).set(target,key);
}
export function $csharpSetter(ctx: DecoratorContext, target: ModelProperty, setter: string): void {
  ctx.program.stateMap(stateKeys.setter).set(target,setter);
}
export function $csharpInternal(ctx: DecoratorContext, target: ModelProperty): void {
  ctx.program.stateMap(stateKeys.internalProperty).set(target,true);
}
export function $csharpOutput(ctx: DecoratorContext, target: Namespace, options: OutputOptions): void {
  ctx.program.stateMap(stateKeys.output).set(target, options);
  ctx.program.stateMap(stateKeys.csharpNamespace).set(target, options.namespace);
}
export function $csharpForm(ctx: DecoratorContext, target: Model, form: string): void { ctx.program.stateMap(stateKeys.form).set(target, form); }
export function $csharpType(ctx: DecoratorContext, target: ModelProperty | Model | Scalar | Union, type: string): void { ctx.program.stateMap(stateKeys.propertyType).set(target, type); }
export function $duckDbTable(ctx: DecoratorContext, target: Model, options: TableOptions): void { ctx.program.stateMap(stateKeys.table).set(target, options); }
export function $duckDbColumn(ctx: DecoratorContext, target: ModelProperty, options: ColumnOptions): void { ctx.program.stateMap(stateKeys.column).set(target, options); }
export function $environment(ctx: DecoratorContext, target: ModelProperty, name: string): void { ctx.program.stateMap(stateKeys.environment).set(target,name); }
export function outputFor(program: Program, type: Type): OutputOptions | undefined {
  let current: Type | undefined = type;
  while (current) {
    const value = program.stateMap(stateKeys.output).get(current) as OutputOptions | undefined;
    if (value) return value;
    current = "namespace" in current ? current.namespace : undefined;
  }
  return undefined;
}
const quote = (value: unknown): string => JSON.stringify(value);
const pascal = (value: string): string => value.charAt(0).toUpperCase() + value.slice(1);
function namedFloat(t: Type): boolean {
  if (t.kind === "Model" && t.indexer) return namedFloat(t.indexer.value);
  if (t.kind !== "Union") return false;
  if (isNamedFloatingPointUnion(t)) return true;
  const members = [...t.variants.values()].map(v => v.type).filter(t => t.kind !== "Intrinsic" || t.name !== "null");
  return members.length === 1 && namedFloat(members[0]!);
}
function attributes(options: object, omit: string[]): string {
  return Object.entries(options).filter(([key]) => !omit.includes(key)).map(([key, value]) => `${pascal(key)} = ${quote(value)}`).join(", ");
}
function conflictClause(program: Program, table: TableOptions, properties: ModelProperty[]): string | undefined {
  const columns = properties.flatMap(property => {
    const metadata = program.stateMap(stateKeys.column).get(property) as ColumnOptions | undefined;
    if (!metadata) return [];
    // Match the Roslyn generator's CLR-property to column-name mapping.
    const name = pascal(property.name).replace(/[A-Z]/gu, (c, index) => (index ? "_" : "") + c.toLowerCase());
    return [{ name: `"${name}"`, ...metadata }];
  });
  const updates = columns.filter(column => column.merge !== undefined);
  if (table.conflict !== "update") {
    if (updates.length) throw new Error(`${table.name} declares merge rules without update conflicts`);
    return table.conflict === "ignore" ? "ON CONFLICT DO NOTHING" : undefined;
  }
  const keys = columns.filter(c => c.primaryKeyOrdinal !== undefined).sort((a,b) => a.primaryKeyOrdinal! - b.primaryKeyOrdinal!);
  if (!keys.length || !updates.length) throw new Error(`${table.name} update conflicts need keys and merge columns`);
  const assignments = updates.map(column => {
    if (column.primaryKeyOrdinal !== undefined || column.excludeFromInsert) throw new Error(`${table.name}.${column.name} cannot merge a key or database-generated value`);
    const current = column.name, incoming = `EXCLUDED.${current}`;
    let expression: string;
    switch (column.merge) {
      case "replace": expression = incoming; break;
      case "coalesce": expression = `coalesce(${incoming}, ${current})`; break;
      case "min": expression = `least(${current}, ${incoming})`; break;
      case "max": expression = `greatest(${current}, ${incoming})`; break;
      case "minPresent": expression = `coalesce(least(${current}, ${incoming}), ${current}, ${incoming})`; break;
      case "longest": expression = `CASE WHEN length(coalesce(${incoming}, '')) > length(coalesce(${current}, '')) THEN ${incoming} WHEN length(coalesce(${incoming}, '')) < length(coalesce(${current}, '')) THEN ${current} ELSE greatest(${current}, ${incoming}) END`; break;
      default: throw new Error(`Unsupported merge rule ${column.merge}`);
    }
    return `${current} = ${expression}`;
  });
  return `ON CONFLICT (${keys.map(key => key.name).join(", ")}) DO UPDATE SET\n    ${assignments.join(",\n    ")}`;
}
function valueExpression(value: Value): string {
  switch (value.valueKind) {
    case "NumericValue": return value.value.toString();
    case "StringValue": return JSON.stringify(value.value);
    case "BooleanValue": return String(value.value);
    case "NullValue": return "null";
    case "ArrayValue": return `[${value.values.map(valueExpression).join(", ")}]`;
    case "ObjectValue": return `new() { ${[...value.properties.values()].map(p => `${pascal(p.name)} = ${valueExpression(p.value)}`).join(", ")} }`;
    default: throw new Error(`Unsupported C# default ${value.valueKind}`);
  }
}
function defaultExpression(property: ModelProperty): string | undefined {
  return property.defaultValue ? valueExpression(property.defaultValue) : undefined;
}
export async function emitOutputs(program: Program): Promise<void> {
  const groups = new Map<OutputOptions, Model[]>();
  const seen = new Set<Type>();
  const enums = new Map<OutputOptions, Enum[]>();
  const mixedUnions = new Map<OutputOptions, Union[]>();
  navigateProgram(program, { model(model) {
    const output = outputFor(program, model);
    if (!output || !model.name || program.stateMap(stateKeys.propertyType).has(model) || seen.has(model) || model.namespace?.name === "TypeSpec") return;
    seen.add(model);
    const values = groups.get(output) ?? [];
    values.push(model); groups.set(output, values);
  }, enum(type) {
    const output = outputFor(program,type);
    if (!output) return;
    const values = enums.get(output) ?? []; values.push(type); enums.set(output, values);
    if (!groups.has(output)) groups.set(output,[]);
  }, union(type) {
    const output = outputFor(program, type);
    if (!output || !type.name || program.stateMap(stateKeys.propertyType).has(type) || !mixedUnionShape(program, type)) return;
    const values = mixedUnions.get(output) ?? []; values.push(type); mixedUnions.set(output, values);
    if (!groups.has(output)) groups.set(output, []);
  } });
  const slotUnions: Union[] = [];
  navigateProgram(program, { union(union) { if (program.stateMap(stateKeys.slot).has(union)) slotUnions.push(union); } });
  for (const union of slotUnions) await emitSlotValue(program, union);
  const manifest: object[] = slotUnions.map(type => ({type:typeIdentity(type),project:outputFor(program,type)!.directory,file:`${type.name}.g.cs`,namespace:outputFor(program,type)!.namespace,form:"typed-slots",json:false}));
  for (const [output, models] of groups) {
    const body: string[] = [];
    for (const union of mixedUnions.get(output) ?? []) {
      body.push(renderMixedUnion(program, union, mixedUnionShape(program, union)!, "internal"));
      manifest.push({type:typeIdentity(union),project:output.directory,file:output.file,namespace:output.namespace,form:"closed-mixed-union",json:output.json ?? false});
    }
    for (const model of models) {
      if (isArrayModelType(model) || isRecordModelType(model)) {
        if (!output.json || !output.context) throw new Error(`${typeIdentity(model)} needs a JSON output context`);
        manifest.push({type:typeIdentity(model),project:output.directory,file:output.file,namespace:output.namespace,form:"json-root",json:true});
        continue;
      }
      const table = program.stateMap(stateKeys.table).get(model) as TableOptions | undefined;
      const form = program.stateMap(stateKeys.form).get(model) as string | undefined ?? "internal sealed partial record";
      manifest.push({type:typeIdentity(model),project:output.directory,file:output.file,namespace:output.namespace,form,json:output.json ?? false,
        properties:[...walkPropertiesInherited(model)].map(p=> {const r=resolveProperty(program,p); return {code:r.codeName,json:r.jsonName,optional:r.optional,nullable:r.nullable,minimum:r.minimum,maximum:r.maximum,column:program.stateMap(stateKeys.column).get(p)};})});
      if (["public sealed record", "internal sealed record", "internal sealed partial positional record", "internal readonly record struct", "internal readonly partial record struct"].includes(form)) {
        body.push(`${form.replace(" positional", "")} ${model.name}(${[...walkPropertiesInherited(model)].map(property => {
          const type = mapType(program,property.type) + (property.optional && !mapType(program,property.type).endsWith("?") ? "?" : "");
          const value = defaultExpression(property) ?? (property.optional ? "null" : undefined);
          return `${type} ${pascal(property.name)}${value === undefined ? "" : " = " + value}`;
        }).join(", ")});`);
        continue;
      }
      if (form === "internal static class") {
        body.push(`${form} ${model.name}\n{`);
        for (const property of walkPropertiesInherited(model)) {
          const name = program.stateMap(stateKeys.environment).get(property);
          if (typeof name !== "string") throw new Error(`${model.name}.${property.name} requires an environment name`);
          body.push(`    public const string ${pascal(property.name)} = ${quote(name)};`);
        }
        body.push("}\n");
        continue;
      }
      if (!["internal sealed partial record", "internal sealed partial class", "public sealed partial record", "public sealed partial class", "internal abstract partial class", "internal closed partial class"].includes(form)) throw new Error(`Unsupported C# form ${form}`);
      const properties = [...walkPropertiesInherited(model)];
      if (table) {
        if (table.bootstrap && table.derived) throw new Error(`${table.name} cannot be both bootstrap and derived`);
        const orders = properties.filter(p => !program.stateMap(stateKeys.internalProperty).has(p)).map(p => (program.stateMap(stateKeys.column).get(p) as ColumnOptions | undefined)?.order);
        if (orders.some(o => o === undefined) || new Set(orders).size !== orders.length) throw new Error(`${typeIdentity(model)} requires distinct explicit column orders`);
        properties.sort((a,b) => ((program.stateMap(stateKeys.column).get(a) as ColumnOptions | undefined)?.order ?? Infinity) - ((program.stateMap(stateKeys.column).get(b) as ColumnOptions | undefined)?.order ?? Infinity));
        const conflict = conflictClause(program, table, properties);
        const args = attributes({...table, ...(conflict === undefined ? {} : {onConflict: conflict})}, ["name", "conflict"]);
        body.push(`[DuckDbTable(${quote(table.name)}${args ? ", " + args : ""})]`);
      }
      const discriminator = getDiscriminator(program, model);
      if (output.json && discriminator) {
        body.push(`[System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = ${quote(discriminator.propertyName)})]`);
        for (const candidate of models) {
          let parent = candidate.baseModel;
          while (parent && parent !== model) parent = parent.baseModel;
          const tag = [...walkPropertiesInherited(candidate)].find(p => p.name === discriminator.propertyName);
          if (parent === model && tag?.type.kind === "String") body.push(`[System.Text.Json.Serialization.JsonDerivedType(typeof(${candidate.name}), ${quote(tag.type.value)})]`);
        }
      }
      body.push(`${form} ${model.name}${model.baseModel ? " : " + mapType(program, model.baseModel) : ""}\n{`);
      for (const property of properties) {
        const resolved = resolveProperty(program, property);
        const internalProperty = program.stateMap(stateKeys.internalProperty).has(property);
        if (internalProperty) {
          if (program.stateMap(stateKeys.column).has(property)) throw new Error(`${typeIdentity(property)} cannot be both internal and a database column`);
          body.push("    [JsonIgnore]");
        }
        const configuredKey = program.stateMap(stateKeys.configuredBy).get(property) as ModelProperty | undefined;
        if (configuredKey) {
          if (!configuredKey.model || !program.stateMap(stateKeys.environment).has(configuredKey)) throw new Error(`${typeIdentity(configuredKey)} is not an environment key`);
          const key = `${mapType(program, configuredKey.model)}.${pascal(configuredKey.name)}`;
          body.push(`    [Microsoft.Extensions.Configuration.ConfigurationKeyName(${key})]`);
          if (resolved.minimum !== undefined || resolved.maximum !== undefined) {
            if (resolved.minimum === undefined || resolved.maximum === undefined) throw new Error(`${model.name}.${property.name} needs both bounds`);
            body.push(`    [Nami.Collector.Ingestion.Configuration.ConfiguredRange(Key = ${key}, Minimum = ${resolved.minimum}, Maximum = ${resolved.maximum})]`);
          }
        }
        let taggedBase = model.baseModel;
        let isTag = false;
        while (taggedBase) {
          if (getDiscriminator(program, taggedBase)?.propertyName === property.name) isTag = true;
          taggedBase = taggedBase.baseModel;
        }
        if (isTag) continue;
        const column = program.stateMap(stateKeys.column).get(property) as ColumnOptions | undefined;
        const override = program.stateMap(stateKeys.propertyType).get(property) as string | undefined;
        let type = override ?? mapType(program, property.type);
        if (resolved.nullable && !program.stateMap(stateKeys.slot).has(property.type) && !type.endsWith("?")) type += "?";
        if (property.optional && !table && !type.endsWith("?")) type += "?";
        if (column) {
          if (column.sequenceName && (!/^[a-z][a-z0-9_]*$/u.test(column.sequenceName) || column.defaultSql !== undefined)) throw new Error(`${model.name}.${property.name} requires a valid sequence name without an authored default`);
          const args = attributes({...column, ...(column.sequenceName ? {defaultSql: `nextval('${column.sequenceName}')`} : {})}, ["order", "privateInit", "merge"]);
          if (args) body.push(`    [DuckDbColumn(${args})]`);
        }
        if (output.json && resolved.encoding?.type.name === "string" && /^(long|ulong|IReadOnlyList<(long|ulong)>)[?]?$/u.test(type))
          body.push("    [System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString | System.Text.Json.Serialization.JsonNumberHandling.WriteAsString)]");
        if (output.json && namedFloat(property.type)) body.push("    [System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals)]");
        if (output.json && !property.optional && resolved.nullable) body.push("    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]");
        if (output.json) body.push(`    [System.Text.Json.Serialization.JsonPropertyName(${quote(resolved.jsonName)})]`);
        const fallback = defaultExpression(property);
        const setter = program.stateMap(stateKeys.setter).get(property) as string | undefined ?? (output.mutable ? "set" : "init");
        body.push(`    ${internalProperty ? "internal" : "public"} ${!property.optional && fallback === undefined && !column?.privateInit ? "required " : ""}${type} ${pascal(property.name)} { get; ${column?.privateInit ? "private " : ""}${setter}; }${fallback === undefined ? "" : " = " + fallback + ";"}`);
      }
      body.push("}\n");

    }
    for (const type of enums.get(output) ?? []) {
      const base = program.stateMap(stateKeys.enumType).get(type) as string | undefined ?? "int";
      const flags = program.stateMap(stateKeys.enumFlags).has(type);
      if (!["byte","sbyte","short","ushort","int","uint","long","ulong"].includes(base)) throw new Error(`Invalid enum representation ${base}`);
      body.push(`${flags ? "[Flags]\n" : ""}internal enum ${type.name} : ${base}\n{\n${[...type.members.values()].map((member,index)=>`    ${pascal(member.name)} = ${member.value ?? index},`).join("\n")}\n}`);
      if (flags) body.push(`internal static class ${type.name}Names\n{\n    public static bool TryParse(string? value, out ${type.name} result)\n    {\n        result = value switch\n        {\n${[...type.members.values()].filter(m => m.value !== 0).map(m => `            ${quote(m.name)} => ${type.name}.${pascal(m.name)},`).join("\n")}\n            _ => 0\n        };\n        return result != 0;\n    }\n}`);
      manifest.push({type:typeIdentity(type),project:output.directory,file:output.file,namespace:output.namespace,form:`enum:${base}`,json:false});
    }
    if (output.json && output.context) {
      const options: string[] = [];
      const numericRoots = models.filter(m => isArrayModelType(m) && namedFloat(m));
      if (numericRoots.length) {
        if (numericRoots.length !== models.length || mixedUnions.has(output)) throw new Error("Named-float array roots require a dedicated JSON context");
        options.push("NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals");
      }
      if (output.strictJson) options.push("UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow", "AllowDuplicateProperties = false", "RespectNullableAnnotations = true");
      if (options.length) body.push(`[JsonSourceGenerationOptions(${options.join(", ")})]`);
      body.push(`${[...models, ...mixedUnions.get(output) ?? []].map(m => `[JsonSerializable(typeof(${m.kind === "Model" && (isArrayModelType(m) || isRecordModelType(m)) ? mapType(program, m) : m.name}), TypeInfoPropertyName = ${quote(m.name)})]`).join("\n")}\ninternal partial class ${output.context} : JsonSerializerContext;`);
    }
    await emitFile(program, {path: recordOutput(program, resolve(program.projectRoot, "..", output.directory, output.file)), content:`// <auto-generated/>\n#nullable enable\nusing System;\nusing System.Collections.Generic;\nusing System.Linq;\nusing System.Text.Json;\nusing System.Text.Json.Serialization;\nnamespace ${output.namespace};\n\n${body.join("\n")}`});
  }
  await emitFile(program, {path: resolve(program.projectRoot,"generated/model-manifest.json"), content: JSON.stringify(manifest, null, 2) + "\n"});
}
