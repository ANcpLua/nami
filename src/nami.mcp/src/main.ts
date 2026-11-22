#!/usr/bin/env bun
import { timingSafeEqual } from "node:crypto";
import { resolve } from "node:path";
import { createMcpHandler } from "@modelcontextprotocol/server";
import { serveStdio } from "@modelcontextprotocol/server/stdio";
import { CONFIG, CONFIG_DEFAULTS, CONTRACT_REVISION } from "./contracts/generated/manifest.js";
import type { HealthReport, ProjectKeyDocument } from "./contracts/generated/json.js";
import { Engine, EngineError } from "./engine.js";
import { dnsRebindingResponse, isLoopbackBindHost, isWildcardHost, requestNames } from "./http-security.js";
import { configuredPort } from "./port.js";
import { removeReadyManifest, writeReadyManifest } from "./ready.js";
import { createServer } from "./server.js";
import { stdioTransport } from "./stdio.js";

const args = process.argv.slice(2);
const engineIndex = args.indexOf("--engine");
const enginePath = engineIndex >= 0 ? args[engineIndex + 1] : undefined;
if (!enginePath) throw new Error("Pass --engine with the published NativeAOT engine path.");
const stdio = args.includes("--stdio");
// The ready manifest (ready.ts). One left behind by a nami that was killed is removed before the engine starts,
// so whoever waits for the file never reads the addresses of an earlier run.
const readyIndex = args.indexOf("--ready-file");
const readyOption = readyIndex >= 0 ? args[readyIndex + 1] : process.env[CONFIG.ReadyFile];
if (readyIndex >= 0 && !readyOption) throw new Error("Pass a file path after --ready-file.");
const readyFile = readyOption ? resolve(readyOption) : undefined;
if (readyFile) removeReadyManifest(readyFile);
// The host answers /mcp before the engine sees a call, so it repeats two engine rules on purpose: the default mode
// (CollectorAuthExtensions: Unsecured only in Development; WebApplication.CreateSlimBuilder adds DOTNET_ variables
// after ASPNETCORE_ ones, so DOTNET_ENVIRONMENT wins as here) and the key match (OtlpApiKeyValidator.Resolve: every
// key compared in fixed time). The engine refuses blank keys at startup and checks the read role of every call again,
// so a drift between the copies can refuse a call early but never admit one the engine refuses.
const environment = process.env.DOTNET_ENVIRONMENT ?? process.env.ASPNETCORE_ENVIRONMENT;
const mode = process.env[CONFIG.AuthMode] ?? (environment?.toLowerCase() === "development" ? CONFIG_DEFAULTS.AuthMode : "ApiKey");
// Stdio carries no credential, and in ApiKey mode the engine answers every call without one "unauthorized".
if (stdio && mode.toLowerCase() === "apikey")
  throw new Error(`--stdio cannot be used with ${CONFIG.AuthMode}=ApiKey: stdio carries no project key, so every tool call would be refused. `
    + `Serve Streamable HTTP and send a project's read key in x-otlp-api-key, or set ${CONFIG.AuthMode}=Unsecured for a local engine with the default project.`);
// The engine reads the same variable and refuses the same values (CollectorStopOptions.cs): one spelling for
// both, decimal digits and nothing else. Number() alone also takes 0x10, 1e1 and 10.0, which the engine does not.
const stopBudgetText = process.env[CONFIG.StopBudgetSeconds] ?? CONFIG_DEFAULTS.StopBudgetSeconds;
const stopBudget = /^[0-9]+$/.test(stopBudgetText) ? Number(stopBudgetText) : Number.NaN;
if (!Number.isInteger(stopBudget) || stopBudget < 1 || stopBudget > 3600)
  throw new Error(`${CONFIG.StopBudgetSeconds} must be a whole number of seconds from 1 to 3600. Write it in decimal digits only.`);
const engine = new Engine(resolve(enginePath), stopBudget * 1000);
await engine.ready;
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

