import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
const repository = (path) => fileURLToPath(new URL(`../../../${path}`, import.meta.url));
export const openApiPath = repository("contracts/openapi/nami.openapi.json");
export const csharpGeneratedDirectory = repository("internal/nami.contracts/Nami");
export const csharpRevisionPath = repository("internal/nami.contracts/Nami/Api/ContractRevision.cs");
export const jsonSchemaDirectory = repository("contracts/json-schema");
export const contractSchemasPath = repository("internal/nami.contracts/Nami/Api/Contracts/contract-schemas.json");
export async function readOpenApi() { return JSON.parse(await readFile(openApiPath, "utf8")); }
