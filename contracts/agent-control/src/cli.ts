import { resolve } from "node:path";
import { applyArtifactPlan, diagnoseArtifacts, planArtifacts, planTypeSpecFix, provePolicy, type Result } from "./control.js";
import { createDiagnostic } from "./index.js";
import { NoTarget } from "@typespec/compiler";
import { getCodeFixes, getTypeInfo } from "./query.js";

const args = process.argv.slice(2);
const rootAt = args.indexOf("--root");
let root = process.cwd();
try {
  if (rootAt >= 0) {
    if (!args[rootAt + 1]) throw new Error("--root requires a path");
    root = resolve(args[rootAt + 1]); args.splice(rootAt, 2);
  }
  if (args[0] === "type-info") {
    if (!args[1]) throw new Error("type-info requires a qualified TypeSpec type name");
    const [info, diagnostics] = await getTypeInfo(resolve(root, "contracts/main.tsp"), args[1]);
    if (info) console.log(info.content);
    for (const diagnostic of diagnostics.slice(0, 20)) console.log(`${diagnostic.severity} ${diagnostic.code}: ${diagnostic.message}`);
    process.exitCode = diagnostics.some(d => d.severity === "error") || !info ? 2 : 0;
  } else if (args[0] === "codefixes") {
    const entrypoint = args[1] ? resolve(root, args[1]) : resolve(root, "contracts/main.tsp");
    const diagnostics = await getCodeFixes(entrypoint);
    for (const diagnostic of diagnostics.slice(0, 20)) {
      console.log(`${diagnostic.code}: ${diagnostic.message}`);
      for (const fix of diagnostic.fixes) {
        console.log(`  fix ${fix.id}: ${fix.label}`);
        for (const edit of fix.edits) console.log(`    ${edit.file.path} offset=${edit.pos} ${edit.kind}: ${JSON.stringify(edit.text)}`);
      }
    }
  } else if (args[0] === "proof") {
    console.log(await provePolicy());
  } else {
    let result: Result;
    switch (args[0]) {
      case "diagnose": result = await diagnoseArtifacts(root); break;
      case "plan": result = await planArtifacts(root); break;
      case "tsp-plan":
        if (args.length !== 4) throw new Error("tsp-plan requires ENTRYPOINT DIAGNOSTIC_CODE FIX_ID");
        result = await planTypeSpecFix(root, args[1], args[2], args[3]); break;
      case "apply":
        if (!args[1]) throw new Error("apply requires the exact plan ID");
        result = await applyArtifactPlan(root, args[1]); break;
      default: throw new Error("Usage: agent proof | diagnose | plan | apply PLAN_ID [--root PATH]");
    }
    console.log(`AGENT ${result.phase}: exit=${result.exitCode}`);
    if (result.plan) {
      console.log(`Plan: ${result.plan.id}`);
      console.log(`Owned targets: ${result.plan.edit ? 1 : result.plan.inventory.artifacts.length}`);
      if (result.plan.edit) console.log(`  ${result.plan.edit.path}: ${result.plan.edit.fixId}`);
      if (result.phase === "verified") console.log(result.plan.edit
        ? "Validation: TypeSpec source/noEmit. Generated outputs require the normal Contracts gate."
        : "Validation: owned artifact inventory and protected source fingerprints.");
      for (const target of result.plan.inventory.artifacts.slice(0, 20)) console.log(`  ${target.path} (${target.bytes} bytes)`);
    }
    for (const diagnostic of result.diagnostics.slice(0, 20)) console.log(`${diagnostic.severity.toUpperCase()} ${diagnostic.code}: ${diagnostic.message}`);
    process.exitCode = result.exitCode;
  }
} catch (error) {
  const diagnostic = createDiagnostic({ code: "setup-failed", target: NoTarget,
    format: { detail: error instanceof Error ? error.message : String(error) } });
  console.error(`ERROR ${diagnostic.code}: ${diagnostic.message}`);
  process.exitCode = 2;
}
