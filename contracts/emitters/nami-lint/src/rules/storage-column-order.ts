// Smell — a table whose column orders are not 0, 1, 2, … (cleanup ledger C06).
//
// `@duckDbColumn(#{ order: n })` fixes the position of a column in the
// generated row type and therefore in the table DDL. `MetricObservationRow`
// jumps from 9 to 12: two columns were removed and their numbers were left
// behind. A gap is harmless to the generator, which only sorts by `order`, but
// it misleads the next author twice: the highest number no longer says how many
// columns the table has, and a new column given the free number 10 lands in the
// middle of an existing table instead of at its end.
//
// Two orders that are equal are reported too. The C# emitter rejects them, but
// only while emitting, so `tsp compile --no-emit` (the lint gate) never sees it.
//
// Closing a gap is mechanical and a code fix does it. Tried on 48bdb08, it
// changes two numbers in `generated/model-manifest.json`, which no build step
// reads, and leaves `StorageRows.g.cs` byte-identical. The DDL and the schema
// hash come from that file alone: the generated `[DuckDbColumn]` attribute has
// no order, and the DuckDB generator takes the column sequence from the
// property declarations (`DuckDbInsertGenerator.cs`,
// `ComparePropertyDeclarationOrder`). The fix is still not applied there,
// because the cleanup ledger asks for no renumbering outside a schema change
// and the hash could not be recomputed without .NET.

import type { CodeFix } from "@typespec/compiler";
import { createRule, defineCodeFix, getSourceLocation, paramMessage } from "@typespec/compiler";
import type { Node } from "@typespec/compiler/ast";
import type { StorageColumn } from "../contract.js";
import { compareText, storageTables } from "../contract.js";

export const renumberColumnOrdersFixId = "nami-renumber-column-orders";

function renumberFix(columns: readonly StorageColumn[]): CodeFix | undefined {
  const edits: { node: Node; order: number }[] = [];
  for (const [index, column] of columns.entries()) {
    if (column.order === index) continue;
    if (!column.orderNode) return undefined;
    edits.push({ node: column.orderNode, order: index });
  }
  return defineCodeFix({
    id: renumberColumnOrdersFixId,
    label: "Renumber the column orders 0, 1, 2, … keeping their sequence",
    fix: (fixContext) => edits.map((edit) => fixContext.replaceText(getSourceLocation(edit.node), String(edit.order))),
  });
}

export const storageColumnOrderRule = createRule({
  name: "storage-column-order",
  severity: "warning",
  description: "The column orders of a DuckDB table are exactly 0 to n-1, each used once.",
  messages: {
    gap: paramMessage`Column '${"column"}' of table '${"table"}' has order ${"order"} where ${"expected"} is next: ${"missing"} unused. A gap hides the column count and invites a new column into the middle of the table. Renumber the columns in sequence; check the storage schema hash first, because the order decides the column position in the DDL.`,
    duplicate: paramMessage`Columns '${"column"}' and '${"first"}' of table '${"table"}' both have order ${"order"}. The C# emitter rejects this, but only when it emits, so the lint gate would not see it. Give every column its own order.`,
  },
  create(context) {
    return {
      exit: () => {
        for (const table of storageTables(context.program)) {
          const columns = table.columns
            .filter((column) => column.order !== undefined)
            .sort((left, right) => left.order! - right.order! || compareText(left.columnName, right.columnName));

          let duplicate = false;
          for (const [index, column] of columns.entries()) {
            const previous = index > 0 ? columns[index - 1]! : undefined;
            if (previous?.order !== column.order) continue;
            duplicate = true;
            context.reportDiagnostic({
              messageId: "duplicate",
              target: column.property,
              format: { column: column.property.name, first: previous!.property.name, table: table.name, order: String(column.order) },
            });
          }
          // With two columns on one order there is no sequence to compare against.
          if (duplicate) continue;

          const firstGap = columns.findIndex((column, index) => column.order !== index);
          if (firstGap < 0) continue;
          const used = new Set(columns.map((column) => column.order!));
          const missing: number[] = [];
          for (let order = 0; order < columns[columns.length - 1]!.order!; order++) {
            if (!used.has(order)) missing.push(order);
          }
          const column = columns[firstGap]!;
          const fix = renumberFix(columns);
          context.reportDiagnostic({
            messageId: "gap",
            target: column.property,
            format: {
              column: column.property.name,
              table: table.name,
              order: String(column.order),
              expected: String(firstGap),
              missing: missing.length === 1 ? `order ${missing[0]} is` : `orders ${missing.join(", ")} are`,
            },
            codefixes: fix ? [fix] : undefined,
          });
        }
      },
    };
  },
});
