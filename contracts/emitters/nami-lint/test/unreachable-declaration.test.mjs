import test from "node:test";
import { unreachableDeclarationRule as rule } from "../dist/rules/unreachable-declaration.js";
import assert from "node:assert/strict";
import { createLinterRuleTester, createTester, mockFile } from "@typespec/compiler/testing";
import { GateTester, LIBRARY, code, contracts, libraries, mounted, testRule } from "./harness.mjs";

test("unreachable-declaration: a declaration nothing consumes is reported", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Api.Contracts.Tools;
    model Input { query: string; }
    model Result { hits: string[]; }
    model Failure { message: string; }
    @@tool(Input, "search", Result, Failure);
    scalar MacAddress extends string;
  `).toEmitDiagnostics({
    code: code(rule),
    message: /^Scalar 'Nami\.Api\.Contracts\.Tools\.MacAddress' is used by no declaration, tool, wire root, route, storage row or projection, and no emitter generates a type from it/,
  });
});

test("unreachable-declaration: only the top of a dead subgraph is reported, naming what goes with it", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Api.Contracts.OTel;
    scalar PointCount extends uint64;
    model HistogramPoint { kind: "histogram"; count: PointCount; }
    model SummaryPoint { kind: "summary"; count: PointCount; }
    @discriminated(#{ envelope: "none", discriminatorPropertyName: "kind" })
    union PointData { histogram: HistogramPoint, summary: SummaryPoint }
  `).toEmitDiagnostics({
    code: code(rule),
    message: /^Union 'Nami\.Api\.Contracts\.OTel\.PointData' is used by no declaration.* Only unreached declarations use 'HistogramPoint', 'PointCount', 'SummaryPoint', so they go with it\.$/,
  });
});

test("unreachable-declaration: two dead declarations that use each other are reported once", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Api.Contracts.Tree;
    model Branch { leaves: Leaf[]; }
    model Leaf { parent?: Branch; }
  `).toEmitDiagnostics({ code: code(rule), message: /^Model 'Nami\.Api\.Contracts\.Tree\.Branch' / });
});

test("unreachable-declaration: tools, wire roots, routes, tables, projections and every kind of reference keep a declaration alive", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    @service namespace Nami.Api.Contracts {
      @route("/health") @get op health(): Health.Report;
    }
    namespace Nami.Api.Contracts.Health {
      enum Status { healthy: "healthy", degraded: "degraded" }
      model Report { status: Status; }
    }
    namespace Nami.Api.Contracts.Tools {
      scalar Cursor extends string;
      model Paging { cursor?: Cursor; }
      model Input extends Paging { query: string; }
      model Mixin { service: string; }
      model Hit { ...Mixin; body: string; }
      model ProjectedHit { ...OptionalProperties<Hit>; }
      model Result { hits: ProjectedHit[]; }
      model Failure { message: string; }
      @@tool(Input, "search", Result, Failure);
      @discriminator("kind") model Shape { kind: string; }
      model Circle extends Shape { kind: "circle"; radius: float64; }
      @@wireRoot(Shape);
      @jsonSchema model Standalone { id: string; }
    }
    namespace Nami.Models.Rows {
      scalar ProjectId extends string;
      @duckDbTable(#{ name: "spans" })
      model SpanRow { @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0 }) projectId: ProjectId; }
      model SpanRecord { projectId: string; }
      model SpanValues { projectId: SpanRow.projectId; }
      @@modelProjection(SpanRow, SpanRecord, SpanValues);
    }
    @csharpOutput(#{ directory: "src/probe/Generated", \`namespace\`: "Probe", file: "Probe.g.cs" })
    namespace Nami.Models.Runtime {
      model ConsumedByHandWrittenCode { bytes: int64; }
    }
  `).toBeValid();
});

test("unreachable-declaration: an identity scalar of the lint policy is a root", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Api.Contracts.Common;
    scalar SessionId extends string;
  `).toBeValid();
});

test("unreachable-declaration: declarations outside the contract namespaces are not judged", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Other.Things;
    model Unused { id: string; }
  `).toBeValid();
});

test("unreachable-declaration: a template is used through its instances, and unused without one", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Api.Contracts.Tools;
    model Page<T> { items: T[]; }
    model Hit { body: string; }
    model Input { query: string; }
    model Result { page: Page<Hit>; }
    model Failure { message: string; }
    @@tool(Input, "search", Result, Failure);
    model Envelope<T> { value: T; }
  `).toEmitDiagnostics({ code: code(rule), message: /^Model 'Nami\.Api\.Contracts\.Tools\.Envelope' / });
});

test("unreachable-declaration: extending a used base does not make a model used, unless the base is a discriminator", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Api.Contracts.Tools;
    model ToolReadInput { service?: string; }
    model SearchInput extends ToolReadInput { query: string; }
    model OrphanInput extends ToolReadInput { limit: int32; }
    model Result { hits: string[]; }
    model Failure { message: string; }
    @@tool(SearchInput, "search", Result, Failure);
  `).toEmitDiagnostics({ code: code(rule), message: /^Model 'Nami\.Api\.Contracts\.Tools\.OrphanInput' / });
});

