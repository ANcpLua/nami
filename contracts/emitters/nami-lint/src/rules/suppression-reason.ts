// Health — a suppression says why.
//
// `#suppress "code"` compiles with no message at all (checked against
// @typespec/compiler 1.18.0), and a suppression without a reason is a finding
// nobody can ever close: the next reader cannot tell a decision from a
// leftover. In this contract a suppression is how a known finding is carried,
// so each one names the cleanup-ledger item or the reason it is acceptable.
//
// The directives are read with `collectSuppressions` from `@typespec/compiler/ast`
// (new in 1.18.0), which lists the `#suppress` directives of a parsed file
// without resolving their codes. Whether a suppression is still needed is a
// different question, answered after the whole compilation by
// `scripts/check-suppressions.mjs`.

import { createRule, paramMessage } from "@typespec/compiler";
import { collectSuppressions } from "@typespec/compiler/ast";
import { compareText } from "../contract.js";

export const suppressionReasonRule = createRule({
  name: "suppression-reason",
  severity: "warning",
  description: "Every #suppress directive gives a reason: a cleanup-ledger id or a sentence.",
  messages: {
    default: paramMessage`This suppression of '${"code"}' gives no reason. Add one as the second string: the cleanup-ledger id, or why the finding is acceptable here.`,
  },
  create(context) {
    return {
      exit: () => {
        const program = context.program;
        const scripts = [...program.sourceFiles.values()]
          .filter((script) => program.getSourceFileLocationContext(script.file).type === "project")
          .sort((left, right) => compareText(left.file.path, right.file.path));
        for (const script of scripts) {
          for (const suppression of collectSuppressions(script)) {
            if (suppression.directive.message.trim() !== "") continue;
            context.reportDiagnostic({
              target: suppression.directive.node,
              format: { code: suppression.directive.code },
            });
          }
        }
      },
    };
  },
});
