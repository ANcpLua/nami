// Validate client-observed MCP data against the generated Nami contract.
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { TOOLS, CONFIG, PRODUCT_VERSION, CONTRACT_REVISION } from "../../src/nami.mcp/src/contracts/generated/manifest.ts";

const require = createRequire(new URL("../../src/nami.mcp/package.json", import.meta.url));
const { fromJsonSchema } = await import(require.resolve("@modelcontextprotocol/server"));
const schemas = JSON.parse(readFileSync(new URL("../../internal/nami.contracts/Nami/Api/Contracts/contract-schemas.json", import.meta.url)));
const canonical = value => Array.isArray(value) ? value.map(canonical) : value && typeof value === "object"
  ? Object.fromEntries(Object.keys(value).sort().map(key => [key, canonical(value[key])])) : value;
const equal = (left, right) => JSON.stringify(canonical(left)) === JSON.stringify(canonical(right));
const [mode, path, era] = process.argv.slice(2);
const findings = [];
const fail = (rule, message) => findings.push({ level: "error", lane: "protocol", rule, path: "<mcp>", line: 1, column: 1, message });

if (mode === "metadata") {
  console.log(JSON.stringify({ config: CONFIG, version: PRODUCT_VERSION, revision: CONTRACT_REVISION,
    tools: TOOLS.map(tool => tool.name), probe: TOOLS.find(tool => tool.name === "sources_status")?.name }));
} else {
  const value = JSON.parse(readFileSync(path, "utf8"));
  if (mode === "connection") {
    if (value.pendingAuth || value.elicitationPending) fail("MCP_INTERACTION_REQUIRED", "The local probe unexpectedly requires interactive authorization.");
    if (value.protocolEra !== era) fail("MCP_ERA_MISMATCH", `Expected ${era}, received ${value.protocolEra}.`);
    if (value.serverInfo?.name !== "nami" || value.serverInfo?.version !== PRODUCT_VERSION)
      fail("MCP_SERVER_IDENTITY", "Server identity differs from generated product metadata.");
    if (!value.protocolVersion || (era === "modern" && value.protocolVersion !== "2026-07-28"))
      fail("MCP_PROTOCOL_VERSION", `Unexpected negotiated protocol version ${value.protocolVersion}.`);
  } else if (mode === "catalog") {
    const actual = Array.isArray(value.tools) ? value.tools : [];
    if (!equal(actual.map(tool => tool.name).sort(), TOOLS.map(tool => tool.name).sort()))
      fail("MCP_TOOL_SET", "The advertised tool set differs from the generated six-tool manifest.");
    for (const expected of TOOLS) {
      const tool = actual.find(tool => tool.name === expected.name);
      if (!tool) continue;
      for (const [property, model] of [["inputSchema", expected.input], ["outputSchema", expected.output]])
        if (!equal(tool[property], schemas[model])) fail("MCP_SCHEMA_MISMATCH", `${expected.name}.${property} differs from ${model}.`);
      if (tool.description !== expected.description) fail("MCP_DESCRIPTION_MISMATCH", `${expected.name} description differs from TypeSpec.`);
      const annotations = { readOnlyHint: true, idempotentHint: true, destructiveHint: false, openWorldHint: false };
      for (const [key, expectedValue] of Object.entries(annotations))
        if (tool.annotations?.[key] !== expectedValue) fail("MCP_ANNOTATION_MISMATCH", `${expected.name}.${key} differs.`);
    }
  } else if (mode === "call") {
    const tool = TOOLS.find(tool => tool.name === "sources_status");
    if (!tool) throw new Error("The generated manifest has no sources_status probe.");
    if (value.isError === true || !value.structuredContent) fail("MCP_TOOL_CALL", "The sources_status probe did not return successful structured content.");
    else {
      const result = await fromJsonSchema(schemas[tool.output])["~standard"].validate(value.structuredContent);
      if (result.issues) fail("MCP_RESULT_SCHEMA", JSON.stringify(result.issues));
      try {
        if (!Array.isArray(value.content) || value.content.length !== 2 || value.content.some(part => part.type !== "text")
          || !value.content[0].text || !equal(JSON.parse(value.content[1].text), value.structuredContent))
          fail("MCP_RESULT_FRAME", "Text and structured result frames differ.");
      } catch { fail("MCP_RESULT_FRAME", "The text frame is not valid JSON."); }
    }
    if (value._meta?.contract_revision !== CONTRACT_REVISION) fail("MCP_CONTRACT_REVISION", "Tool result has a different contract revision.");
  } else throw new Error(`Unknown evidence mode ${mode}`);
  console.log(JSON.stringify({ findings, observed: mode === "connection" ? { era: value.protocolEra, version: value.protocolVersion } : undefined }));
}
