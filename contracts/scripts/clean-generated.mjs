import { rm } from "node:fs/promises";
import { csharpGeneratedDirectory, jsonSchemaDirectory, openApiPath } from "./lib/paths.mjs";
await Promise.all([csharpGeneratedDirectory, jsonSchemaDirectory, openApiPath].map(path => rm(path, {force:true,recursive:true})));
