import assert from "node:assert/strict";
import test from "node:test";
import { mkdtemp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { compile, NodeHost } from "@typespec/compiler";
import { emitModelProjections } from "../../emitters/csharp/dist/projections.js";

const library = fileURLToPath(new URL("../../emitters/csharp/lib/main.tsp", import.meta.url));
async function fixture(extra, check) {
  const workspace = await mkdtemp(join(tmpdir(), "nami-row-projection-"));
  try {
    const directory = join(workspace, "contracts");
    await mkdir(directory);
    await writeFile(join(directory, "main.tsp"), `import ${JSON.stringify(library)};
using Nami.Api.Schema.Emit.CSharp;
@csharpOutput(#{directory:"src/probe/Generated", \`namespace\`:"Probe", file:"Models.g.cs"}) namespace Probe;
model Source { id: string; }
model Computed { duration: uint64; ${extra.computed ?? ""} }
@modelProjection(Source, Computed)
model Row { id: string; duration: uint64; ${extra.row ?? ""} }
${extra.declarations ?? ""}
`);
    const program = await compile(NodeHost, join(directory,"main.tsp"), {noEmit:true});
    assert.deepEqual(program.diagnostics.map(d => d.message), []);
    program.compilerOptions.noEmit = false;
    await check(program,workspace);
  } finally { await rm(workspace,{recursive:true,force:true}); }
}
test("row mapping consumes source and computed values in its configured project", () => fixture({}, async (program, workspace) => {
  await emitModelProjections(program);
  const source = await readFile(join(workspace,"src/probe/Generated/ModelMappings.g.cs"),"utf8");
  assert.match(source,/namespace Probe;/u);
  assert.match(source,/Id = source.Id/u);
  assert.match(source,/Duration = computed.Duration/u);
}));
test("adding even an optional row field without an input fails generation", () => fixture({row:"unmapped?: string;"}, async program => {
  await assert.rejects(emitModelProjections(program),/Row has no input for unmapped/u);
}));
test("obsolete computed fields fail generation instead of silently being discarded", () => fixture({computed:"obsolete?: string;"}, async program => {
  await assert.rejects(emitModelProjections(program),/unused row inputs: obsolete/u);
}));
test("namespace-owned mapping uses explicit source roles without a computed object", () => fixture({declarations:`
model EntitySource { identity: string; }
@modelProjection(EntitySource, Probe) model EntityTarget { id: string; }
@@projectionSource(EntityTarget.id, EntitySource.identity);
`}, async (program, workspace) => {
  await emitModelProjections(program);
  const source = await readFile(join(workspace,"src/probe/Generated/ModelMappings.g.cs"),"utf8");
  assert.match(source,/Probe.EntityTarget ToModel\(Probe.EntitySource source\)/u);
  assert.match(source,/Id = source.Identity/u);
}));
test("same-named property from another source model cannot satisfy a mapping", () => fixture({declarations:`
model OtherSource { id: string; }
@@projectionSource(Row.id, OtherSource.id);
`}, async program => {
  await assert.rejects(emitModelProjections(program),/Row.id maps a property outside Source/u);
}));
