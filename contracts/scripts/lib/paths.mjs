// Where the contract's generated faces live. Paths resolve from this file, so every script
// writes the same files whatever directory it runs from.
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";

const repository = (path) => fileURLToPath(new URL(`../../../${path}`, import.meta.url));

/** The emitted OpenAPI document, committed. */
export const openApiPath = repository("contracts/openapi/nami.openapi.json");

/** The C# contracts: the emitter writes Nami/; the project file beside it is not generated. */
export const csharpGeneratedDirectory = repository("internal/nami.contracts/Nami");
export const csharpRevisionPath = repository("internal/nami.contracts/Nami/Api/ContractRevision.cs");

/** The MCP's generated module: TypeScript types, header names and tool schema documents. */
export const mcpGeneratedDirectory = repository("services/nami.mcp/src/contracts/generated");
export const mcpApiPath = repository("services/nami.mcp/src/contracts/generated/api.ts");
export const mcpHeadersPath = repository("services/nami.mcp/src/contracts/generated/headers.ts");
export const mcpContractSchemasPath = repository("services/nami.mcp/src/contracts/generated/contract-schemas.json");

export async function readOpenApi() {
  return JSON.parse(await readFile(openApiPath, "utf8"));
}
