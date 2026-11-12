import test from "node:test";
import { duplicateWireNameRule as rule } from "../dist/rules/duplicate-wire-name.js";
import { Tester, code, testRule } from "./harness.mjs";

const inherited = `
  namespace Nami.Api.Contracts.Tools;
  model ToolResult { @encodedName("application/json", "snapshot_id") snapshotId: string; }
  model LogsResult extends ToolResult { @encodedName("application/json", "snapshot_id") snapshot: string; }
`;

test("duplicate-wire-name: the compiler accepts a wire name that repeats an inherited one", async () => {
  // The gap this rule exists for. If a later compiler reports it, this test says the rule can go.
  const diagnostics = await Tester.diagnose(inherited);
  if (diagnostics.length !== 0) throw new Error(`compiler now reports: ${diagnostics.map((d) => d.code).join(", ")}`);
});

test("duplicate-wire-name: an own property takes the wire name of an inherited property", async () => {
  const tester = await testRule(rule);
  await tester.expect(inherited).toEmitDiagnostics({
    code: code(rule),
    message: "Property 'snapshot' ships the JSON key 'snapshot_id', which 'snapshotId' inherited from 'Nami.Api.Contracts.Tools.ToolResult' already ships. One object cannot carry one key twice: rename one of them on the wire, or drop the duplicate.",
  });
});

test("duplicate-wire-name: an encoded name equal to an inherited property name collides too", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Api.Contracts.Tools;
    model ToolResult { cursor: string; }
    model LogsResult extends ToolResult { @encodedName("application/json", "cursor") next: string; }
  `).toEmitDiagnostics({ code: code(rule) });
});

test("duplicate-wire-name: a collision is reported once, where it is introduced", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Api.Contracts.Tools;
    model Base { cursor: string; }
    model Middle extends Base { @encodedName("application/json", "cursor") next: string; }
    model Leaf extends Middle { limit: int32; }
  `).toEmitDiagnostics({ code: code(rule), message: /^Property 'next' / });
});

test("duplicate-wire-name: distinct wire names, an override and a header are fine", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Api.Contracts.Tools;
    model ToolResult {
      @encodedName("application/json", "snapshot_id") snapshotId: string;
      kind: string;
      @header("x-request-id") requestId: string;
    }
    model LogsResult extends ToolResult {
      @encodedName("application/json", "next_cursor") nextCursor: string;
      kind: "logs";
      @encodedName("application/json", "request_id") request: string;
    }
  `).toBeValid();
});
