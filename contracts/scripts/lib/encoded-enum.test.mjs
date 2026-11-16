import assert from "node:assert/strict";
import test from "node:test";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { compile, NodeHost } from "@typespec/compiler";
import { $onEmit as emitCsharp } from "../../emitters/csharp/dist/emitter.js";
import { $onEmit as emitTypescript } from "../../emitters/ts-types/dist/index.js";

const library = fileURLToPath(new URL("../../emitters/csharp/lib/main.tsp", import.meta.url));

// Since TypeSpec 1.18.0 an @encodedName on an enum member sets the value OpenAPI and JSON Schema
// publish; the in-repo emitters must write the same value, or C# output fails nami's schema check.
async function emit(source) {
  const directory = await mkdtemp(join(tmpdir(), "nami-encoded-enum-"));
  try {
    await writeFile(join(directory, "main.tsp"), `import ${JSON.stringify(library)};
using Nami.Api.Schema.Emit.CSharp;
@csharpNamespace("Probe") namespace Nami.Api.Contracts.Probe;
${source}
model Report { status: Status; }
`);
    const program = await compile(NodeHost, join(directory, "main.tsp"), { noEmit: true });
    const errors = program.diagnostics.map((d) => d.message);
    program.compilerOptions.noEmit = false;
    const out = join(directory, "out");
    const failures = [];
    for (const emit of [emitCsharp, emitTypescript]) {
      try { await emit({ program, emitterOutputDir: out, options: {} }); } catch (error) { failures.push(error.message); }
    }
    const read = async (path) => readFile(join(out, path), "utf8").catch(() => "");
    return { errors, failures, csharp: await read("Probe.cs"), typescript: await read("api.ts") };
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
}

test("an encoded enum member keeps its published value in C# and TypeScript", async () => {
  const { errors, failures, csharp, typescript } = await emit(`enum Status { healthy: "healthy", @encodedName("application/json", "partly-degraded") degraded: "degraded" }
model Pinned { status: Status.degraded; }`);
  assert.deepEqual([...errors, ...failures], []);
  assert.match(csharp, /\[JsonStringEnumMemberName\("partly-degraded"\)\]\n    Degraded/u);
  assert.match(csharp, /public const string Degraded = "partly-degraded";/u);
  assert.doesNotMatch(csharp, /"degraded"/u);
  assert.match(typescript, /"degraded": "partly-degraded"/u);
  // A property typed as one member is that member's value as a literal type.
  assert.match(typescript, /interface Pinned \{\n  "status": "partly-degraded";/u);
});

test("an enum whose JSON values mix numbers and strings is refused", async () => {
  const { failures } = await emit(`enum Status { @encodedName("application/json", "ready") ready: 1, busy: 2 }`);
  assert.equal(failures.length, 1);
  assert.match(failures[0], /mixes number and string JSON values/u);
});
