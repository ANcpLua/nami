import { appendFile, readFile } from "node:fs/promises";
import { spawnSync } from "node:child_process";
import { isAbsolute, join, relative, resolve } from "node:path";
import { canonicalize } from "./lib/canonical-schema.mjs";
import { schemaPath, verifyContracts } from "./lib/contract-status.mjs";

const args = process.argv.slice(2);
const mode = args.shift() ?? "verify";
const option = (name, fallback) => { const index = args.indexOf(name); return index < 0 ? fallback : args[index + 1]; };
const root = resolve(option("--root", "."));
const state = resolve(option("--state", "."));
const limit = Number(option("--limit", "20"));
const offset = Number(option("--offset", "0"));

function display(report) {
  const files = report.fileChanges ?? [];
  console.log(`CONTRACT ${report.status} exit=${report.exitCode}; files +${files.filter(c => c.change === "added").length} -${files.filter(c => c.change === "removed").length} ~${files.filter(c => c.change === "modified").length}; wire=${report.wire?.status ?? "UNAVAILABLE"}`);
  if (report.wire?.changes.length) console.log("Compatibility and quality: NOT_ASSESSED. Additions can also tighten validation.");
  for (const problem of report.problems ?? []) console.log("  " + String(problem).replace(/\s+/g, " ").slice(0, 500));
  const changes = [...files.map(c => ({ ...c, kind: "file" })), ...(report.wire?.changes ?? []).map(c => ({ ...c, kind: "wire" })),
    ...(report.reproducibilityChanges ?? []).map(c => ({ ...c, kind: "nondeterministic" }))];
  for (const change of changes.slice(offset, offset + limit)) console.log(`  ${change.kind} ${change.change}: ${change.path.replace(/\s+/g, " ")}`);
  if (changes.length > offset + limit) console.log(`  More: ./eng/contracts.sh status --offset ${offset + limit} --limit ${limit}`);
  if (report.status === "GENERATION_REQUIRED") console.log("  Action: ./eng/build.sh Contracts; review and commit the generated changes.");
  if (report.status === "NONDETERMINISTIC") console.log("  Action: fix the generator; two consecutive runs produced different output.");
}

try {
  const inside = relative(root, state);
  if (!inside || (!inside.startsWith("..") && !isAbsolute(inside))) throw new Error("Contract diagnostic state must be outside the repository.");
  if (!Number.isInteger(limit) || limit < 1 || limit > 200 || !Number.isInteger(offset) || offset < 0)
    throw new Error("Use --limit 1..200 and --offset >= 0.");
  if (mode === "status") {
    const report = JSON.parse(await readFile(join(state, "summary.json"), "utf8"));
    if (report.context?.source && option("--source") !== report.context.source) {
      report.problems.push(`Recorded status was ${report.status}; sources have changed. Run verify again.`);
      report.status = "STALE";
      report.exitCode = 2;
    }
    display(report);
    process.exitCode = report.exitCode;
  } else {
    if (!["verify", "generate"].includes(mode)) throw new Error("Use verify, generate or status.");
    const baseline = spawnSync("git", ["show", `HEAD:${schemaPath}`], { cwd: root, encoding: "utf8" });
    const baselineSchemas = baseline.status === 0 ? canonicalize(JSON.parse(baseline.stdout)) : null;
    const bun = option("--bun", "bun");
    let installed = false;
    const report = await verifyContracts({ root, state, mode, baselineSchemas, sourceFingerprint: option("--source"), generate: async () => {
      const commands = installed ? [["run", "compile"]] : [["install", "--frozen-lockfile"], ["run", "compile"]];
      installed = true;
      for (const command of commands) {
        const result = spawnSync(bun, command, { cwd: join(root, "contracts"), encoding: "utf8", timeout: 120000, maxBuffer: 8 * 1024 * 1024 });
        await appendFile(join(state, "generation.log"), `${bun} ${command.join(" ")}\n${result.stdout ?? ""}${result.stderr ?? ""}\n`);
        if (result.error || result.status !== 0) {
          const error = new Error(`Generator failed: ${command.join(" ")} (exit ${result.status ?? "unavailable"}${result.signal ? `, signal ${result.signal}` : ""}).${result.error ? ` ${result.error.message}` : ""}`);
          error.exitCode = result.status;
          error.diagnostics = `${result.stdout ?? ""}\n${result.stderr ?? ""}`.replace(/\x1b\[[0-9;]*m/g, "")
            .split(/\r?\n/).filter(line => /\berror\b/i.test(line)).slice(0, 5).map(line => line.slice(0, 500));
          throw error;
        }
      }
    } });
    display(report);
    process.exitCode = report.exitCode;
  }
} catch (error) {
  console.error(`CONTRACT SETUP_FAILED exit=2: ${error.message}`);
  process.exitCode = 2;
}
