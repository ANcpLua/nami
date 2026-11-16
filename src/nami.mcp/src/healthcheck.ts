#!/usr/bin/env bun
// The container's health probe. The image has no shell and no curl, so Docker runs this file with the Bun that
// already serves MCP: exit 0 when GET /health on the MCP listener answers 200, exit 1 otherwise.
import { CONFIG, CONFIG_DEFAULTS } from "./contracts/generated/manifest.js";

const bind = process.env[CONFIG.BindAddress] ?? CONFIG_DEFAULTS.BindAddress;
// A wildcard listener is reached over loopback; a listener on one address only at that address.
const host = bind === "0.0.0.0" ? "127.0.0.1" : bind === "::" ? "[::1]" : bind.includes(":") ? `[${bind}]` : bind;
const port = process.env[CONFIG.Port] ?? process.env[CONFIG.PlatformPort] ?? CONFIG_DEFAULTS.Port;
try {
  const response = await fetch(`http://${host}:${port}/health`, { signal: AbortSignal.timeout(4000) });
  process.exit(response.ok ? 0 : 1);
} catch {
  process.exit(1);
}
