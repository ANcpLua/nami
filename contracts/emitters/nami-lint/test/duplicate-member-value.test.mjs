import test from "node:test";
import { duplicateMemberValueRule as rule } from "../dist/rules/duplicate-member-value.js";
import { Tester, code, testRule } from "./harness.mjs";

test("duplicate-member-value: the compiler accepts duplicate enum values and union variants", async () => {
  // The gap this rule exists for, checked against the compiler the contract is pinned to.
  const diagnostics = await Tester.diagnose(`
    namespace Nami.Api.Contracts.Health;
    enum Status { ok: "ok", fine: "ok" }
    union Level { "low", "high", "low" }
  `);
  if (diagnostics.length !== 0) throw new Error(`compiler now reports: ${diagnostics.map((d) => d.code).join(", ")}`);
});

test("duplicate-member-value: two enum members with one string value", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Api.Contracts.Health;
    enum Status { ok: "ok", fine: "ok", bad: "bad" }
  `).toEmitDiagnostics({
    code: code(rule),
    message: `Enum member 'fine' is serialized as "ok", the value 'ok' already has. A reader cannot tell the two apart; give it its own value or remove it.`,
  });
});

test("duplicate-member-value: two enum members with one number, and a name that equals another member's value", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Api.Contracts.Health;
    enum Kind { unspecified: 0, internal: 1, server: 1 }
    enum Mode { fast, quick: "fast" }
  `).toEmitDiagnostics([
    { code: code(rule), message: /^Enum member 'server' is serialized as 1, the value 'internal' already has\./ },
    { code: code(rule), message: /^Enum member 'quick' is serialized as "fast", the value 'fast' already has\./ },
  ]);
});

test("duplicate-member-value: an encoded name gives a member its own serialized value (TypeSpec 1.18.0)", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Api.Contracts.Health;
    enum Status { @encodedName("application/json", "unknown") unspecified: 0, ok: 1, @encodedName("application/json", "bad") failed: 2 }
  `).toBeValid();
});

test("duplicate-member-value: a variant listed twice in a named and in an anonymous union", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Api.Contracts.Tools;
    union Level { "low", "high", "low" }
    model Change { kind: "revision" | "version" | "revision"; size: int32 | string | int32; }
  `).toEmitDiagnostics([
    // In the compiler's navigation order: the models of a namespace, then its unions.
    { code: code(rule), message: `Union variant 3 repeats '"revision"', which variant 1 already lists. Remove the duplicate.` },
    { code: code(rule), message: `Union variant 3 repeats 'int32', which variant 1 already lists. Remove the duplicate.` },
    { code: code(rule), message: `Union variant 3 repeats '"low"', which variant 1 already lists. Remove the duplicate.` },
  ]);
});

test("duplicate-member-value: distinct values, 1 beside \"1\", and a discriminated union sharing a payload are fine", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Api.Contracts.Tools;
    enum Status { ok: "ok", bad: "bad" }
    enum Code { one: 1, two: 2 }
    union Mixed { 1, "1", float64, "NaN" }
    model Payload { value: string; }
    @discriminated
    union Event { created: Payload, deleted: Payload }
    model Uses { a: Status; b: Code; c: Mixed; d: Event; e: string | null; }
  `).toBeValid();
});
