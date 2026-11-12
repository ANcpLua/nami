// Redundancy — a secondary index that another index of the same table makes unnecessary.
//
// Two findings, both structural and true whatever the engine does:
//
//   duplicate  the same column list is declared twice, or repeats the primary key
//   prefix     an index is a leading prefix of another index or of the primary
//              key: `ProjectId` beside `ProjectId,TraceId`
//
// For a prefix pair one of the two is not needed, and which one depends on the
// engine. If it can answer a prefix lookup from the longer index, the shorter
// one is covered. If it cannot, and DuckDB documents that only single-column
// indexes are eligible for index scans (see `storage-index-composite`), the
// longer one is not read through at all. The rule reports the pair and makes no
// claim about speed; the measurement is the team's to take (cleanup ledger C07).
//
// At 48bdb08 `spans` has two such indexes: `ProjectId`, a prefix of nine other
// indexes and of the primary key, and `ProjectId,TraceId`, a prefix of
// `ProjectId,TraceId,ParentSpanId` and of the primary key.

import { createRule, paramMessage } from "@typespec/compiler";
import type { StorageIndex } from "../contract.js";
import { storageTables } from "../contract.js";

function isPrefix(shorter: readonly string[], longer: readonly string[]): boolean {
  return shorter.length < longer.length && shorter.every((column, index) => column === longer[index]);
}

function sameColumns(left: readonly string[], right: readonly string[]): boolean {
  return left.length === right.length && left.every((column, index) => column === right[index]);
}

export const storageIndexRedundantRule = createRule({
  name: "storage-index-redundant",
  severity: "warning",
  description: "A table declares no secondary index twice and none that is a leading prefix of another index or of the primary key.",
  messages: {
    duplicate: paramMessage`Index '${"index"}' is declared ${"count"} times on table '${"table"}'. Keep one.`,
    primaryKey: paramMessage`Index '${"index"}' on table '${"table"}' has the columns of the primary key, which already has an index. Remove it.`,
    prefix: paramMessage`Index '${"index"}' on table '${"table"}' is a leading prefix of ${"covering"}. One of the two is not needed: if DuckDB answers a prefix lookup from the longer index, this one is covered; if it does not, the longer one is not read through. Measure, then keep one.`,
  },
  create(context) {
    return {
      exit: () => {
        for (const table of storageTables(context.program)) {
          const reported = new Set<string>();
          for (const [position, index] of table.indexes.entries()) {
            if (reported.has(index.text)) continue;

            const copies = table.indexes.filter((other) => sameColumns(other.columns, index.columns)).length;
            if (copies > 1) {
              reported.add(index.text);
              context.reportDiagnostic({
                messageId: "duplicate",
                target: table.indexTarget,
                format: { index: index.text, table: table.name, count: String(copies) },
              });
              continue;
            }
            if (sameColumns(index.columns, table.primaryKey)) {
              reported.add(index.text);
              context.reportDiagnostic({
                messageId: "primaryKey",
                target: table.indexTarget,
                format: { index: index.text, table: table.name },
              });
              continue;
            }

            const covering: string[] = [];
            if (isPrefix(index.columns, table.primaryKey)) covering.push(`the primary key (${table.primaryKey.join(",")})`);
            const longer = table.indexes.filter(
              (other: StorageIndex, otherPosition) => otherPosition !== position && isPrefix(index.columns, other.columns),
            );
            if (longer.length === 1) covering.push(`index '${longer[0]!.text}'`);
            else if (longer.length > 1) covering.push(`${longer.length} indexes (${longer.map((other) => `'${other.text}'`).join(", ")})`);
            if (covering.length === 0) continue;
            reported.add(index.text);
            context.reportDiagnostic({
              messageId: "prefix",
              target: table.indexTarget,
              format: { index: index.text, table: table.name, covering: covering.join(" and of ") },
            });
          }
        }
      },
    };
  },
});
