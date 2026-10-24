import { mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { pathToFileURL } from "node:url";

const [scenario, root, state, modulePath] = process.argv.slice(2);
const { canonicalize } = await import(pathToFileURL(join(dirname(modulePath), "canonical-schema.mjs")));
const { schemaPath, verifyContracts } = await import(pathToFileURL(modulePath));
const original = { Widget: { type: "object", properties: { name: { type: "string" } }, required: ["name"] } };
const schemaFile = join(root, schemaPath);
const codeFile = join(root, "internal/nami.contracts/Nami/original.cs");
const extraFile = join(root, "internal/nami.contracts/Nami/added.cs");
await mkdir(dirname(schemaFile), { recursive: true });
await writeFile(schemaFile, JSON.stringify(original));
await writeFile(codeFile, "original source\n");
if (scenario === "metadata") await writeFile(join(dirname(codeFile), ".DS_Store"), "finder metadata");
let calls = 0;
const report = await verifyContracts({ root, state,
  mode: scenario === "generate" ? "generate" : "verify",
  baselineSchemas: scenario === "untracked" ? null : canonicalize(original),
  sourceFingerprint: scenario === "stale" ? "before" : undefined,
  generate: async () => {
    calls++;
    if (scenario === "failed") {
      await rm(codeFile);
      throw Object.assign(new Error("Intentional fixture generation failure"), { exitCode: 17 });
    }
    const next = structuredClone(original);
    if (["add", "generate", "mixed"].includes(scenario)) {
      next.Widget.properties.count = { type: "integer" };
      await writeFile(extraFile, "added source\n");
    }
    if (scenario === "remove") {
      delete next.Widget;
      await rm(codeFile, { force: true });
    }
    if (scenario === "mixed") delete next.Widget.properties.name;
    if (scenario === "modify") next.Widget.properties.name.type = "integer";
    if (scenario === "docs") next.Widget.description = "New explanatory documentation";
    if (scenario === "nondeterministic") next.Widget.properties.name.maxLength = calls;
    if (scenario === "metadata") await rm(join(dirname(codeFile), ".DS_Store"), { force: true });
    await writeFile(schemaFile, JSON.stringify(next));
  } });
const restored = await readFile(schemaFile, "utf8") === JSON.stringify(original)
  && await readFile(codeFile, "utf8").catch(() => null) === "original source\n"
  && await readFile(extraFile, "utf8").catch(() => null) === null;
console.log(JSON.stringify({ report, restored, calls }));
