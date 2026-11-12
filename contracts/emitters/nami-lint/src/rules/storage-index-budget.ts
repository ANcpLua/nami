// Smell — more secondary indexes on one table than the budget allows.
//
// Every secondary index is a second copy of its columns that each insert,
// update and delete has to maintain; DuckDB's own guidance is "Do not define
// explicit indexes unless you have highly selective queries and enough memory
// available" (https://duckdb.org/docs/1.5/guides/performance/indexing).
// `spans` carries 19 (cleanup ledger C07); the next largest table has 5.
//
// The rule counts and compares with a budget. It claims nothing about speed.
// The budget is `STORAGE_INDEX_BUDGET` in `policy.ts` and applies to each table
// separately; one run can use another number through the 1.18.0 flag
//   --linter-rule '@ancplua/typespec-nami-lint/storage-index-budget={"maxIndexes":12}'

import { createRule, paramMessage } from "@typespec/compiler";
import { storageTables } from "../contract.js";
import { STORAGE_INDEX_BUDGET } from "../policy.js";

export const storageIndexBudgetRule = createRule({
  name: "storage-index-budget",
  severity: "warning",
  description: "A DuckDB table declares no more secondary indexes than the budget in policy.ts.",
  optionSchema: {
    type: "object",
    additionalProperties: false,
    properties: {
      maxIndexes: { type: "integer", minimum: 0 },
    },
  },
  defaultOptions: { maxIndexes: STORAGE_INDEX_BUDGET },
  messages: {
    default: paramMessage`Table '${"table"}' declares ${"count"} secondary indexes; the budget is ${"budget"}. Each one is maintained on every write. Remove the indexes no query needs, or raise the budget in policy.ts with the measurement that justifies it.`,
  },
  create(context) {
    return {
      exit: () => {
        const budget = context.options.maxIndexes;
        for (const table of storageTables(context.program)) {
          if (table.indexes.length <= budget) continue;
          context.reportDiagnostic({
            target: table.indexTarget,
            format: { table: table.name, count: String(table.indexes.length), budget: String(budget) },
          });
        }
      },
    };
  },
});
