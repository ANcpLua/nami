// Smell — a multi-column secondary index, which DuckDB documents as not eligible for index scans.
//
// The DuckDB performance guide says, in the documentation for 1.3, 1.4, 1.5
// and 2.0 (nami runs 1.5.x through DuckDB.NET 1.5.6.1):
//
//   "Indexes are only eligible for index scans if they index a single column
//    without expressions. […] the following two indexes are NOT eligible for
//    index scans: CREATE INDEX idx_multi_column ON tbl (col1, col2); …"
//    https://duckdb.org/docs/1.5/guides/performance/indexing#art-index-scans
//
// A non-unique index enforces nothing, so by that page a non-unique index over
// two or more columns is written on every change and read by no query. At
// 48bdb08 seven tables declare 19 of them, nine on `spans` (cleanup ledger C07).
//
// The rule checks structure and cites the page. It does not measure anything
// and states no speed: the message asks for `EXPLAIN ANALYZE` before an index
// is removed, and a team that measures otherwise disables the rule with its
// reason in `tspconfig.yaml`. A unique index is exempt, because it carries a
// constraint whether or not it is ever scanned.

import { createRule, paramMessage } from "@typespec/compiler";
import { storageTables } from "../contract.js";
import { DUCKDB_INDEX_SCAN_URL } from "../policy.js";

export const storageIndexCompositeRule = createRule({
  name: "storage-index-composite",
  severity: "warning",
  description: "A non-unique secondary index covers one column, the only kind DuckDB documents as eligible for index scans.",
  url: DUCKDB_INDEX_SCAN_URL,
  messages: {
    default: paramMessage`Table '${"table"}' declares ${"count"} over more than one column: ${"indexes"}. DuckDB documents that only single-column indexes are eligible for index scans (${"url"}), and a non-unique index enforces no constraint, so by that page ${"these"} written on every change and never read through. Confirm with EXPLAIN ANALYZE, then drop ${"them"} or keep ${"them"} with the measurement as the reason.`,
  },
  create(context) {
    return {
      exit: () => {
        for (const table of storageTables(context.program)) {
          const composite = table.indexes.filter((index) => !index.unique && index.columns.length > 1);
          if (composite.length === 0) continue;
          const one = composite.length === 1;
          context.reportDiagnostic({
            target: table.indexTarget,
            format: {
              table: table.name,
              count: one ? "1 non-unique index" : `${composite.length} non-unique indexes`,
              indexes: composite.map((index) => `'${index.text}'`).join(", "),
              url: DUCKDB_INDEX_SCAN_URL,
              these: one ? "it is" : "they are",
              them: one ? "it" : "them",
            },
          });
        }
      },
    };
  },
});
