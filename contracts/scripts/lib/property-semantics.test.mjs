import assert from "node:assert/strict";
import test from "node:test";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { compile, NodeHost } from "@typespec/compiler";
import { resolveProperty, typeIdentity } from "../../emitters/csharp/dist/semantics.js";
import { renderSerialization } from "../../emitters/ts-types/dist/serialization.js";
import { $onEmit } from "../../emitters/csharp/dist/emitter.js";
const library = fileURLToPath(new URL("../../emitters/csharp/lib/main.tsp", import.meta.url));
async function fixture(run) {
  const directory = await mkdtemp(join(tmpdir(), "nami-semantics-"));
  try {
    await writeFile(join(directory,"main.tsp"), `import ${JSON.stringify(library)};
using Nami.Api.Schema.Emit.CSharp;
@csharpNamespace("Probe") namespace Probe;
@encode(string) @minValue(-9223372036854775808) @maxValue(9223372036854775807) scalar Exact extends int64;
model Envelope { @encodedName("application/json", "wire_value") value?: Exact | null; values: Record<Exact[]>; }
namespace Left { model Same { value: string; } }
namespace Right { model Same { value: string; } }
model Box<T> { value: T; }
model Containers { values?: Record<(Exact | null)[]> | null; }
model Pair { left: Box<Left.Same>; right: Box<Right.Same>; }
`);
    const program = await compile(NodeHost, join(directory,"main.tsp"), {noEmit:true});
    assert.deepEqual(program.diagnostics.map(d=>d.message),[]);
    await run(program,program.getGlobalNamespaceType().namespaces.get("Probe"),directory);
  } finally { await rm(directory,{recursive:true,force:true}); }
}
test("one property retains presence, null, wire name and exact Int64 endpoints",()=>fixture(async(program,ns)=>{
  const property=resolveProperty(program,ns.models.get("Envelope").properties.get("value"));
  assert.equal(property.jsonName,"wire_value"); assert.equal(property.optional,true); assert.equal(property.nullable,true);
  assert.equal(property.encoding.type.name,"string");
  assert.equal(property.minimum,"-9223372036854775808"); assert.equal(property.maximum,"9223372036854775807");
}));
test("template identity includes the namespaces of its arguments",()=>fixture(async(program,ns)=>{
  const pair=ns.models.get("Pair");
  assert.notEqual(typeIdentity(pair.properties.get("left").type),typeIdentity(pair.properties.get("right").type));
}));
test("emission composes optional nullable presence with the exact integer codec",()=>fixture(async(program,ns,directory)=>{
  program.compilerOptions.noEmit=false;
  await $onEmit({program,emitterOutputDir:join(directory,"out"),options:{}});
  const source=await readFile(join(directory,"out/Probe.cs"),"utf8");
  const declarations = [...source.matchAll(/public sealed class (\w+)/gu)].map(match => match[1]);
  assert.equal(declarations.length, 7);
  assert.equal(new Set(declarations).size, declarations.length);
  assert.match(source,/OptionalNullableValueConverter<long, global::Nami.Api.Contracts.Int64StringConverter>/u);
  assert.match(source,/EncodedMapConverter<IReadOnlyList<long\?>/u);
  assert.match(source,/OptionalConvertedValueConverter</u);
  assert.match(source,/ValueMaximum = 9223372036854775807;/u);
  assert.doesNotMatch(source,/ValueMaximum = 9223372036854776000/u);
}));

test("generated codec retains decimal strings through nullable values, arrays and data map keys",()=>fixture(async(program,ns,directory)=>{
  const source=renderSerialization(program,new Map([["Envelope",ns.models.get("Envelope")]]),()=>"string");
  const path=join(directory,"codec.ts"); await writeFile(path,source);
  const {EnvelopeJson}=await import(pathToFileURL(path).href);
  const value={value:"9223372036854775807",values:{type:["-9223372036854775808"],kind:["0"]}};
  assert.deepEqual(EnvelopeJson.fromJson(EnvelopeJson.toJson(value)),value);
  assert.deepEqual(EnvelopeJson.toJson({value:null,values:{}}),{wire_value:null,values:{}});
  assert.deepEqual(EnvelopeJson.toJson({values:{}}),{values:{}});
  assert.throws(()=>EnvelopeJson.toJson({value:9223372036854775807,values:{}}),TypeError);
  assert.throws(()=>EnvelopeJson.toJson({value:"9223372036854775808",values:{}}),RangeError);
}));
