import test from "node:test";
import { wireRootTwinRule as rule } from "../dist/rules/wire-root-twin.js";
import { code, testRule } from "./harness.mjs";

// The shape of cleanup-ledger item C05: `EngineRequest` and `EngineCommand` at 48bdb08.
const engine = (flatProperties, roots) => `
  namespace Nami.Api.Contracts.Engine;
  model EngineRequest { ${flatProperties} }
  model EngineEnvelope { id: string; revision: string; }
  model EngineCall { ...EngineEnvelope; method: "call"; name: string; @encodedName("application/json", "trace_parent") traceParent: string | null; }
  model EngineHealth { ...EngineEnvelope; method: "health"; }
  @discriminated(#{ envelope: "none", discriminatorPropertyName: "method" })
  union EngineCommand { call: EngineCall, health: EngineHealth }
  ${roots}
`;
const flat = `id: string; revision: string; method: "call" | "health"; name?: string | null; @encodedName("application/json", "trace_parent") traceParent?: string | null;`;
const bothRoots = `@@wireRoot(EngineRequest); @@wireRoot(EngineCommand);`;

test("wire-root-twin: a flat model and the discriminated union of the same message are both wire roots", async () => {
  const tester = await testRule(rule);
  await tester.expect(engine(flat, bothRoots)).toEmitDiagnostics({
    code: code(rule),
    message: /^Wire roots 'Nami\.Api\.Contracts\.Engine\.EngineRequest' and 'Nami\.Api\.Contracts\.Engine\.EngineCommand' describe one message\. .* its 'method' takes exactly the variant tags \("call", "health"\), and the variants' properties together are exactly its 5 properties\./,
  });
});

test("wire-root-twin: one root for the message is fine", async () => {
  const tester = await testRule(rule);
  await tester.expect(engine(flat, `@@wireRoot(EngineCommand);`)).toBeValid();
});

test("wire-root-twin: a model with a field no variant has is a different message", async () => {
  const tester = await testRule(rule);
  await tester.expect(engine(`${flat} deadline?: string;`, bothRoots)).toBeValid();
});

test("wire-root-twin: a model whose discriminator admits another value is a different message", async () => {
  const tester = await testRule(rule);
  const wider = flat.replace(`"call" | "health"`, `"call" | "health" | "cancel"`);
  await tester.expect(engine(wider, bothRoots)).toBeValid();
});

test("wire-root-twin: a tool result that is a variant of a union is not its twin", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Api.Contracts.Tools;
    model Active { kind: "active"; }
    @discriminated(#{ envelope: "none", discriminatorPropertyName: "kind" })
    union Status { active: Active }
    @@wireRoot(Status);
    @@wireRoot(Active);
  `).toBeValid();
});

test("wire-root-twin: a tool result is a wire root too", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Api.Contracts.Tools;
    model Input { query: string; }
    model Failure { message: string; }
    model Status { kind: "active" | "silent"; signal: string; since?: string; }
    model Active { kind: "active"; signal: string; }
    model Silent { kind: "silent"; signal: string; since: string; }
    @discriminated(#{ envelope: "none", discriminatorPropertyName: "kind" })
    union SignalStatus { active: Active, silent: Silent }
    @@tool(Input, "status", Status, Failure);
    @@wireRoot(SignalStatus);
  `).toEmitDiagnostics({ code: code(rule), message: /^Wire roots 'Nami\.Api\.Contracts\.Tools\.Status' and 'Nami\.Api\.Contracts\.Tools\.SignalStatus' describe one message\./ });
});
