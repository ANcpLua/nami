// Failure-injection probe. The MSTest caller asserts the real files afterwards.
import { rm, cp } from "node:fs/promises";
import { applyArtifactPlan } from "../../../contracts/agent-control/src/control.ts";
let calls = 0;
const native = process.argv[4] === "compiler";
const result = await applyArtifactPlan(process.argv[2], process.argv[3], {
  copy: (source, destination) => cp(source, destination, { recursive: true, dereference: false }),
  remove: async (path, directory) => {
    if (!native && ++calls === 2) throw new Error("injected filesystem failure");
    await rm(path, { recursive: directory, force: false });
  },
  validateTypeSpec: native ? async () => false : undefined,
});
console.log(JSON.stringify({ phase: result.phase, exitCode: result.exitCode }));
