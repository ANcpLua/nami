// =============================================================================
// @ancplua/typespec-nami-lint — the nami contract invariants, machine-checked
// =============================================================================
// Loaded by path from tspconfig.yaml (`linter.extends`), never imported by a
// .tsp file, so the contract sources carry no build-only dependency while every
// compile still enforces each invariant.
//
// Wire naming and identity
//   wire-name-snake-case          one casing convention for JSON body keys
//   http-param-snake-case         the same convention for query/path params
//   identity-binding              one identity: one scalar, one name, one key
//   identity-edge-typed           an identity edge is a declared scalar
//   confusable-identity-documented  look-alike identities declare their relation
//
// Redundancy
//   unreachable-declaration       nothing declared that nothing can consume
//   duplicate-model-shape         no model declared twice under two names
//   duplicate-member-value        no enum value or union variant listed twice
//   storage-index-redundant       no index repeated or a prefix of another
//
// Duplication
//   wire-root-twin                one message has one wire root
//   duplicate-wire-name           one JSON key per model, across inheritance
//
// Smells
//   storage-column-order          column orders are 0 to n-1
//   storage-index-composite       non-unique indexes cover one column
//   storage-index-budget          a table stays within its index budget
//
// Every rule is deterministic: same sources, same diagnostics, same order.
// =============================================================================

import { createTypeSpecLibrary, defineLinter } from "@typespec/compiler";
import { confusableIdentityDocumentedRule } from "./rules/confusable-identity-documented.js";
import { duplicateMemberValueRule } from "./rules/duplicate-member-value.js";
import { duplicateModelShapeRule } from "./rules/duplicate-model-shape.js";
import { duplicateWireNameRule } from "./rules/duplicate-wire-name.js";
import { httpParamSnakeCaseRule } from "./rules/http-param-snake-case.js";
import { identityBindingRule } from "./rules/identity-binding.js";
import { identityEdgeTypedRule } from "./rules/identity-edge-typed.js";
import { storageColumnOrderRule } from "./rules/storage-column-order.js";
import { storageIndexBudgetRule } from "./rules/storage-index-budget.js";
import { storageIndexCompositeRule } from "./rules/storage-index-composite.js";
import { storageIndexRedundantRule } from "./rules/storage-index-redundant.js";
import { unreachableDeclarationRule } from "./rules/unreachable-declaration.js";
import { wireNameSnakeCaseRule } from "./rules/wire-name-snake-case.js";
import { wireRootTwinRule } from "./rules/wire-root-twin.js";

export const LIBRARY_NAME = "@ancplua/typespec-nami-lint";

export const $lib = createTypeSpecLibrary({
  name: LIBRARY_NAME,
  diagnostics: {},
} as const);

const wireRules = [
  wireNameSnakeCaseRule,
  httpParamSnakeCaseRule,
  identityBindingRule,
  identityEdgeTypedRule,
  confusableIdentityDocumentedRule,
  wireRootTwinRule,
  duplicateWireNameRule,
  duplicateMemberValueRule,
];

const storageRules = [
  storageColumnOrderRule,
  storageIndexRedundantRule,
  storageIndexCompositeRule,
  storageIndexBudgetRule,
];

const hygieneRules = [unreachableDeclarationRule, duplicateModelShapeRule];

/** Every rule, in the order its diagnostics are reported. */
export const rules = [...wireRules, ...storageRules, ...hygieneRules];

const enable = (selected: readonly { name: string }[]) =>
  Object.fromEntries(selected.map((rule) => [`${LIBRARY_NAME}/${rule.name}`, true]));

export const $linter = defineLinter({
  rules,
  // `all` is the gate. The narrower sets are for a focused run while editing one
  // area: `tsp compile main.tsp --no-emit --linter-ruleset @ancplua/typespec-nami-lint/storage`.
  ruleSets: {
    all: { enable: enable(rules) },
    wire: { enable: enable(wireRules) },
    storage: { enable: enable(storageRules) },
  },
});

export * from "./policy.js";
export { wireNameFixId } from "./rules/wire-name-snake-case.js";
export { renumberColumnOrdersFixId } from "./rules/storage-column-order.js";
