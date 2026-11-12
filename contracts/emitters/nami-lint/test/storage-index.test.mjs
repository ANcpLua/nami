import test from "node:test";
import { storageIndexBudgetRule as budget } from "../dist/rules/storage-index-budget.js";
import { storageIndexCompositeRule as composite } from "../dist/rules/storage-index-composite.js";
import { storageIndexRedundantRule as redundant } from "../dist/rules/storage-index-redundant.js";
import { STORAGE_INDEX_BUDGET } from "../dist/policy.js";
import { code, testRule } from "./harness.mjs";

// A table keyed like `spans` at 48bdb08 (cleanup-ledger item C07): primary key (ProjectId, TraceId, SpanId).
const spans = (options) => `
  namespace Nami.Models.Storage.Rows;
  @duckDbTable(#{ name: "spans", ${options} })
  model SpanStorageRow {
    @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0 }) projectId: string;
    @duckDbColumn(#{ order: 1, primaryKeyOrdinal: 2 }) spanId: string;
    @duckDbColumn(#{ order: 2, primaryKeyOrdinal: 1 }) traceId: string;
    @duckDbColumn(#{ order: 3 }) parentSpanId?: string | null;
    @duckDbColumn(#{ order: 4 }) sessionId?: string | null;
    @duckDbColumn(#{ order: 5 }) serviceName?: string | null;
  }
`;

test("storage-index-redundant: an index declared twice", async () => {
  const tester = await testRule(redundant);
  await tester.expect(spans(`indexes: "SessionId;ServiceName;SessionId"`)).toEmitDiagnostics({
    code: code(redundant),
    message: "Index 'SessionId' is declared 2 times on table 'spans'. Keep one.",
  });
});

test("storage-index-redundant: an index with the columns of the primary key", async () => {
  const tester = await testRule(redundant);
  await tester.expect(spans(`indexes: "SessionId", uniqueIndexes: "ProjectId,TraceId,SpanId"`)).toEmitDiagnostics({
    code: code(redundant),
    message: "Index 'ProjectId,TraceId,SpanId' on table 'spans' has the columns of the primary key, which already has an index. Remove it.",
  });
});

test("storage-index-redundant: an index that is a leading prefix of another index and of the primary key", async () => {
  const tester = await testRule(redundant);
  await tester.expect(spans(`indexes: "ProjectId;ProjectId,SessionId;SessionId"`)).toEmitDiagnostics({
    code: code(redundant),
    message: /^Index 'ProjectId' on table 'spans' is a leading prefix of the primary key \(ProjectId,TraceId,SpanId\) and of index 'ProjectId,SessionId'\. One of the two is not needed/,
  });
});

test("storage-index-redundant: a different column sequence is a different index", async () => {
  const tester = await testRule(redundant);
  // `TraceId,ProjectId` is no prefix of the key (ProjectId first), and `SessionId` no prefix of `ServiceName,SessionId`.
  await tester.expect(spans(`indexes: "TraceId,ProjectId;SessionId;ServiceName,SessionId"`)).toBeValid();
});

test("storage-index-composite: non-unique indexes over more than one column", async () => {
  const tester = await testRule(composite);
  await tester.expect(spans(`indexes: "SessionId;ProjectId,SessionId;ProjectId,ServiceName"`)).toEmitDiagnostics({
    code: code(composite),
    message: /^Table 'spans' declares 2 non-unique indexes over more than one column: 'ProjectId,SessionId', 'ProjectId,ServiceName'\. DuckDB documents that only single-column indexes are eligible for index scans \(https:\/\/duckdb\.org\/docs\/1\.5\/guides\/performance\/indexing#art-index-scans\)/,
  });
});

test("storage-index-composite: single-column indexes and unique indexes are fine", async () => {
  const tester = await testRule(composite);
  await tester.expect(spans(`indexes: "SessionId;ServiceName", uniqueIndexes: "ProjectId,SessionId"`)).toBeValid();
});

const many = (count) =>
  spans(`indexes: "${["TraceId", "SpanId", "ParentSpanId", "SessionId", "ServiceName", "ProjectId", "TraceId,SessionId", "SpanId,SessionId", "SessionId,ServiceName", "ServiceName,TraceId"].slice(0, count).join(";")}"`);

test("storage-index-budget: more secondary indexes than the budget in policy.ts", async () => {
  const tester = await testRule(budget);
  await tester.expect(many(STORAGE_INDEX_BUDGET + 1)).toEmitDiagnostics({
    code: code(budget),
    message: `Table 'spans' declares ${STORAGE_INDEX_BUDGET + 1} secondary indexes; the budget is ${STORAGE_INDEX_BUDGET}. Each one is maintained on every write. Remove the indexes no query needs, or raise the budget in policy.ts with the measurement that justifies it.`,
  });
});

test("storage-index-budget: a table at the budget, and a table without indexes, are fine", async () => {
  await (await testRule(budget)).expect(many(STORAGE_INDEX_BUDGET)).toBeValid();
  await (await testRule(budget)).expect(spans(`conflict: "ignore"`)).toBeValid();
});
