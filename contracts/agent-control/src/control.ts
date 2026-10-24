import { createHash } from "node:crypto";
import { execFile, execFileSync } from "node:child_process";
import { promisify } from "node:util";
import { cp, lstat, mkdir, readFile, realpath, rename, rm, writeFile } from "node:fs/promises";
import { dirname, isAbsolute, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { applyCodeFix, NoTarget, NodeHost, type CodeFix, type Diagnostic } from "@typespec/compiler";
import { createDiagnostic } from "./index.js";
import { resolveOfferedFix, validateFixed } from "./query.js";

const exec = promisify(execFile);
const toolRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../../..");
const hash = (value: string) => createHash("sha256").update(value).digest("hex");
type Artifact = { path: string; sha256: string; kind: "file" | "directory" | "symlink"; bytes: number };
type Inventory = { protectedSource: string; artifacts: Artifact[] };
type Facts = { revisionMatches?: boolean; targetsOwned?: boolean; sourcesPreserved?: boolean; validationPassed?: boolean; rollbackRestored?: boolean };
export type Phase = "idle" | "diagnosed" | "planned" | "applied" | "verified" | "refused" | "rolledBack";
export type Event = "diagnose" | "plan" | "apply" | "verify" | "refuse" | "rollback";
export type Finding = Pick<Diagnostic, "code" | "severity" | "message">;
type SourceEdit = { path: string; entrypoint: string; code: string; fixId: string; message: string; before: string; after: string; expectedSource: string };
export type Plan = { id: string; root: string; policy: string; inventory: Inventory; phase: Phase; edit?: SourceEdit };
export type Result = { phase: Phase; exitCode: number; diagnostics: Finding[]; plan?: Plan };
export interface ArtifactEffects {
  copy(source: string, destination: string): Promise<void>;
  remove(path: string, directory: boolean): Promise<void>;
  validateTypeSpec?: typeof validateFixed;
}
const fileEffects: ArtifactEffects = {
  copy: (source, destination) => cp(source, destination, { recursive: true, dereference: false, verbatimSymlinks: true }),
  remove: (path, directory) => rm(path, { recursive: directory, force: false }),
};

function finding(diagnostic: Diagnostic): Finding {
  return { code: diagnostic.code, severity: diagnostic.severity, message: diagnostic.message };
}
async function command(file: string, args: string[]): Promise<string> {
  try {
    const result = await exec(file, args, { cwd: toolRoot, maxBuffer: 4 * 1024 * 1024, timeout: 120_000 });
    return result.stdout.trim();
  } catch (error) {
    const failure = error as Error & { stdout?: string; stderr?: string };
    const detail = `${failure.stdout ?? ""}\n${failure.stderr ?? ""}`.trim().slice(0, 1600);
    throw new Error(`${failure.message.slice(0, 400)}${detail ? "\n" + detail : ""}`);
  }
}
async function inventory(root: string): Promise<Inventory> {
  return JSON.parse(await command("python3", [join(toolRoot, "eng/scripts/diagnostics.py"), "inventory", root])) as Inventory;
}
async function stateDirectory(root: string): Promise<string> {
  const base = await command("python3", [join(toolRoot, "eng/scripts/diagnostics.py"), "path", root]);
  const directory = join(base, "agent/current");
  await mkdir(directory, { recursive: true });
  return directory;
}
async function json(path: string, value: unknown): Promise<void> {
  const temporary = `${path}.tmp`;
  await writeFile(temporary, JSON.stringify(value, null, 2) + "\n");
  await rename(temporary, path);
}
async function previewFix(fix: CodeFix): Promise<Map<string, string>> {
  const writes = new Map<string, string>();
  await applyCodeFix({ ...NodeHost, writeFile: async (path, content) => { writes.set(path, content); } }, fix);
  return writes;
}
async function policyIdentity(): Promise<string> {
  return hash(await readFile(join(toolRoot, "lean-toolchain"), "utf8") + await readFile(join(toolRoot, "eng/agent-policy/Policy.lean"), "utf8")
    + await readFile(join(toolRoot, "eng/scripts/diagnostics.py"), "utf8")
    + await readFile(fileURLToPath(import.meta.url), "utf8")
    + await readFile(join(dirname(fileURLToPath(import.meta.url)), "query.ts"), "utf8"));
}
async function policyArgs(): Promise<string[]> {
  const pin = (await readFile(join(toolRoot, "lean-toolchain"), "utf8")).trim();
  // Listing does not install or mutate the selected global Lean toolchain.
  const installed = (await command("elan", ["toolchain", "list"])).split("\n").map(line => line.trim().split(" ")[0]);
  if (!installed.includes(pin)) throw new Error(`Install the pinned Lean toolchain explicitly: ${pin}`);
  const source = await readFile(join(toolRoot, "eng/agent-policy/Policy.lean"), "utf8");
  if (/\b(sorry|admit|axiom)\b/.test(source)) throw new Error("The policy contains an unchecked proof or custom axiom");
  return ["run", pin, "lean", "-j2", "--run", join(toolRoot, "eng/agent-policy/Policy.lean")];
}
export async function provePolicy(): Promise<string> {
  return command("elan", [...await policyArgs(), "proof"]);
}
async function transition(phase: Phase, event: Event, facts: Facts = {}): Promise<Phase> {
  const flags = [facts.revisionMatches, facts.targetsOwned, facts.sourcesPreserved, facts.validationPassed, facts.rollbackRestored]
    .map(value => value === true ? "true" : "false");
  const next = await command("elan", [...await policyArgs(), phase, event, ...flags]);
  if (!["idle", "diagnosed", "planned", "applied", "verified", "refused", "rolledBack"].includes(next))
    throw new Error("Invalid Lean policy response");
  return next as Phase;
}
function planId(plan: Pick<Plan, "root" | "policy" | "inventory" | "edit">): string {
  return hash(JSON.stringify({ root: plan.root, policy: plan.policy, inventory: plan.inventory, edit: plan.edit }));
}
function safePath(root: string, target: string): string {
  if (isAbsolute(target)) throw new Error("Absolute target in plan");
  const full = resolve(root, target);
  const within = relative(root, full);
  if (!within || within.startsWith(`..${sep}`) || within === "..") throw new Error("Target escapes repository");
  return full;
}
async function exists(path: string): Promise<boolean> {
  try { await lstat(path); return true; } catch (error) {
    if ((error as NodeJS.ErrnoException).code === "ENOENT") return false;
    throw error;
  }
}
async function store(root: string, result: Result): Promise<Result> {
  const directory = await stateDirectory(root);
  await json(join(directory, "summary.json"), { status: result.phase, exitCode: result.exitCode,
    context: { root, source: result.plan?.phase === "verified" && result.plan.edit
      ? result.plan.edit.expectedSource : result.plan?.inventory.protectedSource }, problems: result.diagnostics.map(d => `${d.code}: ${d.message}`),
    planId: result.plan?.id });
  if (result.plan) await json(join(directory, "plan.json"), result.plan);
  return result;
}
async function diagnoseArtifactsCore(root: string): Promise<Result> {
  root = await realpath(root);
  const current = await inventory(root);
  const phase = await transition("idle", "diagnose");
  return store(root, { phase, exitCode: current.artifacts.length ? 1 : 0, diagnostics: current.artifacts.map(item =>
    finding(createDiagnostic({ code: "artifact-leftover", target: NoTarget, format: { path: item.path } }))) });
}
async function planArtifactsCore(root: string): Promise<Result> {
  root = await realpath(root);
  const directory = await stateDirectory(root);
  if (await exists(join(directory, "backups")) || await exists(join(directory, "source-backup"))) {
    return store(root, { phase: "refused", exitCode: 2, diagnostics: [finding(createDiagnostic({ code: "validation-failed", target: NoTarget }))] });
  }
  const data = { root, policy: await policyIdentity(), inventory: await inventory(root) };
  const phase = await transition(await transition("idle", "diagnose"), "plan");
  const plan: Plan = { ...data, phase, id: planId(data) };
  return store(root, { phase, exitCode: 0, plan, diagnostics: [] });
}

async function planTypeSpecFixCore(root: string, entrypoint: string, code: string, fixId: string): Promise<Result> {
  root = await realpath(root);
  const directory = await stateDirectory(root);
  if (await exists(join(directory, "backups")) || await exists(join(directory, "source-backup")))
    return store(root, { phase: "refused", exitCode: 2, diagnostics: [finding(createDiagnostic({ code: "validation-failed", target: NoTarget }))] });
  const before = await inventory(root);
  const offered = await resolveOfferedFix(safePath(root, entrypoint), code, fixId);
  if (!offered || offered.edits.length === 0) return store(root, { phase: "refused", exitCode: 2,
    diagnostics: [finding(createDiagnostic({ code: "unsafe-target", target: NoTarget }))] });
  const files = new Set(offered.edits.map(edit => edit.file.path));
  if (files.size !== 1) return store(root, { phase: "refused", exitCode: 2,
    diagnostics: [finding(createDiagnostic({ code: "unsafe-target", target: NoTarget }))] });
  const file = await realpath([...files][0]);
  const path = relative(root, file);
  if (!path.startsWith(`contracts${sep}`) || !path.endsWith(".tsp") || path.includes(`${sep}node_modules${sep}`))
    return store(root, { phase: "refused", exitCode: 2, diagnostics: [finding(createDiagnostic({ code: "unsafe-target", target: NoTarget }))] });
  const original = await readFile(file, "utf8");
  if (offered.edits.some(edit => edit.file.text !== original) || JSON.stringify(before) !== JSON.stringify(await inventory(root)))
    return store(root, { phase: "refused", exitCode: 2, diagnostics: [finding(createDiagnostic({ code: "stale-plan", target: NoTarget }))] });
  const preview = await previewFix(offered.fix);
  const after = preview.get([...files][0]);
  if (preview.size !== 1 || after === undefined) return store(root, { phase: "refused", exitCode: 2,
    diagnostics: [finding(createDiagnostic({ code: "unsafe-target", target: NoTarget }))] });
  const expectedSource = execFileSync("python3", [join(toolRoot, "eng/scripts/diagnostics.py"), "predict", root],
    { input: JSON.stringify({ [path]: after }), encoding: "utf8", maxBuffer: 1024 * 1024 }).trim();
  const data = { root, policy: await policyIdentity(), inventory: before,
    edit: { path, entrypoint, code, fixId, message: offered.diagnostic.message, before: hash(original), after, expectedSource } };
  const plan: Plan = { ...data, phase: await transition(await transition("idle", "diagnose"), "plan"), id: planId(data) };
  return store(root, { phase: plan.phase, exitCode: 0, plan, diagnostics: [] });
}

async function applyTypeSpecPlan(plan: Plan, directory: string, validate: typeof validateFixed): Promise<Result> {
  const edit = plan.edit!;
  const target = safePath(plan.root, edit.path);
  const original = await readFile(target, "utf8");
  const offered = await resolveOfferedFix(safePath(plan.root, edit.entrypoint), edit.code, edit.fixId);
  if (!offered || hash(original) !== edit.before || (await previewFix(offered.fix)).get(offered.edits[0].file.path) !== edit.after)
    return store(plan.root, { phase: "refused", exitCode: 2, plan: { ...plan, phase: "refused" },
      diagnostics: [finding(createDiagnostic({ code: "stale-plan", target: NoTarget }))] });
  plan.phase = await transition(plan.phase, "apply", { revisionMatches: true, targetsOwned: true, sourcesPreserved: true });
  await json(join(directory, "plan.json"), plan);
  const backup = join(directory, "source-backup");
  await writeFile(backup, original);
  try {
    await applyCodeFix({ ...NodeHost, writeFile: async (path, content) => {
      if (await realpath(path) !== target || content !== edit.after || hash(await readFile(target, "utf8")) !== edit.before)
        throw new Error("Codefix changed its declared target");
      const temporary = target + ".nami-fix.tmp";
      try { await writeFile(temporary, content); await rename(temporary, target); }
      finally { await rm(temporary, { force: true }); }
    } }, offered.fix);
    const actual = await inventory(plan.root);
    const valid = await validate(safePath(plan.root, edit.entrypoint), edit.code, edit.message);
    plan.phase = await transition(plan.phase, "verify", { revisionMatches: actual.protectedSource === edit.expectedSource,
      sourcesPreserved: actual.protectedSource === edit.expectedSource, validationPassed: valid });
    if (plan.phase !== "verified") throw new Error("TypeSpec verification failed");
    await rm(backup);
    return store(plan.root, { phase: plan.phase, exitCode: 0, plan, diagnostics: [] });
  } catch {
    if (await readFile(target, "utf8") === edit.after) await writeFile(target, original);
    const restored = JSON.stringify(await inventory(plan.root)) === JSON.stringify(plan.inventory);
    plan.phase = await transition(plan.phase, "rollback", { rollbackRestored: restored });
    if (restored) await rm(backup, { force: true });
    return store(plan.root, { phase: plan.phase, exitCode: 2, plan,
      diagnostics: [finding(createDiagnostic({ code: "validation-failed", target: NoTarget }))] });
  }
}
async function applyArtifactPlanCore(root: string, id: string, effects: ArtifactEffects = fileEffects): Promise<Result> {
  root = await realpath(root);
  const directory = await stateDirectory(root);
  const plan = JSON.parse(await readFile(join(directory, "plan.json"), "utf8")) as Plan;
  if (await exists(join(directory, "backups")) || await exists(join(directory, "source-backup"))) {
    return store(root, { phase: "refused", exitCode: 2,
      diagnostics: [finding(createDiagnostic({ code: "invalid-state", target: NoTarget, format: { phase: "recovery-required", event: "apply" } }))] });
  }
  if (plan.root !== root || id !== plan.id || planId(plan) !== id || plan.policy !== await policyIdentity()
      || JSON.stringify(await inventory(root)) !== JSON.stringify(plan.inventory)) {
    return store(root, { phase: "refused", exitCode: 2, plan: { ...plan, phase: "refused" },
      diagnostics: [finding(createDiagnostic({ code: "stale-plan", target: NoTarget }))] });
  }
  if (plan.phase !== "planned") {
    return store(root, { phase: "refused", exitCode: 2,
      diagnostics: [finding(createDiagnostic({ code: "invalid-state", target: NoTarget, format: { phase: plan.phase, event: "apply" } }))] });
  }
  if (plan.edit) return applyTypeSpecPlan(plan, directory, effects.validateTypeSpec ?? validateFixed);
  // The inventory comes from the same ownership implementation used by automatic cleanup.
  const targets = plan.inventory.artifacts.map(item => safePath(root, item.path));
  plan.phase = await transition(plan.phase, "apply", { revisionMatches: true, targetsOwned: true, sourcesPreserved: true });
  await json(join(directory, "plan.json"), plan);
  const backups = join(directory, "backups");
  const removed: number[] = [];
  try {
    await mkdir(backups);
    for (const [i, target] of targets.entries()) await effects.copy(target, join(backups, String(i)));
    if (JSON.stringify(await inventory(root)) !== JSON.stringify(plan.inventory)) throw new Error("Inputs changed during backup");
    for (const [i, target] of targets.entries()) {
      await effects.remove(target, plan.inventory.artifacts[i].kind === "directory");
      removed.push(i);
    }
    const after = await inventory(root);
    plan.phase = await transition(plan.phase, "verify", { revisionMatches: after.protectedSource === plan.inventory.protectedSource,
      sourcesPreserved: after.protectedSource === plan.inventory.protectedSource, validationPassed: after.artifacts.length === 0 });
    if (plan.phase !== "verified") throw new Error("Post-apply validation failed");
    await rm(backups, { recursive: true });
    return store(root, { phase: plan.phase, exitCode: 0, plan, diagnostics: [] });
  } catch {
    for (const index of removed.reverse()) {
      const target = targets[index];
      if (!await exists(target)) {
        await mkdir(dirname(target), { recursive: true });
        await cp(join(backups, String(index)), target, { recursive: true, dereference: false, verbatimSymlinks: true });
      }
    }
    const restored = JSON.stringify(await inventory(root)) === JSON.stringify(plan.inventory);
    plan.phase = await transition(plan.phase, "rollback", { rollbackRestored: restored });
    if (restored) await rm(backups, { recursive: true, force: true });
    return store(root, { phase: plan.phase, exitCode: 2, plan,
      diagnostics: [finding(createDiagnostic({ code: "validation-failed", target: NoTarget }))] });
  }
}

// Public library boundary: expected setup/IO failures are diagnostics, not rejected promises.
async function guarded(operation: () => Promise<Result>): Promise<Result> {
  try { return await operation(); }
  catch (error) {
    return { phase: "refused", exitCode: 2, diagnostics: [finding(createDiagnostic({
      code: "setup-failed", target: NoTarget,
      format: { detail: (error instanceof Error ? error.message : String(error)).slice(0, 1800) },
    }))] };
  }
}
export const diagnoseArtifacts = (root: string) => guarded(() => diagnoseArtifactsCore(root));
export const planArtifacts = (root: string) => guarded(() => planArtifactsCore(root));
export const planTypeSpecFix = (root: string, file: string, code: string, id: string) =>
  guarded(() => planTypeSpecFixCore(root, file, code, id));
export const applyArtifactPlan = (root: string, id: string, effects: ArtifactEffects = fileEffects) =>
  guarded(() => applyArtifactPlanCore(root, id, effects));
