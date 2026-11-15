import test from "node:test";
import { duplicateModelShapeRule as rule } from "../dist/rules/duplicate-model-shape.js";
import { code, testRule } from "./harness.mjs";

test("duplicate-model-shape: two independent models with the same properties", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Api.Contracts.Tools;
    model LogHit { @encodedName("application/json", "trace_id") traceId?: string; body: string; }
    model LogMatch { body: string; @encodedName("application/json", "trace_id") trace?: string; }
  `).toEmitDiagnostics({
    code: code(rule),
    message: "Model 'Nami.Api.Contracts.Tools.LogMatch' declares the same 2 properties as 'Nami.Api.Contracts.Tools.LogHit': the same wire names, types and optionality. Use 'Nami.Api.Contracts.Tools.LogHit', or derive one from the other (extends, spread or 'is') so the relationship is stated and cannot drift.",
  });
});

test("duplicate-model-shape: every later copy is reported against the first", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Models.Rows;
    model C { key: string; value: int64; }
    model A { key: string; value: int64; }
    model B { key: string; value: int64; }
  `).toEmitDiagnostics([
    { code: code(rule), message: /^Model 'Nami\.Models\.Rows\.B' declares the same 2 properties as 'Nami\.Models\.Rows\.A'/ },
    { code: code(rule), message: /^Model 'Nami\.Models\.Rows\.C' declares the same 2 properties as 'Nami\.Models\.Rows\.A'/ },
  ]);
});

test("duplicate-model-shape: a different type, optionality or wire name is a different shape", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Api.Contracts.Tools;
    model A { key: string; value: int64; }
    model B { key: string; value: int32; }
    model C { key: string; value?: int64; }
    model D { key: string; @encodedName("application/json", "v") value: int64; }
  `).toBeValid();
});

// The three deliberate mirrors that exist in the contract at 48bdb08.
test("duplicate-model-shape: declared derivations and projection pairs are not copies", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Api.Contracts.Tools {
      model ToolReadInput { service?: string; since?: string; }
      model SourcesStatusInput extends ToolReadInput {}
      model ErrorsRecentInput extends ToolReadInput {}
    }
    namespace Nami.Models.Storage {
      model StoredQuantileValue { quantile: float64; value: float64; }
      model StoredBuckets { offset: int32; counts: int64[]; }
    }
    namespace Nami.Models.Ingestion {
      model MetricQuantileValue { ...Nami.Models.Storage.StoredQuantileValue; }
      model MetricBuckets { offset: int32; counts: int64[]; }
      @@storageProjection(MetricBuckets, Nami.Models.Storage.StoredBuckets);
    }
  `).toBeValid();
});

test("duplicate-model-shape: one-property models are too small to call copies", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Models.Runtime;
    model EventValues { attributes: string[]; }
    model LinkValues { attributes: string[]; }
  `).toBeValid();
});

test("duplicate-model-shape: two tables with the same columns are still two tables", async () => {
  const tester = await testRule(rule);
  await tester.expect(`
    namespace Nami.Models.Storage.Rows;
    @duckDbTable(#{ name: "hot" })
    model HotRow { @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0 }) id: string; @duckDbColumn(#{ order: 1 }) body: string; }
    @duckDbTable(#{ name: "archive" })
    model ArchiveRow { @duckDbColumn(#{ order: 0, primaryKeyOrdinal: 0 }) id: string; @duckDbColumn(#{ order: 1 }) body: string; }
  `).toBeValid();
});
