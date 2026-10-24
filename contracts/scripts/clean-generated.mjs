import { rm } from "node:fs/promises";
import { csharpGeneratedDirectory, typescriptGeneratedDirectory, jsonSchemaDirectory, openApiPath } from "./lib/paths.mjs";
await Promise.all([csharpGeneratedDirectory, typescriptGeneratedDirectory, jsonSchemaDirectory, openApiPath].map(path => rm(path, {force:true,recursive:true})));
