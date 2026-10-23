import assert from "node:assert/strict";
import test from "node:test";
import { canonicalize } from "./canonical-schema.mjs";

test("contract revision ignores documentation, not same-named wire fields", () => {
  const schema = { description: "documentation", properties: { description: { type: "string", maxLength: 20, description: "help" }, title: { type: "string" } } };
  assert.deepEqual(canonicalize(schema), { properties: { description: { maxLength: 20, type: "string" }, title: { type: "string" } } });
});

test("a description field's validation change changes the canonical contract", () => {
  const schema = (maximum) => ({ properties: { description: { type: "string", maxLength: maximum } } });
  assert.notDeepEqual(canonicalize(schema(20)), canonicalize(schema(21)));
});
