import type { Program, Scalar, Type } from "@typespec/compiler";
import { emitFile, getMaxValueAsNumeric, getMinValueAsNumeric, navigateProgram } from "@typespec/compiler";
import { effectiveEncoding, nonNullType, resolveProperty } from "./semantics.js";
import { schemaName } from "./tool-metadata.js";
function kind(type: Type): string | undefined {
  type = nonNullType(type);
  if (type.kind !== "Scalar") return;
  for (let scalar: Scalar | undefined = type; scalar; scalar=scalar.baseScalar)
    if (scalar.name === "int64" || scalar.name === "uint64") return scalar.name;
}
export async function emitExactSchema(program: Program): Promise<void> {
  const scalars: Record<string,object> = {}, properties: Record<string,Record<string,object>> = {};
  navigateProgram(program, {
    scalar(scalar) {
      if (effectiveEncoding(program,scalar)?.type.name !== "string" || !kind(scalar)) return;
      let minimum: string | undefined, maximum: string | undefined;
      for(let current: Scalar | undefined=scalar;current;current=current.baseScalar) {
        minimum ??= getMinValueAsNumeric(program,current)?.toString();
        maximum ??= getMaxValueAsNumeric(program,current)?.toString();
      }
      scalars[schemaName(scalar)]={kind:kind(scalar),minimum,maximum};
    },
    model(model) {
      const fields: Record<string,object>={};
      for(const property of model.properties.values()) {
        const resolved=resolveProperty(program,property), base=kind(property.type);
        if(resolved.encoding?.type.name!=="string" || !base) continue;
        fields[resolved.jsonName]={kind:base,minimum:resolved.minimum,maximum:resolved.maximum,
          default:property.defaultValue?.valueKind==="NumericValue" ? property.defaultValue.value.toString() : undefined};
      }
      if(Object.keys(fields).length) properties[schemaName(model)]=fields;
    }
  });
  await emitFile(program,{path:`${program.projectRoot}/generated/exact-schema.json`,content:JSON.stringify({scalars,properties},null,2)+"\n"});
}
