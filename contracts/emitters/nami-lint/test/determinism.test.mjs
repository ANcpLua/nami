// Same sources, same diagnostics, in the same order.
//
// The whole-program rules sort what they report by full type name, so their
// output does not follow the order in which declarations, files or imports
// happen to be visited. The fixture below trips every one of those rules.
import assert from "node:assert/strict";
import test from "node:test";
import { GateTester, LIBRARY } from "./harness.mjs";

// Only `main.tsp` gets the tester's imports; the other files bring their own.
const prelude = `import "./emit/lib/main.tsp";\nusing Nami.Api.Schema.Emit.CSharp;\n`;
const rows = `${prelude}
namespace Nami.Models.Storage.Rows;
@duckDbTable(#{ name: "zeta", indexes: "A,B;A" })
model ZetaRow {
  @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0 }) a: string;
  @duckDbColumn(#{ order: 2 }) b: string;
}
@duckDbTable(#{ name: "alpha", indexes: "A,B;A" })
model AlphaRow {
  @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0 }) a: string;
  @duckDbColumn(#{ order: 3 }) b: string;
}
`;
const tools = `${prelude}
namespace Nami.Api.Contracts.Tools;
scalar Zulu extends string;
scalar Alpha extends string;
model Twin2 { key: string; value: int64; }
model Twin1 { key: string; value: int64; }
model Flat { kind: "a" | "b"; id: string; }
model A { kind: "a"; id: string; }
model B { kind: "b"; id: string; }
@discriminated(#{ envelope: "none", discriminatorPropertyName: "kind" })
union Tagged { a: A, b: B }
@@wireRoot(Flat);
@@wireRoot(Tagged);
`;

/** Lint the fixture with its two files registered and imported in the given order. */
async function lint(order) {
  const sources = { "rows.tsp": rows, "tools.tsp": tools };
  const files = { "main.tsp": "" };
  for (const name of order) files[name] = sources[name];
  const [, diagnostics] = await GateTester.import(...order.map((name) => `./${name}`)).compileAndDiagnose(files);
  // Every diagnostic must come from this library: a fixture that does not compile proves nothing.
  for (const diagnostic of diagnostics) assert.ok(diagnostic.code.startsWith(`${LIBRARY}/`), `${diagnostic.code}: ${diagnostic.message}`);
  return diagnostics.map((d) => `${d.code.slice(LIBRARY.length + 1)}: ${d.message}`);
}

const forwardOrder = ["rows.tsp", "tools.tsp"];

test("determinism: two runs over the same sources report the same diagnostics in the same order", async () => {
  const first = await lint(forwardOrder);
  const second = await lint(forwardOrder);
  assert.ok(first.length >= 10, `the fixture should trip many rules, got ${first.length}`);
  assert.deepEqual(second, first);
});

test("determinism: whole-program findings do not follow file, import or declaration order", async () => {
  const forward = await lint(forwardOrder);
  // The same declarations: files registered and imported in the opposite order.
  const reversed = await lint(["tools.tsp", "rows.tsp"]);
  assert.deepEqual(reversed, forward);

  // Sorted by table model name and by declaration name, whatever the source says.
  const subjects = (rule, pattern) => forward.filter((line) => line.startsWith(`${rule}:`)).map((line) => line.match(pattern)[1]);
  assert.deepEqual(subjects("storage-column-order", /of table '(\w+)'/), ["alpha", "zeta"]);
  assert.deepEqual(subjects("storage-index-composite", /^storage-index-composite: Table '(\w+)'/), ["alpha", "zeta"]);
  assert.deepEqual(subjects("unreachable-declaration", /Tools\.(\w+)'/), ["Alpha", "Twin1", "Twin2", "Zulu"]);
  assert.deepEqual(subjects("duplicate-model-shape", /^duplicate-model-shape: Model '[\w.]+\.(\w+)'/), ["Twin2"]);
});

test("determinism: no message carries a path, a time or anything else from the machine", async () => {
  for (const line of await lint(forwardOrder)) {
    assert.doesNotMatch(line, /\/test\/|[A-Za-z]:\\|\d{4}-\d{2}-\d{2}|\d{2}:\d{2}:\d{2}/, line);
  }
});
