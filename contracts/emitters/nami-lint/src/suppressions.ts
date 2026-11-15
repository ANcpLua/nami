// =============================================================================
// Suppression audit — is every `#suppress` still needed?
// =============================================================================
// A suppression carries a known finding until its cause is fixed. Once the
// cause is gone the directive must go too, or it will silently hide the next,
// unrelated finding on the same declaration.
//
// TypeSpec 1.18.0 exposes what is needed: `getSuppressions(program)` returns
// every project suppression with a `used` flag. The flag is a snapshot of one
// compilation. A directive is "unused" when nothing matched it in that run,
// which also happens when its rule was disabled, its emitter was skipped or the
// compilation stopped early. A linter rule cannot make the check either: rule
// diagnostics are matched against suppressions only after the whole lint pass
// returns, so inside a rule every lint suppression still reads as unused.
//
// So the audit runs after `compile`, and its caller makes the snapshot complete:
// the configured ruleset with no override, emitters running, no errors. This
// module is the pure half: it reads a finished program and decides. It is
// deterministic: records are sorted by project-relative path and position.
// =============================================================================

import type { Program } from "@typespec/compiler";
import { getSuppressions } from "@typespec/compiler/ast";
import { compareText } from "./contract.js";

export type SuppressionState =
  /** Matched a diagnostic in this compilation. */
  | "used"
  /** Matched nothing although every stage ran with its rule enabled: remove it. */
  | "unused"
  /** Its rule is disabled by the configuration, so this compilation cannot tell. */
  | "undecidable";

export interface SuppressionRecord {
  /** Path relative to the project root, with forward slashes. */
  readonly file: string;
  /** Absolute path, for a caller that edits the file. */
  readonly path: string;
  /** One-based line and column of the directive. */
  readonly line: number;
  readonly column: number;
  /** Offsets of the directive in the file text. */
  readonly pos: number;
  readonly end: number;
  /** The diagnostic code as written. */
  readonly code: string;
  /** The reason as written; empty when none was given. */
  readonly reason: string;
  readonly state: SuppressionState;
}

export interface SuppressionAudit {
  readonly records: readonly SuppressionRecord[];
  readonly used: number;
  readonly unused: number;
  readonly undecidable: number;
  /** Whether every suppression is known to be needed. */
  readonly clean: boolean;
}

export interface SuppressionAuditOptions {
  /**
   * Rule codes the configuration disables (`linter.disable`), as returned by
   * `collectLinterDisables`. A suppression of such a rule cannot be judged.
   */
  readonly disabledRules?: Iterable<string>;
}

function relativePath(root: string, path: string): string {
  const prefix = root.endsWith("/") ? root : `${root}/`;
  return path.startsWith(prefix) ? path.slice(prefix.length) : path;
}

/** Judge every project suppression of a finished compilation. */
export function auditSuppressions(program: Program, options: SuppressionAuditOptions = {}): SuppressionAudit {
  const disabled = new Set(options.disabledRules ?? []);
  const records: SuppressionRecord[] = getSuppressions(program).map((suppression) => {
    const { file, pos, end } = suppression.location;
    const start = file.getLineAndCharacterOfPosition(pos);
    const code = suppression.directive.code;
    return {
      file: relativePath(program.projectRoot, file.path),
      path: file.path,
      line: start.line + 1,
      column: start.character + 1,
      pos,
      end,
      code,
      reason: suppression.directive.message,
      state: suppression.used ? "used" : disabled.has(code) ? "undecidable" : "unused",
    };
  });
  records.sort((left, right) => compareText(left.file, right.file) || left.pos - right.pos);
  const count = (state: SuppressionState): number => records.filter((record) => record.state === state).length;
  const unused = count("unused");
  const undecidable = count("undecidable");
  return { records, used: count("used"), unused, undecidable, clean: unused === 0 && undecidable === 0 };
}

export interface SuppressionReportOptions {
  /** List every suppression, not only the ones that fail the audit. */
  readonly list?: boolean;
}

/**
 * The audit as text. Nothing here depends on the machine, the clock or the
 * environment, so two runs over the same sources print the same bytes.
 */
export function formatSuppressionAudit(audit: SuppressionAudit, options: SuppressionReportOptions = {}): string {
  const lines: string[] = [];
  const position = (record: SuppressionRecord): string => `${record.file}:${record.line}:${record.column}`;
  for (const record of audit.records) {
    if (record.state === "unused") {
      lines.push(`${position(record)} - unused suppression of '${record.code}': nothing matched it in a full compilation. Remove the directive.`);
    } else if (record.state === "undecidable") {
      lines.push(`${position(record)} - suppression of '${record.code}' cannot be checked: tspconfig.yaml disables that rule. Enable the rule or remove the directive.`);
    }
  }
  if (options.list) {
    const perCode = new Map<string, number>();
    for (const record of audit.records) perCode.set(record.code, (perCode.get(record.code) ?? 0) + 1);
    if (audit.records.length > 0) lines.push("Suppressions by rule:");
    for (const code of [...perCode.keys()].sort(compareText)) lines.push(`  ${String(perCode.get(code)).padStart(3)}  ${code}`);
    if (audit.records.length > 0) lines.push("Suppressions:");
    for (const record of audit.records) {
      lines.push(`  ${position(record)}  ${record.state}  ${record.code}  ${JSON.stringify(record.reason)}`);
    }
  }
  lines.push(
    `Suppressions: ${audit.records.length} total, ${audit.used} used, ${audit.unused} unused, ${audit.undecidable} undecidable.`,
  );
  return lines.join("\n");
}

/**
 * Text edits that delete the unused directives of one file: a directive alone
 * on its line goes with the line, otherwise only the directive goes. Edits are
 * returned last-first so applying them in order keeps earlier offsets valid.
 */
export function unusedSuppressionEdits(text: string, records: readonly SuppressionRecord[]): { pos: number; end: number }[] {
  const edits = records
    .filter((record) => record.state === "unused")
    .map((record) => {
      const lineStart = text.lastIndexOf("\n", record.pos - 1) + 1;
      if (text.slice(lineStart, record.pos).trim() !== "") return { pos: record.pos, end: record.end };
      let end = record.end;
      while (end < text.length && (text[end] === " " || text[end] === "\t")) end++;
      if (text[end] === "\r" && text[end + 1] === "\n") end += 2;
      else if (text[end] === "\n" || text[end] === "\r") end++;
      return { pos: lineStart, end };
    });
  return edits.sort((left, right) => right.pos - left.pos);
}
