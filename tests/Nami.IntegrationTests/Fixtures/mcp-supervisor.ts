import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { connect } from "node:net";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { Engine } from "../../../src/nami.mcp/src/engine.ts";
import { createServer } from "../../../src/nami.mcp/src/server.ts";

const require = createRequire(new URL("../../../src/nami.mcp/package.json", import.meta.url));
const { createMcpHandler } = await import(require.resolve("@modelcontextprotocol/server"));
const [scenario, scratch] = process.argv.slice(2) as [string, string];
await mkdir(scratch, { recursive: true });
const state = await mkdtemp(join(scratch, "mcp-"));
const sleep = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));
const quote = (text: string) => "'" + text.replaceAll("'", "'\\''") + "'";
const running = (pid: number) => { try { process.kill(pid, 0); return true; } catch { return false; } };
const pid = async () => JSON.parse(await readFile(join(state, "engine.json"), "utf8")).pid as number;
async function until(check: () => boolean | Promise<boolean>) {
  for (let attempt = 0; attempt < 200; attempt++) {
    if (await check()) return;
    await sleep(25);
  }
  throw new Error("Fixture condition did not complete within five seconds.");
}
// Every field a scenario prints is an observation. The MSTest method judges it; assertions here
// only guard steps that a later observation depends on.
const observed = (check: () => boolean | Promise<boolean>) => until(check).then(() => true, () => false);
async function launcher(mode: string) {
  const path = join(state, "engine");
  const fixture = fileURLToPath(new URL("./mcp-engine.ts", import.meta.url));
  await writeFile(path, `#!/bin/sh\nexec ${quote(process.execPath)} ${quote(fixture)} ${quote(mode)} ${quote(state)} "$@"\n`, { mode: 0o700 });
  return path;
}
async function request(handler: ReturnType<typeof createMcpHandler>, input: unknown, meta?: Record<string, unknown>) {
  const response = await handler.fetch(new Request("http://localhost/mcp", {
    method: "POST", headers: { "Content-Type": "application/json", Accept: "application/json, text/event-stream", "MCP-Protocol-Version": "2025-11-25" },
    body: JSON.stringify({ jsonrpc: "2.0", id: 1, method: "tools/call", params: { name: "sources_status", arguments: input, ...(meta ? { _meta: meta } : {}) } })
  }));
  assert.equal(response.status, 200);
  const body = await response.text();
  const data = body.split("\n").find(line => line.startsWith("data: "));
  return JSON.parse(data ? data.slice(6) : body).result;
}

const lines = async (name: string) => existsSync(join(state, name))
  ? (await readFile(join(state, name), "utf8")).split("\n").filter(Boolean) : [];
const toolCall = (url: string, signal?: AbortSignal) => fetch(url + "/mcp", {
  method: "POST", signal, headers: { "Content-Type": "application/json", Accept: "application/json, text/event-stream", "MCP-Protocol-Version": "2025-11-25" },
  body: JSON.stringify({ jsonrpc: "2.0", id: 1, method: "tools/call", params: { name: "sources_status", arguments: {} } })
});
async function toolResult(response: Response) {
  const body = await response.text();
  const data = body.split("\n").find(line => line.startsWith("data: "));
  return JSON.parse(data ? data.slice(6) : body).result as { isError?: boolean; structuredContent?: { code?: string } };
}
async function healthOf(url: string) {
  const response = await fetch(url + "/health");
  const text = await response.text();
  let report: { status?: unknown } | null = null;
  try { report = JSON.parse(text); } catch { report = null; }
  return { status: response.status, reportStatus: typeof report?.status === "string" ? report.status : null };
}
// Starts main.ts over HTTP with the fixture engine in the given mode; resolves once the engine is up.
async function startHost(mode: string) {
  const reservation = Bun.serve({ hostname: "127.0.0.1", port: 0, fetch: () => new Response() });
  const port = reservation.port;
  await reservation.stop(true);
  const args = [process.execPath, fileURLToPath(new URL("../../../src/nami.mcp/src/main.ts", import.meta.url)), "--engine", await launcher(mode)];
  processUnderTest = Bun.spawn(args, { stdin: "pipe", stdout: "pipe", stderr: "pipe", env: {
    ...process.env, DOTNET_ENVIRONMENT: "Development", NAMI_OTLP_AUTH_MODE: "Unsecured", NAMI_OTLP_PROJECT_KEYS: "{}",
    NAMI_BIND_ADDRESS: "127.0.0.1", NAMI_PORT: String(port), NAMI_PUBLIC_URL: "", RAILWAY_PUBLIC_DOMAIN: ""
  } });
  const url = `http://127.0.0.1:${port}`;
  await until(() => existsSync(join(state, "engine.json")));
  await until(async () => {
    try { await fetch(url + "/health", { signal: AbortSignal.timeout(200) }); return true; }
    catch { return false; }
  });
  return { url, exited: processUnderTest.exited, child: await pid() };
}
// The exit code once the host ends within the deadline, otherwise "alive".
// The deadline's timer does not keep this script running once the host has ended.
const exitWithin = (exited: Promise<number>, ms: number) =>
  Promise.race([exited, new Promise<"alive">(resolve => { setTimeout(() => resolve("alive"), ms).unref(); })]);
