import assert from "node:assert/strict";
import test from "node:test";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { compile, NodeHost } from "@typespec/compiler";
import { $onEmit } from "../../emitters/csharp/dist/emitter.js";

const library = fileURLToPath(new URL("../../emitters/csharp/lib/main.tsp", import.meta.url));

// A tagged union whose cases share an envelope, and one whose cases share only the tag and a
// same-named member of different types.
async function emitted() {
  const directory = await mkdtemp(join(tmpdir(), "nami-tagged-union-"));
  try {
    await writeFile(join(directory, "main.tsp"), `import ${JSON.stringify(library)};
using Nami.Api.Schema.Emit.CSharp;
@csharpNamespace("Probe") namespace Probe;
alias Envelope = { id: string; revision: string; };
model Run { ...Envelope; method: "run"; name: string; }
model Stop { ...Envelope; method: "stop"; reason?: string; }
@discriminated(#{ envelope: "none", discriminatorPropertyName: "method" })
union Command { run: Run, stop: Stop }
model Whole { kind: "whole"; value: int64; }
model Part { kind: "part"; value: float64; }
@discriminated(#{ envelope: "none", discriminatorPropertyName: "kind" })
union Amount { whole: Whole, part: Part }
`);
    const program = await compile(NodeHost, join(directory, "main.tsp"), { noEmit: true });
    assert.deepEqual(program.diagnostics.map((d) => d.message), []);
    program.compilerOptions.noEmit = false;
    await $onEmit({ program, emitterOutputDir: join(directory, "out"), options: {} });
    return await readFile(join(directory, "out/Probe.cs"), "utf8");
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
}

function unionBody(source, name) {
  const start = source.indexOf(`public readonly union ${name}(`);
  assert.ok(start >= 0, `union ${name} is declared`);
  return source.slice(start, source.indexOf("\n}\n", start) + 2);
}

test("a tagged union is read by a tag classifier, not by a converter that buffers the object", async () => {
  const source = await emitted();
  assert.doesNotMatch(source, /JsonConverter<Command>|JsonDocument\.ParseValue/u);
  assert.match(source, /\[JsonUnion\(TypeClassifier = typeof\(Command\.Classifier\)\)\]\npublic readonly union Command\(Run, Stop\)\n\{/u);
  const command = unionBody(source, "Command");
  assert.match(command, /WireTag\.TrySeek\(ref reader, "method"u8\)/u);
  assert.match(command, /if \(reader\.ValueTextEquals\("run"u8\)\) return typeof\(Run\);/u);
  assert.match(command, /if \(reader\.ValueTextEquals\("stop"u8\)\) return typeof\(Stop\);/u);
});

test("members every case declares alike are lifted onto the union, the tag included", async () => {
  const command = unionBody(await emitted(), "Command");
  const lifted = [...command.matchAll(/public (\S+) (\w+) => this switch/gu)].map((match) => `${match[1]} ${match[2]}`);
  assert.deepEqual(lifted, ["string Id", "string Revision", "string Method"]);
  assert.match(command, /Run variant => variant\.Id,\n\s+Stop variant => variant\.Id,\n\s+null => throw new InvalidOperationException\("An empty Command has no Id\."\)/u);
});

test("a member whose type differs between cases is not lifted", async () => {
  const amount = unionBody(await emitted(), "Amount");
  const lifted = [...amount.matchAll(/public (\S+) (\w+) => this switch/gu)].map((match) => match[2]);
  assert.deepEqual(lifted, ["Kind"]);
});