test("unreachable-declaration: a type named with typeref(T) in a decorator value is used (TypeSpec 1.18.0)", async () => {
  // `typeref` is behind a compiler feature; a registration decorator stands in for a future emitter one.
  const tester = mounted(createTester(contracts, { libraries, features: ["typeref"] }))
    .files({ "registry.js": mockFile.js({ $registers: () => {} }) })
    .import("./registry.js");
  const source = (registration) => `
    namespace Registry { model Entry { name: string; type: TypeRef<unknown>; } }
    extern dec registers(target: Reflection.Model, ...entries: valueof Registry.Entry[]);
    namespace Nami.Api.Contracts.Tools {
      model Referenced { id: string; }
      ${registration}
      model Root { id: string; }
      @@wireRoot(Root);
    }
  `;
  const run = async (registration) => createLinterRuleTester(await tester.createInstance(), rule, LIBRARY).expect(source(registration));
  await (await run(`@@registers(Root, #{ name: "referenced", type: typeref(Referenced) });`)).toBeValid();
  await (await run(``)).toEmitDiagnostics({ code: code(rule), message: /^Model 'Nami\.Api\.Contracts\.Tools\.Referenced' / });
});

// The shape of the contract at 48bdb08: `OTel.Metrics.MetricPointData` and `OTel.Logs.LogRecord` are reached by
// no tool, but the namespace is mapped with @csharpNamespace, so both emitters generate a type from each, and
// hand-written tests use the first. Only the unbranded scalar has no generated type.
const generatedNamespace = `
  namespace Nami.Api.Contracts.OTel {
    scalar PointCount extends uint64;
    model HistogramPoint { kind: "histogram"; count: PointCount; }
    @discriminated(#{ envelope: "none", discriminatorPropertyName: "kind" })
    union PointData { histogram: HistogramPoint }
    enum Severity { info: 9, warn: 13 }
    model LogRecord { severity: Severity; }
    @csharpBrand scalar Branded extends string;
    scalar MacAddress extends string;
  }
  @@csharpNamespace(Nami.Api.Contracts.OTel, "Nami.Api.Contracts.OTel");
`;

test("unreachable-declaration: a declaration with a generated type counts as reached; an unbranded scalar does not", async () => {
  const tester = await testRule(rule);
  await tester.expect(generatedNamespace).toEmitDiagnostics({
    code: code(rule),
    message: "Scalar 'Nami.Api.Contracts.OTel.MacAddress' is used by no declaration, tool, wire root, route, storage row or projection, and no emitter generates a type from it; at most a schema entry carries its name. Remove it unless code looks that entry up by name, or reference it from the declaration that needs it.",
  });
});

test("unreachable-declaration: the audit option lists unreached generated types and tells them apart", async () => {
  const audit = { enable: { [code(rule)]: { generatedTypes: "report" } } };
  const [, diagnostics] = await GateTester.compileAndDiagnose(generatedNamespace, { compilerOptions: { linterRuleSet: audit } });
  const lookFirst = "The emitters still generate a type from it for code outside contracts/, so it is unused only if no hand-written code uses that type: look for such a use before removing it.";
  const unreached = (kind, name) => `${kind} 'Nami.Api.Contracts.OTel.${name}' is not reachable from any tool, wire root, route, storage row or projection. ${lookFirst}`;
  assert.deepEqual(diagnostics.map((d) => d.message), [
    unreached("Scalar", "Branded"),
    `${unreached("Model", "LogRecord")} Only unreached declarations use 'Severity', so it goes with it.`,
    "Scalar 'Nami.Api.Contracts.OTel.MacAddress' is used by no declaration, tool, wire root, route, storage row or projection, and no emitter generates a type from it; at most a schema entry carries its name. Remove it unless code looks that entry up by name, or reference it from the declaration that needs it.",
    `${unreached("Union", "PointData")} Only unreached declarations use 'HistogramPoint', 'PointCount', so they go with it.`,
  ]);
});

test("unreachable-declaration: an unknown option value is rejected, not ignored", async () => {
  const wrong = { enable: { [code(rule)]: { generatedTypes: "ignore" } } };
  const [, diagnostics] = await GateTester.compileAndDiagnose(generatedNamespace, { compilerOptions: { linterRuleSet: wrong } });
  assert.deepEqual(diagnostics.map((d) => d.code), ["invalid-rule-options"]);
});
