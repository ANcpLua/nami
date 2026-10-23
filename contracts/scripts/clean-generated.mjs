// Remove every generated output before a compile, so a type or file the contract no longer
// declares cannot survive in the committed tree.
import { rm } from "node:fs/promises";
import { csharpGeneratedDirectory, mcpGeneratedDirectory, openApiPath } from "./lib/paths.mjs";

await Promise.all(
  [csharpGeneratedDirectory, mcpGeneratedDirectory, openApiPath].map((path) => rm(path, { force: true, recursive: true })),
);
