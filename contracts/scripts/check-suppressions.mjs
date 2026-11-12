// The suppression gate: fails when a `#suppress` directive is no longer needed.
//
// A suppression carries a known finding until its cause is fixed. This script
// makes sure it does not outlive the cause. It is built on `getSuppressions`
// from TypeSpec 1.18.0, whose `used` flag is a snapshot of one compilation: a
// directive also reads as unused when its rule was disabled, its emitter was
// skipped or the compilation stopped early. So this run is made complete, and
// the answer is refused whenever it is not:
//
//   - the ruleset is the one in tspconfig.yaml, with no command-line override
//     (this script takes no `--linter-rule`, `--linter-ruleset` or `--no-linter`)
//   - a rule that tspconfig.yaml disables makes its suppressions "undecidable",
//     which fails the gate (read with `collectLinterDisables`, also 1.18.0)
//   - every configured emitter runs, against a host that discards its writes,
//     so emitter diagnostics are matched and no file is touched
//   - any compilation error ends the run with exit code 2: nothing is judged
//
// Usage: node scripts/check-suppressions.mjs [--list] [--fix]
//   --list  also print every suppression with its state, rule and reason
//   --fix   delete the unused directives (never the undecidable ones)
// Exit code: 0 clean, 1 unused or undecidable suppressions, 2 the check could not run.
import { readFile, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { NodeHost, collectLinterDisables, compile, formatDiagnostic, resolveCompilerOptions } from "@typespec/compiler";
import { auditSuppressions, formatSuppressionAudit, unusedSuppressionEdits } from "@ancplua/typespec-nami-lint";

const contracts = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const flags = new Set(process.argv.slice(2));
for (const flag of flags) {
  if (flag !== "--list" && flag !== "--fix") {
    console.error(`check-suppressions: unknown argument '${flag}'. Use --list or --fix.`);
    process.exit(2);
  }
}

// Paths are printed relative to the contract directory so the output is the same on every machine.
const relative = (text) => text.replaceAll(`${contracts.replaceAll("\\", "/")}/`, "");
const fail = (message, diagnostics = []) => {
  for (const diagnostic of diagnostics) console.error(relative(formatDiagnostic(diagnostic)));
  console.error(`check-suppressions: ${message}`);
  process.exit(2);
};

const [options, optionDiagnostics] = await resolveCompilerOptions(NodeHost, {
  cwd: contracts,
  entrypoint: resolve(contracts, "main.tsp"),
  overrides: {},
});
if (optionDiagnostics.some((diagnostic) => diagnostic.severity === "error")) fail("tspconfig.yaml could not be read.", optionDiagnostics);
if (!options.linterRuleSet) fail("tspconfig.yaml configures no linter ruleset, so no suppression can be judged.");

const [disables, disableDiagnostics] = collectLinterDisables(await readFile(resolve(contracts, "tspconfig.yaml"), "utf8"));
if (disables === undefined) fail("the linter.disable section of tspconfig.yaml could not be read.", disableDiagnostics);

// Emitters run so that their diagnostics count, but nothing they write reaches the disk.
const discardingHost = { ...NodeHost, writeFile: async () => {}, mkdirp: async (path) => path, rm: async () => {} };
const program = await compile(discardingHost, resolve(contracts, "main.tsp"), { ...options, noEmit: false, warningAsError: false });
const errors = program.diagnostics.filter((diagnostic) => diagnostic.severity === "error");
if (errors.length > 0) fail("the contract does not compile, so later stages did not run and no suppression can be judged.", errors);

const audit = auditSuppressions(program, { disabledRules: disables.map((disable) => disable.code) });
console.log(formatSuppressionAudit(audit, { list: flags.has("--list") }));

if (flags.has("--fix") && audit.unused > 0) {
  const paths = [...new Set(audit.records.filter((record) => record.state === "unused").map((record) => record.path))].sort();
  for (const path of paths) {
    let text = await readFile(path, "utf8");
    for (const edit of unusedSuppressionEdits(text, audit.records.filter((record) => record.path === path))) {
      text = text.slice(0, edit.pos) + text.slice(edit.end);
    }
    await writeFile(path, text);
  }
  console.log(`Removed ${audit.unused} unused suppression(s) from ${paths.length} file(s). Run the gate again.`);
  process.exit(audit.undecidable > 0 ? 1 : 0);
}
process.exit(audit.clean ? 0 : 1);
