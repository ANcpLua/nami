import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
const repository = (path) => fileURLToPath(new URL(`../../../${path}`, import.meta.url));
export const openApiPath = repository("contracts/openapi/nami.openapi.json");
export const csharpGeneratedDirectory = repository("internal/nami.contracts/Nami");
export const csharpRevisionPath = repository("internal/nami.contracts/Nami/Api/ContractRevision.cs");
export const jsonSchemaDirectory = repository("contracts/json-schema");
export const contractSchemasPath = repository("internal/nami.contracts/Nami/Api/Contracts/contract-schemas.json");
export const typescriptGeneratedDirectory = repository("src/nami.mcp/src/contracts/generated");
export const isGeneratedConsumerPath = (path) => typeof path === "string" &&
  /^(?:src|internal)\/[A-Za-z_][A-Za-z0-9_.-]*\/(?:[A-Za-z_][A-Za-z0-9_.-]*\/)*Generated\/[A-Za-z_][A-Za-z0-9_.-]*\.g\.cs$/u.test(path);
export async function readOpenApi() { return JSON.parse(await readFile(openApiPath, "utf8")); }