// Ends the transport: at once, or (force false, HTTP only) after the requests being answered have their answers.
let closeTransport: (force?: boolean) => Promise<void>;
// The MCP URL of the ready manifest; stdio has none.
let mcpUrl: string | null = null;
if (stdio) {
  const handle = serveStdio(() => createServer(engine), { onerror: report, transport: stdioTransport(report) });
  closeTransport = () => handle.close();
} else {
  const hostname = process.env[CONFIG.BindAddress] ?? CONFIG_DEFAULTS.BindAddress;
  // Port 0 lets the operating system choose; the ready manifest then names the port. Without a manifest nobody
  // could find the listener, and the container's health probe (healthcheck.ts) would fail for good.
  const port = configuredPort(process.env);
  if (port === 0 && !readyFile)
    throw new Error(`${CONFIG.Port}=0 leaves the port to the operating system; name a ready manifest with --ready-file or ${CONFIG.ReadyFile}, which is where the port is written.`);
  const domain = process.env[CONFIG.RailwayPublicDomain];
  const publicUrl = process.env[CONFIG.PublicUrl] ?? (domain ? `https://${domain}` : undefined);
  const localOrigins = ["localhost", "127.0.0.1", "[::1]"];
  let publicOrigin: URL | undefined;
  if (publicUrl) {
    const origin = new URL(publicUrl);
    // Plain HTTP is an origin only for this machine itself: a container whose MCP port is published on the host's
    // loopback (-p 127.0.0.1:5100:5100) is reached at http://127.0.0.1:5100. Anything else needs TLS in front.
    const loopback = origin.protocol === "http:" && localOrigins.includes(origin.hostname);
    if ((origin.protocol !== "https:" && !loopback) || origin.username || origin.password || origin.search || origin.hash || origin.pathname !== "/")
      throw new Error("NAMI_PUBLIC_URL must be an HTTPS origin, or an HTTP origin on localhost, 127.0.0.1 or [::1], without credentials, query or path.");
    if (isWildcardHost(origin.hostname))
      throw new Error("NAMI_PUBLIC_URL names a wildcard address; name the host at which clients reach MCP.");
    publicOrigin = origin;
  }
  if (!isLoopbackBindHost(hostname) && !publicUrl)
    throw new Error("A non-loopback listener requires NAMI_PUBLIC_URL.");
  const names = requestNames(hostname, publicOrigin);
  // A connection that stays silent is closed after the longest idle time Bun allows (its default of 10 seconds cut
  // every call that ran longer). A call being served is exempt, and for revision 2026-07-28 it has to be: the
  // SDK's per-request transport answers such a call with application/json once the result exists, so its client
  // gets no byte, not even the status line, before that (measured with the engine stopped: 0 bytes in 17
  // seconds). A proxy in front of nami must allow such a call a silent connection for as long as it may take.
  // A call without a version header is answered as an event stream: its headers go out at once and a keep-alive
  // comment follows every 5 seconds (keepAliveMs below), under the 10 seconds at which this server used to and
  // many proxies do close a silent stream.
  // Nothing this listener answers carries a path or a stack: no development error page, and an unexpected
  // failure is a JSON-RPC internal error.
  const failure = (status: number, code: number, message: string) =>
    Response.json({ jsonrpc: "2.0", error: { code, message }, id: null }, { status });
  const server = Bun.serve({
    hostname, port, idleTimeout: 255, development: false,
    error(error) { report(error); return failure(500, -32603, "Internal error."); },
    async fetch(request, listener) {
      // Host and Origin are judged before the URL is read: with a malformed Host header the URL does not parse.
      let url: URL;
      try {
        const refused = dnsRebindingResponse(request, names);
        if (refused) return refused;
        url = new URL(request.url);
      } catch { return failure(400, -32600, "Invalid Host header or request target."); }
      if (url.pathname === "/health") return health(request.signal);
      if (url.pathname !== "/mcp") return new Response(null, { status: 404 });
      const access = credential(request);
      if (access instanceof Response) return access;
      listener.timeout(request, 0);
      // Stateless factory: both modern discovery and legacy initialize use the official SDK.
      return createMcpHandler(() => createServer(engine, access), { onerror: report, keepAliveMs: 5000 }).fetch(request);
    }
  });
  closeTransport = async (force = true) => { await server.stop(force); };
  // A wildcard listener is reached over loopback.
  const reachable = hostname === "0.0.0.0" ? "127.0.0.1" : hostname === "::" ? "::1" : hostname;
  mcpUrl = `http://${reachable.includes(":") ? `[${reachable}]` : reachable}:${server.port}/mcp`;
}
// Ready: the engine has reported the addresses it bound, and the MCP listener (if any) is bound.
if (readyFile) writeReadyManifest(readyFile, mcpUrl, await engine.listeners);
let closing = false;
// Removes the ready manifest when nami stops. A manifest that cannot be removed is reported on standard error
// and left behind; it never stops the stop.
function forgetReady(file: string) {
  try { removeReadyManifest(file); } catch (error) { report(error); }
}
async function close() {
  if (closing) return;
  closing = true;
  if (readyFile) forgetReady(readyFile);
  // The engine is told to stop whatever the transport does while it closes.
  try { await closeTransport(); } finally { await engine.close(); }
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
    // The calls this failure rejected are being answered right now: they get their answer, within two seconds.
    await Promise.race([closeTransport(false), new Promise(resolve => setTimeout(resolve, 2000))]).catch(report);
  }
  if (readyFile) forgetReady(readyFile);
  process.exit(1);
});
if (stdio) {
  process.stdin.once("end", () => { void close().catch(report); });
  process.stdin.resume();
}
function report(error: unknown) { console.error(error instanceof Error ? error.message : "MCP transport failure."); }
