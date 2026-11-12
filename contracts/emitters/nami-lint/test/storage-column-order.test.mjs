import test from "node:test";
import { renumberColumnOrdersFixId, storageColumnOrderRule as rule } from "../dist/rules/storage-column-order.js";
import { code, testRule } from "./harness.mjs";

// The shape of cleanup-ledger item C06: `MetricObservationRow` at 48bdb08 jumps from 9 to 12.
const table = (orders) => `
  namespace Nami.Models.Storage.Rows;
  @duckDbTable(#{ name: "metric_observations", conflict: "ignore" })
  model MetricObservationRow {
    @duckDbColumn(#{ order: ${orders[0]}, primaryKeyOrdinal: 0 })
    projectId: string;
    @duckDbColumn(#{ order: ${orders[1]}, sqlType: "JSON" })
    dataJson: string;
    @duckDbColumn(#{ order: ${orders[2]} })
    resourceDroppedAttributesCount?: uint32 | null;
    @duckDbColumn(#{ order: ${orders[3]} })
    scopeDroppedAttributesCount?: uint32 | null;
  }
`;

test("storage-column-order: a gap in the column orders", async () => {
  const tester = await testRule(rule);
  await tester.expect(table([0, 1, 4, 5])).toEmitDiagnostics({
    code: code(rule),
    message: /^Column 'resourceDroppedAttributesCount' of table 'metric_observations' has order 4 where 2 is next: orders 2, 3 are unused\./,
  });
});

test("storage-column-order: orders that do not start at 0", async () => {
  const tester = await testRule(rule);
  await tester.expect(table([1, 2, 3, 4])).toEmitDiagnostics({
    code: code(rule),
    message: /^Column 'projectId' of table 'metric_observations' has order 1 where 0 is next: order 0 is unused\./,
  });
});

test("storage-column-order: two columns on one order", async () => {
  const tester = await testRule(rule);
  await tester.expect(table([0, 1, 1, 2])).toEmitDiagnostics({
    code: code(rule),
    message: /^Columns 'resourceDroppedAttributesCount' and 'dataJson' of table 'metric_observations' both have order 1\./,
  });
});

test("storage-column-order: contiguous orders are fine in any declaration sequence", async () => {
  const tester = await testRule(rule);
  await tester.expect(table([0, 1, 2, 3])).toBeValid();
  await (await testRule(rule)).expect(table([3, 0, 2, 1])).toBeValid();
});

test("storage-column-order: the code fix renumbers in sequence and touches nothing else", async () => {
  const tester = await testRule(rule);
  await tester.expect(table([0, 1, 4, 5])).applyCodeFix(renumberColumnOrdersFixId).toEqual(table([0, 1, 2, 3]));
});
