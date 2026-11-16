#!/usr/bin/env bun
// The container's health probe. The image has no shell and no curl, so Docker runs this file with the Bun that
// already serves MCP: exit 0 when GET /health on the MCP listener answers 200, exit 1 otherwise.
import { readFileSync } from "node:fs";
import { CONFIG, CONFIG_DEFAULTS } from "./contracts/generated/manifest.js";

const bind = process.env[CONFIG.BindAddress] ?? CONFIG_DEFAULTS.BindAddress;
// A wildcard listener is reached over loopback; a listener on one address only at that address.
const host = bind === "0.0.0.0" ? "127.0.0.1" : bind === "::" ? "[::1]" : bind.includes(":") ? `[${bind}]` : bind;
const port = process.env[CONFIG.Port] ?? process.env[CONFIG.PlatformPort] ?? CONFIG_DEFAULTS.Port;
try {
  // With a port the operating system chose (0), only the ready manifest knows the URL.
  const ready = process.env[CONFIG.ReadyFile];
  const manifest: unknown = port === "0" && ready ? JSON.parse(readFileSync(ready, "utf8")) : null;
  const url = typeof manifest === "object" && manifest !== null && "mcp" in manifest && typeof manifest.mcp === "string"
    ? new URL("/health", manifest.mcp).href : `http://${host}:${port}/health`;
  const response = await fetch(url, { signal: AbortSignal.timeout(4000) });
  process.exit(response.ok ? 0 : 1);
} catch {
  process.exit(1);
}
