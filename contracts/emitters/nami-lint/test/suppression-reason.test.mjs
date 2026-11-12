import test from "node:test";
import { suppressionReasonRule as rule } from "../dist/rules/suppression-reason.js";
import { Tester, code, testRule } from "./harness.mjs";

test("suppression-reason: the compiler accepts a suppression without a reason", async () => {
  // The gap this rule exists for.
  const diagnostics = await Tester.diagnose(`#suppress "deprecated"\nmodel Legacy {}`);
  if (diagnostics.length !== 0) throw new Error(`compiler now reports: ${diagnostics.map((d) => d.code).join(", ")}`);
});

test("suppression-reason: a suppression with no reason, an empty reason or a blank reason", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Api.Contracts.Tools;
    #suppress "deprecated"
    model First { id: string; }
    #suppress "@ancplua/typespec-nami-lint/unreachable-declaration" ""
    model Second {
      #suppress "@ancplua/typespec-nami-lint/wire-name-snake-case" "   "
      traceId: string;
    }
  `).toEmitDiagnostics([
    { code: code(rule), message: /^This suppression of 'deprecated' gives no reason\./ },
    { code: code(rule), message: /^This suppression of '@ancplua\/typespec-nami-lint\/unreachable-declaration' gives no reason\./ },
    { code: code(rule), message: /^This suppression of '@ancplua\/typespec-nami-lint\/wire-name-snake-case' gives no reason\./ },
  ]);
});

test("suppression-reason: a ledger id or a sentence is a reason", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Api.Contracts.Tools;
    #suppress "@ancplua/typespec-nami-lint/unreachable-declaration" "C05"
    model First { id: string; }
    #suppress "deprecated" "kept until the 0.3 clients are gone"
    model Second { id: string; }
  `).toBeValid();
});