const gone = (child: number) => {
  try { return readFileSync(`/proc/${child}/stat`, "utf8").split(") ")[1]?.startsWith("Z") ?? true; }
  catch { return true; }
};

let engine: Engine | undefined;
let processUnderTest: ReturnType<typeof Bun.spawn> | undefined;
try {
  if (scenario === "oversize") {
    engine = new Engine(await launcher("normal"));
    await engine.ready;
    const before = await pid();
    const held = engine.request("call", "hold", {});
    await until(() => existsSync(join(state, "held")));
    const handler = createMcpHandler(() => createServer(engine!));
    try {
      // The schema puts no length on a cursor, so this input is valid and only its frame is too large.
      const refusal = await request(handler, { cursor: "x".repeat(1_048_576) });
      assert.equal(refusal.isError, true);
      assert.equal(refusal.structuredContent.retryable, false);
      assert.equal(refusal.content.length, 2);
      assert.deepEqual(JSON.parse(refusal.content[1].text), refusal.structuredContent);
      await engine.request("call", "release", {});
      const pending = await held;
      const later = await request(handler, {});
      const health = await engine.request("health", null, null);
      console.log(JSON.stringify({
        refusalCode: refusal.structuredContent.code, pendingIsError: pending.isError, subsequentIsError: later.isError,
        subsequentSnapshot: later.structuredContent?.snapshot_id ?? null,
        childBefore: before, childAfter: (health.result as { pid?: number } | null)?.pid ?? null
      }));
    } finally { await handler.close(); }
  } else if (scenario === "trace" || scenario === "refusal" || scenario === "headline") {
    engine = new Engine(await launcher("recording"));
    await engine.ready;
    const handler = createMcpHandler(() => createServer(engine!));
    const lone = (text: unknown) => typeof text === "string"
      && /[\ud800-\udbff](?![\udc00-\udfff])|(?<![\ud800-\udbff])[\udc00-\udfff]/.test(text);
    // What the engine received for one request: its frame, or null when the host answered by itself.
    const exchange = async (input: unknown, meta?: Record<string, unknown>) => {
      const before = (await lines("frames")).length;
      const result = await request(handler, input, meta);
      const frames = await lines("frames");
      return { result, frame: frames.length === before + 1 ? JSON.parse(frames.at(-1)!) as Record<string, unknown> : null };
    };
    try {
      if (scenario === "trace") {
        const trace = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
        const cases: Record<string, Record<string, unknown>> = {
          valid: { traceparent: trace, tracestate: "vendor=value, tenant@system=1" },
          loneSurrogate: { traceparent: "\ud800" },
          upperCase: { traceparent: trace.toUpperCase() },
          zeroTraceId: { traceparent: "00-00000000000000000000000000000000-b7ad6b7169203331-01" },
          zeroParentId: { traceparent: "00-0af7651916cd43dd8448eb211c80319c-0000000000000000-01" },
          versionFf: { traceparent: "ff" + trace.slice(2) },
          trailingOnVersion00: { traceparent: trace + "-extra" },
          laterVersion: { traceparent: "01" + trace.slice(2) + "-extra" },
          notText: { traceparent: 7, tracestate: ["vendor=value"] },
          stateWithSurrogate: { traceparent: trace, tracestate: "vendor=\ud800" },
          stateWithoutValue: { traceparent: trace, tracestate: "vendor" },
          stateWithRepeatedKey: { traceparent: trace, tracestate: "vendor=1,vendor=2" },
          stateWithoutParent: { tracestate: "vendor=value" }
        };
        const seen: Record<string, unknown> = {};
        for (const [name, meta] of Object.entries(cases)) {
          const { result, frame } = await exchange({}, meta);
          seen[name] = { isError: result.isError ?? null, forwarded: frame !== null,
            traceparent: frame?.traceparent ?? null, tracestate: frame?.tracestate ?? null };
        }
        console.log(JSON.stringify(seen));
      } else if (scenario === "refusal") {
        let deep: unknown = 0;
        for (let level = 0; level < 70; level++) deep = [deep];
        const cases: Record<string, unknown> = {
          unknownNestedDeep: { nested: deep },
          belowMinimum: { max_bytes: 5 },
          wrongItemType: { fields: ["service", 7] },
          tooLong: { service: "x".repeat(300) },
          nameWithSurrogate: { ["\udc00"]: 1 }
        };
        const seen: Record<string, unknown> = {};
        for (const [name, input] of Object.entries(cases)) {
          const { result, frame } = await exchange(input);
          const error = result.structuredContent ?? {};
          seen[name] = { isError: result.isError ?? null, forwarded: frame !== null, code: error.code ?? null,
            retryable: error.retryable ?? null, field: error.field ?? null, message: error.message ?? null,
            allowed: error.allowed ?? null, hint: error.hint ?? null, parts: result.content?.length ?? 0,
            textIsStructured: result.content?.length === 2 && JSON.stringify(JSON.parse(result.content[1].text)) === JSON.stringify(error),
            loneSurrogate: lone(error.field) || lone(error.message) || lone(result.content?.[0]?.text) };
        }
        const later = await exchange({});
        console.log(JSON.stringify({ ...seen, laterIsError: later.result.isError ?? null, laterForwarded: later.frame !== null,
          framesInAll: (await lines("frames")).length }));
      } else {
        const { result } = await exchange({ service: "emoji-svc" });
        const text: unknown = result.content?.[0]?.text;
        console.log(JSON.stringify({ isError: result.isError ?? null, loneSurrogate: lone(text),
          keepsTheCharacter: typeof text === "string" && text.includes("\u{1F600}"),
          cutAfterIt: typeof text === "string" && !text.includes("Error") }));
      }
    } finally { await handler.close(); }
  } else if (scenario === "backpressure") {
    engine = new Engine(await launcher("paused"));
    await engine.ready;
    const unhandled: unknown[] = [];
    const record = (reason: unknown) => unhandled.push(reason);
    process.on("unhandledRejection", record);
    try {
      const cancellation = new AbortController();
      const pending = engine.request("call", "sources_status", { cursor: "x".repeat(900_000) }, null, cancellation.signal);
      const completed = pending.then(() => "resolved", () => "rejected");
      await sleep(100);
      cancellation.abort();
      const settled = await Promise.race([completed, sleep(500).then(() => "still-pending")]);
      // Release the blocked pipe whatever was observed, so cleanup cannot mask it.
      await writeFile(join(state, "resume"), "resume");
      const cancelForwarded = await observed(() => existsSync(join(state, "cancel")));
      const health = await engine.request("health", null, null);
      const child = await pid();
      await sleep(25);
      console.log(JSON.stringify({
        settled, unhandledRejections: unhandled.length, cancelForwarded, subsequentIsError: health.isError,
        child, subsequentChild: (health.result as { pid?: number } | null)?.pid ?? null
      }));
    } finally { process.removeListener("unhandledRejection", record); }
  } else if (scenario === "failure") {
    engine = new Engine(await launcher("stubborn"));
    await engine.ready;
    const child = await pid();
    await assert.rejects(engine.request("call", "malformed", {}));
    const first = engine.close();
    const second = engine.close();
    await Promise.race([first, sleep(10_000).then(() => { throw new Error("Forced shutdown timed out."); })]);
    console.log(JSON.stringify({
      termReceived: existsSync(join(state, "term")), childRunningAfterClose: running(child), sameShutdown: first === second
    }));
  } else if (scenario === "gone") {
    // P04: the engine fails (one line that is not JSON) and ignores SIGTERM, so the host waits up
    // to 8 seconds for it (engine.ts:112-113). In that window /health must answer 503.
    const host = await startHost("gone");
    const before = await healthOf(host.url);
    await writeFile(join(state, "break"), "break");
    const failed = await observed(() => existsSync(join(state, "term")));
    const during = await healthOf(host.url);
    const exit = await exitWithin(host.exited, 15_000);
    console.log(JSON.stringify({
      healthBefore: before.status, engineFailed: failed, healthStatus: during.status, healthReportStatus: during.reportStatus,
      hostExit: exit, childGone: gone(host.child)
    }));
  } else if (scenario === "killed") {
    // P04: the engine dies while a call waits on it. The call must still get its typed answer before the host ends.
    const host = await startHost("holding");
    const call = toolCall(host.url).then(toolResult).catch((error: Error) => ({ transport: error.name }));
    const waiting = await observed(async () => (await lines("calls")).length === 1);
    process.kill(host.child, "SIGKILL");
    const outcome = await call as { isError?: boolean; structuredContent?: { code?: string; retryable?: boolean }; transport?: string };
    const exit = await exitWithin(host.exited, 10_000);
    console.log(JSON.stringify({ waiting, transportError: outcome.transport ?? null, isError: outcome.isError ?? null,
      code: outcome.structuredContent?.code ?? null, retryable: outcome.structuredContent?.retryable ?? null,
      hostExit: exit, childGone: gone(host.child) }));
  } else if (scenario === "twice") {
    // The engine ignores SIGTERM and the end of its input, so the host's close waits for it. A second
    // SIGTERM during that wait must still end the host by itself, which takes the engine with it.
    const host = await startHost("stubborn");
    processUnderTest!.kill("SIGTERM");
    const closingBegan = await observed(async () => {
      try { await fetch(host.url + "/health", { signal: AbortSignal.timeout(200) }); return false; }
      catch { return true; }
    });
    processUnderTest!.kill("SIGTERM");
    const exit = await exitWithin(host.exited, 3_000);
    const childGone = await observed(() => gone(host.child));
    console.log(JSON.stringify({ closingBegan, hostExit: exit, hostSignal: processUnderTest!.signalCode ?? null, childGone }));
  } else if (scenario === "badhost") {
    // H6: a Host header that is no host. fetch() cannot send one, so the request is written to the socket.
    const host = await startHost("normal");
    const port = Number(new URL(host.url).port);
    const raw = (header: string, path: string) => new Promise<Record<string, unknown>>(resolve => {
      const socket = connect(port, "127.0.0.1");
      let data = "";
      const done = () => resolve({
        status: Number(data.split(" ")[1] ?? 0), contentType: /^content-type: *([^;\r\n]*)/im.exec(data)?.[1]?.toLowerCase() ?? null,
        bytes: data.length, disclosesSource: /main\.ts|\/src\/|<html|stack/i.test(data),
        rpcCode: /"code":(-?[0-9]+)/.exec(data)?.[1] ?? null });
      socket.setEncoding("latin1");
      socket.setTimeout(3_000, () => socket.destroy());
      socket.on("connect", () => socket.write(`GET ${path} HTTP/1.1\r\nHost: ${header}\r\nConnection: close\r\n\r\n`));
      socket.on("data", chunk => { data += chunk; });
      socket.on("close", done);
      socket.on("error", () => {});
    });
    const seen: Record<string, unknown> = {};
    for (const [name, header] of Object.entries({ space: "a b", openBracket: "[::1", portOutOfRange: "localhost:99999", foreign: "evil.example" }))
      seen[name] = await raw(header, "/mcp");
    seen.healthAfter = (await healthOf(host.url)).status;
    processUnderTest!.kill("SIGTERM");
    console.log(JSON.stringify({ ...seen, exitCodeAfterSignal: await host.exited }));
  } else if (scenario === "stdio-apikey") {
    // H11: stdio carries no credential, so in ApiKey mode no call could ever be authorized.
    const args = [process.execPath, fileURLToPath(new URL("../../../src/nami.mcp/src/main.ts", import.meta.url)), "--engine", await launcher("normal"), "--stdio"];
    const refusing = Bun.spawn(args, { stdin: "pipe", stdout: "pipe", stderr: "pipe", env: {
      ...process.env, DOTNET_ENVIRONMENT: "Production", ASPNETCORE_ENVIRONMENT: "Production", NAMI_OTLP_AUTH_MODE: "ApiKey", NAMI_OTLP_PROJECT_KEYS: "{}"
    } });
    processUnderTest = refusing;
    const exit = await exitWithin(refusing.exited, 3_000);
    const engineStarted = existsSync(join(state, "engine.json"));
    let reason = "";
    if (exit !== "alive") {
      reason = await new Response(refusing.stderr).text();
      processUnderTest = undefined;
    }
    console.log(JSON.stringify({ hostExit: exit, engineStarted,
      namesTheTransport: reason.includes("--stdio"), namesTheMode: reason.includes("NAMI_OTLP_AUTH_MODE=ApiKey"),
      namesTheWayOut: reason.includes("Streamable HTTP") && reason.includes("NAMI_OTLP_AUTH_MODE=Unsecured") }));
  } else if (scenario === "slow") {
    // H5: a 2025-11-25 call is answered as an event stream; the engine takes 15 seconds to answer it.
    // Beside it a 2026-07-28 call, which is answered as one JSON body: no byte travels until the result.
    const host = await startHost("slow");
    const started = performance.now();
    type Answer = { status: number; type: string | null; body: string };
    const answered = (call: Promise<Response>): Promise<Answer> => call.then(
      async response => ({ status: response.status, type: response.headers.get("content-type"), body: await response.text() }))
      .catch((error: Error) => ({ status: 0, type: null, body: error.name }));
    const modern = answered(fetch(host.url + "/mcp", {
      method: "POST", headers: { "Content-Type": "application/json", Accept: "application/json, text/event-stream",
        "MCP-Protocol-Version": "2026-07-28", "Mcp-Method": "tools/call", "Mcp-Name": "sources_status" },
      body: JSON.stringify({ jsonrpc: "2.0", id: 2, method: "tools/call", params: { name: "sources_status", arguments: {}, _meta: {
        "io.modelcontextprotocol/protocolVersion": "2026-07-28", "io.modelcontextprotocol/clientCapabilities": {},
        "io.modelcontextprotocol/clientInfo": { name: "fixture", version: "0" } } } })
    }));
    const stream = await answered(toolCall(host.url));
    const resultOf = (text: string | undefined) => {
      try { return JSON.parse(text ?? "").result as { isError?: boolean; structuredContent?: { snapshot_id?: string } }; }
      catch { return null; }
    };
    const seconds = Math.round((performance.now() - started) / 1000);
    const result = resultOf(stream.body.split("\n").find(line => line.startsWith("data: "))?.slice(6));
    const plain = await modern;
    processUnderTest!.kill("SIGTERM");
    console.log(JSON.stringify({
      status: stream.status, contentType: stream.type, seconds,
      keepAlives: stream.body.split("\n").filter(line => line.startsWith(":")).length,
      isError: result?.isError ?? null, snapshot: result?.structuredContent?.snapshot_id ?? null,
      jsonStatus: plain.status, jsonContentType: plain.type, jsonSnapshot: resultOf(plain.body)?.structuredContent?.snapshot_id ?? null,
      exitCodeAfterSignal: await host.exited
    }));
  } else if (scenario === "unhealthy") {
    const host = await startHost("unhealthy");
    const health = await healthOf(host.url);
    processUnderTest!.kill("SIGTERM");
    console.log(JSON.stringify({ healthStatus: health.status, healthReportStatus: health.reportStatus, exitCodeAfterSignal: await host.exited }));
  } else if (scenario === "busy" || scenario === "cancel") {
    // 32 tool calls wait in the engine: the host's own limit (ENGINE_LIMITS.max_pending).
    const host = await startHost("holding");
    const aborts = Array.from({ length: 32 }, () => new AbortController());
    const calls = aborts.map(controller => toolCall(host.url, controller.signal).then(toolResult, (error: Error) => error.name));
    const allWaiting = await observed(async () => (await lines("calls")).length === 32);
    if (scenario === "busy") {
      const health = await healthOf(host.url);
      await writeFile(join(state, "release"), "release");
      const outcomes = await Promise.all(calls);
      processUnderTest!.kill("SIGTERM");
      console.log(JSON.stringify({
        allWaiting, healthStatus: health.status, healthReportStatus: health.reportStatus,
        callsSucceeded: outcomes.filter(outcome => typeof outcome === "object" && outcome.isError === false).length,
        exitCodeAfterSignal: await host.exited
      }));
    } else {
      // P14: the caller of the last call goes away. The host must end that waiting call: free its
      // slot and forward a cancel, so the next call reaches the engine instead of the host's limit.
      aborts[31]!.abort();
      const cancelForwarded = await observed(async () => (await lines("cancels")).length === 1);
      const next = toolCall(host.url).then(toolResult);
      const nextForwarded = await observed(async () => (await lines("calls")).length === 33);
      await writeFile(join(state, "release"), "release");
      const outcomes = await Promise.all(calls);
      const nextOutcome = await next;
      processUnderTest!.kill("SIGTERM");
      console.log(JSON.stringify({
        allWaiting, abortedCaller: outcomes[31], cancelForwarded,
        cancelledId: (await lines("cancels"))[0] ?? null, nextForwarded,
        nextIsError: nextOutcome.isError ?? null, nextCode: nextOutcome.structuredContent?.code ?? null,
        othersSucceeded: outcomes.slice(0, 31).filter(outcome => typeof outcome === "object" && outcome.isError === false).length,
        exitCodeAfterSignal: await host.exited
      }));
    }
  } else if (scenario === "http" || scenario === "stdio") {
    const args = [process.execPath, fileURLToPath(new URL("../../../src/nami.mcp/src/main.ts", import.meta.url)), "--engine", await launcher("normal")];
    let port: number | undefined;
    if (scenario === "http") {
      const reservation = Bun.serve({ hostname: "127.0.0.1", port: 0, fetch: () => new Response() });
      port = reservation.port;
      await reservation.stop(true);
    } else args.push("--stdio");
    processUnderTest = Bun.spawn(args, { stdin: "pipe", stdout: "pipe", stderr: "pipe", env: {
      ...process.env, DOTNET_ENVIRONMENT: "Development", NAMI_OTLP_AUTH_MODE: "Unsecured", NAMI_OTLP_PROJECT_KEYS: "{}",
      NAMI_BIND_ADDRESS: "127.0.0.1", NAMI_PORT: String(port ?? 5100), NAMI_PUBLIC_URL: "", RAILWAY_PUBLIC_DOMAIN: ""
    } });
    const processExit = processUnderTest.exited;
    await until(() => existsSync(join(state, "engine.json")));
    const child = await pid();
    if (scenario === "http") {
      const url = `http://127.0.0.1:${port}`;
      await until(async () => {
        try { return (await fetch(url + "/health", { signal: AbortSignal.timeout(200) })).ok; }
        catch { return false; }
      });
      processUnderTest.stdin.end();
      const afterEof = await Promise.race([processExit.then(() => "exited"), sleep(150).then(() => "alive")]);
      const health = await fetch(url + "/health");
      const call = await fetch(url + "/mcp", {
        method: "POST", headers: { "Content-Type": "application/json", Accept: "application/json, text/event-stream", "MCP-Protocol-Version": "2025-11-25" },
        body: JSON.stringify({ jsonrpc: "2.0", id: 1, method: "tools/call", params: { name: "sources_status", arguments: {} } })
      });
      const body = await call.text();
      processUnderTest.kill("SIGTERM");
      const exitCode = await processExit;
      console.log(JSON.stringify({
        afterEof, healthStatus: health.status, toolStatus: call.status, toolReturnedSnapshot: body.includes("fixture-snapshot"),
        exitCodeAfterSignal: exitCode, childRunningAfterSignal: running(child)
      }));
    } else {
      processUnderTest.stdin.end();
      const exitCode = await processExit;
      console.log(JSON.stringify({ exitCodeOnEof: exitCode, childRunningAfterExit: running(child) }));
    }
  } else throw new Error("Unknown supervisor scenario: " + scenario);
} finally {
  if (processUnderTest) {
    if (processUnderTest.exitCode === null) processUnderTest.kill("SIGKILL");
    await processUnderTest.exited;
    const stderr = await new Response(processUnderTest.stderr).text();
    if (stderr) console.error(stderr);
  }
  await engine?.close();
  // A failed transport assertion must still reap the external fixture child.
  if (existsSync(join(state, "engine.json"))) {
    const child = await pid();
    if (running(child)) process.kill(child, "SIGKILL");
  }
  await rm(state, { recursive: true, force: true });
}
