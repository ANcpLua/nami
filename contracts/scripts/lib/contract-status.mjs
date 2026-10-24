import { createHash } from "node:crypto";
import { mkdir, readFile, readdir, rm, writeFile } from "node:fs/promises";
import { dirname, join, relative } from "node:path";
import { canonicalize } from "./canonical-schema.mjs";

export const generatedDirectories = [
  "internal/nami.contracts/Nami", "contracts/json-schema", "contracts/openapi",
  "src/nami.mcp/src/contracts/generated",
];
export const schemaPath = "internal/nami.contracts/Nami/Api/Contracts/contract-schemas.json";
const hash = (value) => createHash("sha256").update(value).digest("hex");
const equal = (left, right) => JSON.stringify(left) === JSON.stringify(right);
const ordered = (value) => Object.fromEntries(Object.entries(value).sort(([a], [b]) => a.localeCompare(b)));

async function filesIn(directory) {
  let entries;
  try { entries = await readdir(directory, { withFileTypes: true }); }
  catch (error) { if (error.code === "ENOENT") return []; throw error; }
  const result = [];
  for (const entry of entries) {
    if (entry.isSymbolicLink()) throw new Error(`Generated output contains a symbolic link: ${join(directory, entry.name)}`);
    if (entry.isDirectory()) result.push(...await filesIn(join(directory, entry.name)));
    else if (entry.isFile()) result.push(join(directory, entry.name));
  }
  return result.sort();
}

export async function snapshot(root) {
  const contents = new Map();
  const files = {};
  for (const directory of generatedDirectories) {
    for (const path of await filesIn(join(root, directory))) {
      const name = relative(root, path).replaceAll("\\", "/");
      const content = await readFile(path);
      contents.set(name, content);
      if (!name.endsWith("/.DS_Store")) files[name] = hash(content);
    }
  }
  const schemas = contents.has(schemaPath) ? canonicalize(JSON.parse(contents.get(schemaPath).toString())) : null;
  if (schemas !== null && (Array.isArray(schemas) || typeof schemas !== "object"))
    throw new Error("Generated contract schemas must be an object.");
  return { contents, files: ordered(files), schemas };
}

async function restore(root, original) {
  // Only generator-owned directories are restored. Source files are never written.
  for (const directory of generatedDirectories) await rm(join(root, directory), { recursive: true, force: true });
  for (const [name, content] of original.contents) {
    const path = join(root, name);
    await mkdir(dirname(path), { recursive: true });
    await writeFile(path, content);
  }
}

export function compareFiles(before, after) {
  return [...new Set([...Object.keys(before), ...Object.keys(after)])].sort().flatMap((path) =>
    before[path] === after[path] ? [] : [{ path, change: !(path in before) ? "added" : !(path in after) ? "removed" : "modified",
      beforeHash: before[path] ?? null, afterHash: after[path] ?? null }]);
}

function schemaChanges(before, after, path = "") {
  if (equal(before, after)) return [];
  if (before && after && typeof before === "object" && typeof after === "object" && !Array.isArray(before) && !Array.isArray(after)) {
    return [...new Set([...Object.keys(before), ...Object.keys(after)])].sort().flatMap((key) => {
      const next = `${path}/${key.replaceAll("~", "~0").replaceAll("/", "~1")}`;
      return !(key in before) ? [{ path: next, change: "added", after: after[key] }] : !(key in after)
        ? [{ path: next, change: "removed", before: before[key] }] : schemaChanges(before[key], after[key], next);
    });
  }
  return [{ path: path || "/", change: "modified", before, after }];
}

export function compareSchemas(before, after) {
  if (before === null || after === null) return { status: "UNAVAILABLE", changes: [], compatibility: "NOT_ASSESSED", quality: "NOT_ASSESSED" };
  const changes = schemaChanges(before, after);
  const kinds = new Set(changes.map((change) => change.change));
  const status = kinds.size === 0 ? "UNCHANGED" : kinds.size > 1 ? "MIXED"
    : kinds.has("added") ? "ADDITIONS" : kinds.has("removed") ? "REMOVALS" : "MODIFICATIONS";
  return { status, changes, compatibility: "NOT_ASSESSED", quality: "NOT_ASSESSED" };
}

export async function verifyContracts({ root, state, mode = "verify", generate, baselineSchemas = null, sourceFingerprint }) {
  await rm(state, { recursive: true, force: true });
  await mkdir(state, { recursive: true });
  const report = { status: "RUNNING", lane: "contracts", exitCode: null, mode, problems: [] };
  if (sourceFingerprint && mode === "verify") report.context = { source: sourceFingerprint };
  await writeFile(join(state, "summary.json"), JSON.stringify(report, null, 2) + "\n");
  let before;
  try {
    before = await snapshot(root);
    await writeFile(join(state, "before.json"), JSON.stringify(before.files, null, 2) + "\n");
    await generate();
    const first = await snapshot(root);
    if (first.schemas === null) throw new Error("Generator did not produce contract-schemas.json.");
    await generate();
    const second = await snapshot(root);
    if (second.schemas === null) throw new Error("Generator did not produce contract-schemas.json.");
    report.fileChanges = compareFiles(before.files, second.files);
    report.reproducibilityChanges = compareFiles(first.files, second.files);
    report.wire = compareSchemas(baselineSchemas, second.schemas);
    report.generatedWire = compareSchemas(before.schemas, second.schemas);
    await writeFile(join(state, "expected.json"), JSON.stringify(second.files, null, 2) + "\n");
    report.status = report.reproducibilityChanges.length ? "NONDETERMINISTIC"
      : mode === "generate" ? "GENERATED" : report.fileChanges.length ? "GENERATION_REQUIRED" : "CURRENT";
    report.exitCode = report.status === "NONDETERMINISTIC" ? 3 : report.status === "GENERATION_REQUIRED" ? 1 : 0;
    if (mode === "verify" || report.exitCode !== 0) await restore(root, before);
  } catch (error) {
    report.status = "GENERATION_FAILED";
    report.exitCode = 2;
    report.problems.push(error.message);
    report.problems.push(...(error.diagnostics ?? []).slice(0, 5));
    if (Number.isInteger(error.exitCode)) report.generatorExitCode = error.exitCode;
    if (before) {
      try { await restore(root, before); }
      catch (restoreError) { report.problems.push(`Restoration failed: ${restoreError.message}`); }
    }
  }
  await writeFile(join(state, "summary.json"), JSON.stringify(report, null, 2) + "\n");
  return report;
}
