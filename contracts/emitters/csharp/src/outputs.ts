import { recordOutput } from "./semantics.js";
import { emitSlotValue } from "./slots.js";
import type { DecoratorContext, Enum, Model, ModelProperty, Namespace, Program, Type, Value, Scalar, Union } from "@typespec/compiler";
import { emitFile, getDiscriminator, navigateProgram, walkPropertiesInherited } from "@typespec/compiler";
import { resolve } from "node:path";
import { stateKeys } from "./lib.js";
import { isNamedFloatingPointUnion, mapType } from "./type-map.js";
import { resolveProperty, typeIdentity } from "./semantics.js";

export interface OutputOptions { directory: string; namespace: string; file: string; json?: boolean; context?: string; typescript?: boolean; schema?: boolean }
export interface TableOptions { name: string; indexes?: string; uniqueIndexes?: string; onConflict?: string; parameterSets?: string; derived?: boolean; appenderEligible?: boolean; arrowEligible?: boolean }
export interface ColumnOptions { order: number; sqlType?: string; primaryKeyOrdinal?: number; defaultSql?: string; excludeFromInsert?: boolean; privateInit?: boolean }
export function $csharpEnum(ctx: DecoratorContext, target: Enum, type: string): void { ctx.program.stateMap(stateKeys.enumType).set(target,type); }
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
function attributes(options: object, omit: string[]): string {
  return Object.entries(options).filter(([key]) => !omit.includes(key)).map(([key, value]) => `${pascal(key)} = ${quote(value)}`).join(", ");
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
  } });
  const slotUnions: Union[] = [];
  navigateProgram(program, { union(union) { if (program.stateMap(stateKeys.slot).has(union)) slotUnions.push(union); } });
  for (const union of slotUnions) await emitSlotValue(program, union);
  const manifest: object[] = slotUnions.map(type => ({type:typeIdentity(type),project:outputFor(program,type)!.directory,file:`${type.name}.g.cs`,namespace:outputFor(program,type)!.namespace,form:"typed-slots",json:false}));
  for (const [output, models] of groups) {
    const body: string[] = [];
    for (const model of models) {
      const table = program.stateMap(stateKeys.table).get(model) as TableOptions | undefined;
      const form = program.stateMap(stateKeys.form).get(model) as string | undefined ?? "internal sealed partial record";
      manifest.push({type:typeIdentity(model),project:output.directory,file:output.file,namespace:output.namespace,form,json:output.json ?? false,
        properties:[...walkPropertiesInherited(model)].map(p=> {const r=resolveProperty(program,p); return {code:r.codeName,json:r.jsonName,optional:r.optional,nullable:r.nullable,minimum:r.minimum,maximum:r.maximum,column:program.stateMap(stateKeys.column).get(p)};})});
      if (form === "internal sealed record" || form === "internal sealed partial positional record") {
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
        const orders = properties.map(p => (program.stateMap(stateKeys.column).get(p) as ColumnOptions | undefined)?.order);
        if (orders.some(o => o === undefined) || new Set(orders).size !== orders.length) throw new Error(`${typeIdentity(model)} requires distinct explicit column orders`);
        properties.sort((a,b) => (program.stateMap(stateKeys.column).get(a) as ColumnOptions).order - (program.stateMap(stateKeys.column).get(b) as ColumnOptions).order);
        const args = attributes(table, ["name"]);
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
          const args = attributes(column, ["order", "privateInit"]);
          if (args) body.push(`    [DuckDbColumn(${args})]`);
        }
        if (output.json && resolved.encoding?.type.name === "string" && /^(long|ulong|IReadOnlyList<(long|ulong)>)[?]?$/u.test(type))
          body.push("    [System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString | System.Text.Json.Serialization.JsonNumberHandling.WriteAsString)]");
        const namedFloat = (t: Type): boolean => {
          if (t.kind === "Model" && t.indexer) return namedFloat(t.indexer.value);
          if (t.kind !== "Union") return false;
          if (isNamedFloatingPointUnion(t)) return true;
          const members = [...t.variants.values()].map(v => v.type).filter(t => t.kind !== "Intrinsic" || t.name !== "null");
          return members.length === 1 && namedFloat(members[0]!);
        };
        if (output.json && namedFloat(property.type)) body.push("    [System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals)]");
        if (output.json && !property.optional && resolved.nullable) body.push("    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]");
        if (output.json) body.push(`    [System.Text.Json.Serialization.JsonPropertyName(${quote(resolved.jsonName)})]`);
        const fallback = defaultExpression(property);
        body.push(`    public ${!property.optional && fallback === undefined && !column?.privateInit ? "required " : ""}${type} ${pascal(property.name)} { get; ${column?.privateInit ? "private " : ""}init; }${fallback === undefined ? "" : " = " + fallback + ";"}`);
      }
      body.push("}\n");

    }
    for (const type of enums.get(output) ?? []) {
      const base = program.stateMap(stateKeys.enumType).get(type) as string | undefined ?? "int";
      if (!["byte","sbyte","short","ushort","int","uint","long","ulong"].includes(base)) throw new Error(`Invalid enum representation ${base}`);
      body.push(`internal enum ${type.name} : ${base}\n{\n${[...type.members.values()].map((member,index)=>`    ${pascal(member.name)} = ${member.value ?? index},`).join("\n")}\n}`);
      manifest.push({type:typeIdentity(type),project:output.directory,file:output.file,namespace:output.namespace,form:`enum:${base}`,json:false});
    }
    if (output.json && output.context) body.push(`${models.map(m => `[System.Text.Json.Serialization.JsonSerializable(typeof(${m.name}))]`).join("\n")}\ninternal partial class ${output.context} : System.Text.Json.Serialization.JsonSerializerContext;`);
    await emitFile(program, {path: recordOutput(program, resolve(program.projectRoot, "..", output.directory, output.file)), content:`// <auto-generated/>\n#nullable enable\nusing System;\nusing System.Collections.Generic;\nnamespace ${output.namespace};\n\n${body.join("\n")}`});
  }
  await emitFile(program, {path: resolve(program.projectRoot,"generated/model-manifest.json"), content: JSON.stringify(manifest, null, 2) + "\n"});
}
