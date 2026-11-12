// The suppression audit behind `scripts/check-suppressions.mjs`.
//
// These tests compile with the linter running, because the `used` flag of
// `getSuppressions` (TypeSpec 1.18.0) is only set when a diagnostic is matched
// against a directive after the lint pass.
import assert from "node:assert/strict";
import test from "node:test";
import { auditSuppressions, formatSuppressionAudit, unusedSuppressionEdits } from "../dist/index.js";
import { GateTester, LIBRARY } from "./harness.mjs";

const unreachable = `${LIBRARY}/unreachable-declaration`;
const gate = { extends: [`${LIBRARY}/all`] };

const source = `
namespace Nami.Api.Contracts.Tools;
#suppress "${unreachable}" "C99: kept until the metrics tool lands"
scalar Dead extends string;
`;

test("suppression audit: a suppression that still hides a finding is used", async () => {
  const [result, diagnostics] = await GateTester.compileAndDiagnose(source);
  assert.deepEqual(diagnostics.map((d) => d.code), []);
  const audit = auditSuppressions(result.program);
  assert.equal(audit.clean, true);
  // The tester puts its imports above the source, so the line is read from the compiled file.
  const text = [...result.program.sourceFiles.values()].find((script) => script.file.path === audit.records[0].path).file.text;
  const line = text.slice(0, text.indexOf("#suppress")).split("\n").length;
  assert.deepEqual(audit.records.map((r) => [r.file, r.line, r.column, r.code, r.reason, r.state]), [
    ["main.tsp", line, 1, unreachable, "C99: kept until the metrics tool lands", "used"],
  ]);
});

test("suppression audit: once the cause is fixed the suppression is unused and the gate fails", async () => {
  // The same directive, but the declaration is now a wire root: nothing is left to suppress.
  const fixed = `${source}model Root { dead: Dead; }\n@@wireRoot(Root);\n`;
  const [result, diagnostics] = await GateTester.compileAndDiagnose(fixed);
  assert.deepEqual(diagnostics.map((d) => d.code), []);
  const audit = auditSuppressions(result.program);
  assert.equal(audit.clean, false);
  assert.equal(audit.unused, 1);
  assert.match(formatSuppressionAudit(audit), /^main\.tsp:\d+:1 - unused suppression of '@ancplua\/typespec-nami-lint\/unreachable-declaration': nothing matched it in a full compilation\. Remove the directive\.\nSuppressions: 1 total, 0 used, 1 unused, 0 undecidable\.$/);
});

test("suppression audit: with its rule disabled a needed suppression reads as unused, so the audit refuses to judge it", async () => {
  // The snapshot caveat of `getSuppressions`: the same sources as the first test, a narrower ruleset.
  const disabled = { ...gate, disable: { [unreachable]: "demonstration" } };
  const [result] = await GateTester.compileAndDiagnose(source, { compilerOptions: { linterRuleSet: disabled } });
  assert.equal(auditSuppressions(result.program).records[0].state, "unused");
  const audit = auditSuppressions(result.program, { disabledRules: [unreachable] });
  assert.equal(audit.records[0].state, "undecidable");
  assert.equal(audit.clean, false);
  assert.match(formatSuppressionAudit(audit), /cannot be checked: tspconfig\.yaml disables that rule/);
});

test("suppression audit: records are sorted by file and position, and the report does not depend on the machine", async () => {
  const files = {
    "main.tsp": `import "./b.tsp";\nimport "./a.tsp";`,
    "b.tsp": `namespace Nami.Api.Contracts.B;\n#suppress "${unreachable}" "second file"\nscalar Y extends string;`,
    "a.tsp": `namespace Nami.Api.Contracts.A;\n#suppress "${unreachable}" "first file, second directive"\nscalar Z extends string;\n#suppress "${unreachable}" "first file, third directive"\nscalar X extends string;`,
  };
  const reports = [];
  for (let run = 0; run < 2; run++) {
    const [result] = await GateTester.compileAndDiagnose(files);
    const audit = auditSuppressions(result.program);
    assert.deepEqual(audit.records.map((r) => `${r.file}:${r.line}`), ["a.tsp:2", "a.tsp:4", "b.tsp:2"]);
    reports.push(formatSuppressionAudit(audit, { list: true }));
  }
  assert.equal(reports[0], reports[1]);
  assert.doesNotMatch(reports[0], /\/test\/|\\\\/);
});

test("suppression audit: the fix deletes an unused directive with its line and leaves the rest alone", () => {
  const text = [
    `namespace N;`,
    `#suppress "a/unused" "gone"`,
    `@doc("kept")`,
    `model A {}`,
    `  #suppress "a/used" "still needed"`,
    `model B {}`,
    `model C { #suppress "a/unused" "inline" x: string; }`,
    ``,
  ].join("\n");
  const record = (needle, state) => ({ pos: text.indexOf(needle), end: text.indexOf(needle) + needle.length, state });
  const records = [
    record(`#suppress "a/unused" "gone"`, "unused"),
    record(`#suppress "a/used" "still needed"`, "used"),
    record(`#suppress "a/unused" "inline"`, "unused"),
  ];
  let fixed = text;
  for (const edit of unusedSuppressionEdits(text, records)) fixed = fixed.slice(0, edit.pos) + fixed.slice(edit.end);
  assert.equal(fixed, [
    `namespace N;`,
    `@doc("kept")`,
    `model A {}`,
    `  #suppress "a/used" "still needed"`,
    `model B {}`,
    `model C {  x: string; }`,
    ``,
  ].join("\n"));
});
