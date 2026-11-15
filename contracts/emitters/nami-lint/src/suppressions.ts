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
//
// Two kinds of directive are refused by what they say, used or not. One without
// a reason: the rule `suppression-reason` reports it, but a second directive
// stacked above it can suppress that report, and then the compiler, every rule
// and every `used` flag are satisfied. And a suppression of `suppression-reason`
// itself, which is that second directive.
// =============================================================================

import type { Program } from "@typespec/compiler";
import { getSuppressions } from "@typespec/compiler/ast";
import { compareText } from "./contract.js";
import { LIBRARY_NAME } from "./library.js";
import { suppressionReasonRule } from "./rules/suppression-reason.js";

/** The code of the rule that asks every directive for a reason; suppressing it is refused. */
const REASON_RULE = `${LIBRARY_NAME}/${suppressionReasonRule.name}`;

export type SuppressionState =
  /** Matched a diagnostic in this compilation. */
  | "used"
  /** Matched nothing although every stage ran with its rule enabled: remove it. */
  | "unused"
  /** Its rule is disabled by the configuration, so this compilation cannot tell. */
  | "undecidable";

export type SuppressionDefect =
  /** No reason, or only white space: nobody can tell a decision from a leftover. */
  | "no-reason"
  /** It suppresses `suppression-reason`, which would let any directive go without a reason. */
  | "suppresses-reason-rule";

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
  /** Why the directive is refused whatever its state; undefined when it is acceptable. */
  readonly defect: SuppressionDefect | undefined;
}

export interface SuppressionAudit {
  readonly records: readonly SuppressionRecord[];
  readonly used: number;
  readonly unused: number;
  readonly undecidable: number;
  /** Directives refused for what they say (see `defect`), used or not. */
  readonly refused: number;
  /** Whether every suppression is known to be needed and gives an acceptable reason. */
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
    const reason = suppression.directive.message;
    return {
      file: relativePath(program.projectRoot, file.path),
      path: file.path,
      line: start.line + 1,
      column: start.character + 1,
      pos,
      end,
      code,
      reason,
      state: suppression.used ? "used" : disabled.has(code) ? "undecidable" : "unused",
      defect: code === REASON_RULE ? "suppresses-reason-rule" : reason.trim() === "" ? "no-reason" : undefined,
    };
  });
  records.sort((left, right) => compareText(left.file, right.file) || left.pos - right.pos);
  const count = (state: SuppressionState): number => records.filter((record) => record.state === state).length;
  const unused = count("unused");
  const undecidable = count("undecidable");
  const refused = records.filter((record) => record.defect !== undefined).length;
  return { records, used: count("used"), unused, undecidable, refused, clean: unused === 0 && undecidable === 0 && refused === 0 };
}

export interface SuppressionReportOptions {
  /** List every suppression, not only the ones that fail the audit. */
  readonly list?: boolean;
}

/**
 * The audit as text. Nothing here depends on the machine, the clock or the
 * environment, so two runs over the same sources print the same bytes. Every
 * line that fails the gate says `error`, so the contract check in the push gate
 * (`eng/contracts.sh verify`, which keeps such lines) shows where it failed.
 */
export function formatSuppressionAudit(audit: SuppressionAudit, options: SuppressionReportOptions = {}): string {
  const lines: string[] = [];
  const position = (record: SuppressionRecord): string => `${record.file}:${record.line}:${record.column}`;
  for (const record of audit.records) {
    if (record.state === "unused") {
      lines.push(`${position(record)} - error: unused suppression of '${record.code}': nothing matched it in a full compilation. Remove the directive.`);
    } else if (record.state === "undecidable") {
      lines.push(`${position(record)} - error: suppression of '${record.code}' cannot be checked: tspconfig.yaml disables that rule. Enable the rule or remove the directive.`);
    }
    if (record.defect === "suppresses-reason-rule") {
      lines.push(`${position(record)} - error: suppression of '${record.code}' is not allowed: it lets the next directive go without a reason. Remove it and give that directive a reason.`);
    } else if (record.defect === "no-reason") {
      lines.push(`${position(record)} - error: suppression of '${record.code}' gives no reason. Add one as the second string: the cleanup-ledger id, or why the finding is acceptable here.`);
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
  const refused = audit.refused > 0 ? `, ${audit.refused} refused` : "";
  lines.push(
    `Suppressions: ${audit.records.length} total, ${audit.used} used, ${audit.unused} unused, ${audit.undecidable} undecidable${refused}.`,
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
