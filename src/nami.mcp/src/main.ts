#!/usr/bin/env bun
import { timingSafeEqual } from "node:crypto";
import { resolve } from "node:path";
import { createMcpHandler } from "@modelcontextprotocol/server";
import { serveStdio } from "@modelcontextprotocol/server/stdio";
import { CONFIG, CONFIG_DEFAULTS } from "./contracts/generated/manifest.js";
import type { ProjectKeyDocument } from "./contracts/generated/json.js";
import { Engine } from "./engine.js";
import { dnsRebindingResponse, isLoopbackBindHost } from "./http-security.js";
import { createServer } from "./server.js";

const args = process.argv.slice(2);
const engineIndex = args.indexOf("--engine");
const enginePath = engineIndex >= 0 ? args[engineIndex + 1] : undefined;
if (!enginePath) throw new Error("Pass --engine with the published NativeAOT engine path.");
const engine = new Engine(resolve(enginePath));
await engine.ready;
const stdio = args.includes("--stdio");
const environment = process.env.DOTNET_ENVIRONMENT ?? process.env.ASPNETCORE_ENVIRONMENT;
const mode = process.env[CONFIG.AuthMode] ?? (environment?.toLowerCase() === "development" ? CONFIG_DEFAULTS.AuthMode : "ApiKey");
const keyDocument: unknown = JSON.parse(process.env[CONFIG.ProjectKeys] ?? "{}");
// Engine readiness follows validation of this shared credential configuration.
const keys = Object.values(keyDocument as ProjectKeyDocument)
  .flat().map((entry) => ({ bytes: Buffer.from(entry.key), read: entry.roles.includes("read") }));

function credential(request: Request): string | null | Response {
  if (mode.toLowerCase() !== "apikey") return null;
  const supplied = request.headers.get("x-otlp-api-key");
  const bytes = Buffer.from(supplied ?? "");
  let match: (typeof keys)[number] | undefined;
  for (const key of keys) if (bytes.length === key.bytes.length && timingSafeEqual(bytes, key.bytes)) match = key;
  return !match || !match.read ? new Response(null, { status: match ? 403 : 401,
    headers: { "WWW-Authenticate": 'x-otlp-api-key realm="nami"' } }) : supplied;
}

let closeTransport: () => Promise<void>;
if (stdio) {
  const handle = serveStdio(() => createServer(engine), { onerror: report });
  closeTransport = () => handle.close();
} else {
  const hostname = process.env[CONFIG.BindAddress] ?? CONFIG_DEFAULTS.BindAddress;
  const port = Number(process.env[CONFIG.Port] ?? process.env[CONFIG.PlatformPort] ?? CONFIG_DEFAULTS.Port);
  if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error("NAMI_PORT is outside its supported range.");
  const domain = process.env[CONFIG.RailwayPublicDomain];
  const publicUrl = process.env[CONFIG.PublicUrl] ?? (domain ? `https://${domain}` : undefined);
  const allowedHosts = new Set(["localhost", "127.0.0.1", "[::1]", hostname]);
  if (publicUrl) {
    const origin = new URL(publicUrl);
    if (origin.protocol !== "https:" || origin.username || origin.password || origin.search || origin.hash || origin.pathname !== "/")
      throw new Error("NAMI_PUBLIC_URL must be an HTTPS origin without credentials, query or path.");
    allowedHosts.add(origin.hostname);
  }
  if (!isLoopbackBindHost(hostname) && !publicUrl)
    throw new Error("A non-loopback listener requires NAMI_PUBLIC_URL.");
  const server = Bun.serve({ hostname, port, async fetch(request) {
    const url = new URL(request.url);
    const refused = dnsRebindingResponse(request, [...allowedHosts], [...allowedHosts]);
    if (refused) return refused;
    if (url.pathname === "/health") {
      const reply = await engine.request("health", null, null, null, request.signal);
      return Response.json(reply.result, { status: reply.isError ? 503 : 200 });
    }
    if (url.pathname !== "/mcp") return new Response(null, { status: 404 });
    const access = credential(request);
    if (access instanceof Response) return access;
    // Stateless factory: both modern discovery and legacy initialize use the official SDK.
    return createMcpHandler(() => createServer(engine, access), { onerror: report }).fetch(request);
  } });
  closeTransport = async () => { await server.stop(true); };
}
let closing = false;
async function close() {
  if (closing) return;
  closing = true;
  await closeTransport();
  await engine.close();
}
process.once("SIGINT", () => { void close().catch(report); });
process.once("SIGTERM", () => { void close().catch(report); });
process.stdin.once("end", () => { void close().catch(report); });
process.stdin.resume();
function report(error: unknown) { console.error(error instanceof Error ? error.message : "MCP transport failure."); }
