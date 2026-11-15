#!/usr/bin/env bun
import { timingSafeEqual } from "node:crypto";
import { resolve } from "node:path";
import { createMcpHandler } from "@modelcontextprotocol/server";
import { serveStdio } from "@modelcontextprotocol/server/stdio";
import { CONFIG, CONFIG_DEFAULTS, CONTRACT_REVISION } from "./contracts/generated/manifest.js";
import type { HealthReport, ProjectKeyDocument } from "./contracts/generated/json.js";
import { Engine, EngineError } from "./engine.js";
import { dnsRebindingResponse, isLoopbackBindHost } from "./http-security.js";
import { createServer } from "./server.js";

const args = process.argv.slice(2);
const engineIndex = args.indexOf("--engine");
const enginePath = engineIndex >= 0 ? args[engineIndex + 1] : undefined;
if (!enginePath) throw new Error("Pass --engine with the published NativeAOT engine path.");
const engine = new Engine(resolve(enginePath));
await engine.ready;
const stdio = args.includes("--stdio");
// The host answers /mcp before the engine sees a call, so it repeats two engine rules on purpose: the default mode
// (CollectorAuthExtensions: Unsecured only in Development; WebApplication.CreateSlimBuilder adds DOTNET_ variables
// after ASPNETCORE_ ones, so DOTNET_ENVIRONMENT wins as here) and the key match (OtlpApiKeyValidator.Resolve: every
// key compared in fixed time). The engine refuses blank keys at startup and checks the read role of every call again,
// so a drift between the copies can refuse a call early but never admit one the engine refuses.
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

// GET /health always answers with a health report: the engine's own, or this one with 503 when the engine cannot
// give one because it has stopped, is closing, or already holds its limit of waiting requests.
function unhealthy(description: string, started: number): Response {
  const elapsed = performance.now() - started;
  const report: HealthReport = { status: "unhealthy", total_duration_ms: elapsed, contract_revision: CONTRACT_REVISION,
    entries: { engine: { status: "unhealthy", description, duration_ms: elapsed } } };
  return Response.json(report, { status: 503 });
}
async function health(signal: AbortSignal): Promise<Response> {
  const started = performance.now();
  try {
    const reply = await engine.request("health", null, null, null, signal);
    if (!reply.isError) return Response.json(reply.result);
    const result: unknown = reply.result;
    return typeof result === "object" && result !== null && "status" in result && "entries" in result
      ? Response.json(result, { status: 503 }) : unhealthy("The engine refused the health request.", started);
  } catch (error) {
    return unhealthy(error instanceof EngineError ? error.message : "The engine is unavailable.", started);
  }
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
  // A connection that stays silent is closed after the longest idle time Bun allows (its default of 10 seconds cut
  // every call that ran longer). A call being served is exempt: its client may wait for the result without a byte
  // in either direction. Event streams also carry a keep-alive comment every 5 seconds, below the 10 seconds at
  // which this server used to and many proxies do close a silent stream.
  const server = Bun.serve({ hostname, port, idleTimeout: 255, async fetch(request, listener) {
    const url = new URL(request.url);
    const refused = dnsRebindingResponse(request, [...allowedHosts], [...allowedHosts]);
    if (refused) return refused;
    if (url.pathname === "/health") return health(request.signal);
    if (url.pathname !== "/mcp") return new Response(null, { status: 404 });
    const access = credential(request);
    if (access instanceof Response) return access;
    listener.timeout(request, 0);
    // Stateless factory: both modern discovery and legacy initialize use the official SDK.
    return createMcpHandler(() => createServer(engine, access), { onerror: report, keepAliveMs: 5000 }).fetch(request);
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
// The first signal closes; a second one while that is under way ends the host at once. The handlers stay
// registered, so the second signal still exits through the "exit" listener that takes the engine with it.
function stop() {
  if (closing) process.exit(1);
  void close().catch(report);
}
process.on("SIGINT", stop);
process.on("SIGTERM", stop);
// The engine failed by itself. Until its process is gone the host keeps answering: /health with 503, tool calls
// with collector_unavailable. Then it ends non-zero, so that whatever started it starts both again.
void engine.failed.then(async (error) => {
  process.exitCode = 1;
  console.error("The native engine failed: " + error.message);
  if (!closing) {
    closing = true;
    await closeTransport().catch(report);
  }
  process.exit(1);
});
if (stdio) {
  process.stdin.once("end", () => { void close().catch(report); });
  process.stdin.resume();
}
function report(error: unknown) { console.error(error instanceof Error ? error.message : "MCP transport failure."); }
