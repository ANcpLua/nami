import test from "node:test";
import assert from "node:assert/strict";
import { isGeneratedConsumerPath } from "./paths.mjs";

test("consumer outputs permit nested generated folders inside a source project", () => {
  for (const path of ["src/nami/Generated/HostingOptions.g.cs", "src/nami.collector.ingestion/Configuration/Generated/CorsOptions.g.cs", "internal/nami.contracts/Generated/ContractIssue.g.cs"])
    assert.equal(isGeneratedConsumerPath(path), true, path);
});

test("consumer cleanup refuses traversal and non-generated or external paths", () => {
  for (const path of ["src/../Generated/Model.g.cs", "src/nami/../../Generated/Model.g.cs", "src/nami/Generated/../Model.g.cs",
    "src/nami/%2e%2e/Generated/Model.g.cs", "src/nami/Generated/Model.cs", "src/nami/Model.g.cs",
    "/tmp/Generated/Model.g.cs", "src\\nami\\Generated\\Model.g.cs", null])
    assert.equal(isGeneratedConsumerPath(path), false, String(path));
});
