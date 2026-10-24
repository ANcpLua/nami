import { compile, NodeHost, resolveCodeFix, resolveCompilerOptions, type CodeFixEdit, type Diagnostic, type TypeInfo } from "@typespec/compiler";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { wireNameFixId } from "@ancplua/typespec-nami-lint";

const supported = (code: string, id: string) => code === "@ancplua/typespec-nami-lint/wire-name-snake-case" && id === wireNameFixId;

async function programFor(entrypoint: string) {
  const [options, diagnostics] = await resolveCompilerOptions(NodeHost, { entrypoint, cwd: dirname(entrypoint) });
  const program = await compile(NodeHost, entrypoint, { ...options, noEmit: true, additionalImports: [...(options.additionalImports ?? []), "@ancplua/nami-agent-control"] });
  return { program, diagnostics: [...diagnostics, ...program.diagnostics] };
}

/** Read-only accessor: return diagnostics; never report them as a side effect. */
export async function getTypeInfo(entrypoint: string, name: string): Promise<[TypeInfo | undefined, readonly Diagnostic[]]> {
  const { program, diagnostics } = await programFor(entrypoint);
  const [target, resolutionDiagnostics] = program.resolveTypeReference(name);
  return [target ? program.getTypeInfo(target) : undefined, [...diagnostics, ...resolutionDiagnostics]];
}

/** Resolve offered native TypeSpec edits without executing them. */
export async function getCodeFixes(entrypoint: string): Promise<readonly { code: string; message: string; fixes: readonly { id: string; label: string; edits: readonly CodeFixEdit[] }[] }[]> {
  const { diagnostics } = await programFor(entrypoint);
  return Promise.all(diagnostics.map(async diagnostic => ({
    code: diagnostic.code,
    message: diagnostic.message,
    fixes: await Promise.all((diagnostic.codefixes ?? []).filter(fix => supported(diagnostic.code, fix.id))
      .map(async fix => ({ id: fix.id, label: fix.label, edits: await resolveCodeFix(fix) }))),
  })));
}

export async function resolveOfferedFix(entrypoint: string, code: string, fixId: string) {
  if (!supported(code, fixId)) return undefined;
  const { program, diagnostics } = await programFor(entrypoint);
  const candidates = diagnostics.flatMap(diagnostic => diagnostic.code === code
    ? (diagnostic.codefixes ?? []).filter(fix => fix.id === fixId).map(fix => ({ diagnostic, fix })) : []);
  if (candidates.length !== 1) return undefined;
  return { ...candidates[0], edits: await resolveCodeFix(candidates[0].fix), program };
}

export async function validateFixed(entrypoint: string, code: string, message: string): Promise<boolean> {
  const { diagnostics } = await programFor(entrypoint);
  return !diagnostics.some(d => d.severity === "error" || (d.code === code && d.message === message));
}
