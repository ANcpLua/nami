#!/usr/bin/env bun
// The container's health probe. The image has no shell and no curl, so Docker runs this file with the Bun that
// already serves MCP: exit 0 when GET /health on the MCP listener answers 200, exit 1 otherwise, with the reason
// on standard error (Docker keeps it in the container's health log).
import { readFileSync } from "node:fs";
import { CONFIG, CONFIG_DEFAULTS } from "./contracts/generated/manifest.js";
import { configuredPort } from "./port.js";

function healthUrl(): string {
  const port = configuredPort(process.env);
  if (port !== 0) {
    const bind = process.env[CONFIG.BindAddress] ?? CONFIG_DEFAULTS.BindAddress;
    // A wildcard listener is reached over loopback; a listener on one address only at that address.
    const host = bind === "0.0.0.0" ? "127.0.0.1" : bind === "::" ? "[::1]" : bind.includes(":") ? `[${bind}]` : bind;
    return `http://${host}:${port}/health`;
  }
  // The operating system chose the port (0, however it is written): only the ready manifest knows the URL. The
  // host does not start with such a port and no manifest, so the file is named whenever a host can be running.
  const ready = process.env[CONFIG.ReadyFile];
  if (!ready) throw new Error(`${CONFIG.Port}=0 leaves the port to the operating system; set ${CONFIG.ReadyFile} so that the ready manifest names it.`);
  const manifest: unknown = JSON.parse(readFileSync(ready, "utf8"));
  if (typeof manifest !== "object" || manifest === null || !("mcp" in manifest) || typeof manifest.mcp !== "string")
    throw new Error(`The ready manifest ${ready} names no MCP URL.`);
  return new URL("/health", manifest.mcp).href;
}

try {
  const url = healthUrl();
  const response = await fetch(url, { signal: AbortSignal.timeout(4000) });
  if (!response.ok) throw new Error(`GET ${url} answered ${response.status}.`);
  process.exit(0);
} catch (error) {
  console.error(error instanceof Error ? error.message : "The health probe failed.");
  process.exit(1);
}
