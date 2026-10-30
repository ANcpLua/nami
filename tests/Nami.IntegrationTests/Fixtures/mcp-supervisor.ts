import assert from "node:assert/strict";
import { existsSync } from "node:fs";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";
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
async function launcher(mode: string) {
  const path = join(state, "engine");
  const fixture = fileURLToPath(new URL("./mcp-engine.ts", import.meta.url));
  await writeFile(path, `#!/bin/sh\nexec ${quote(process.execPath)} ${quote(fixture)} ${quote(mode)} ${quote(state)} "$@"\n`, { mode: 0o700 });
  return path;
}
async function request(handler: ReturnType<typeof createMcpHandler>, input: unknown) {
  const response = await handler.fetch(new Request("http://localhost/mcp", {
    method: "POST", headers: { "Content-Type": "application/json", Accept: "application/json, text/event-stream", "MCP-Protocol-Version": "2025-11-25" },
    body: JSON.stringify({ jsonrpc: "2.0", id: 1, method: "tools/call", params: { name: "sources_status", arguments: input } })
  }));
  assert.equal(response.status, 200);
  const body = await response.text();
  const data = body.split("\n").find(line => line.startsWith("data: "));
  return JSON.parse(data ? data.slice(6) : body).result;
}

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
      const refusal = await request(handler, { service: "x".repeat(1_048_576) });
      assert.equal(refusal.isError, true);
      assert.equal(refusal.structuredContent.code, "invalid_argument");
      assert.equal(refusal.structuredContent.retryable, false);
      assert.equal(refusal.content.length, 2);
      assert.deepEqual(JSON.parse(refusal.content[1].text), refusal.structuredContent);
      await engine.request("call", "release", {});
      assert.equal((await held).isError, false);
      const later = await request(handler, {});
      assert.equal(later.isError, false);
      assert.equal(later.structuredContent.snapshot_id, "fixture-snapshot");
      const health = await engine.request("health", null, null);
      assert.equal((health.result as { pid: number }).pid, before);
      console.log(JSON.stringify({ refusalCode: refusal.structuredContent.code, pendingSucceeded: true, subsequentSucceeded: true, sameChild: true }));
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
      // Release the blocked pipe even when the assertion fails, so cleanup cannot mask it.
      await writeFile(join(state, "resume"), "resume");
      assert.equal(settled, "rejected", "Cancellation must settle without waiting for pipe drain.");
      await until(() => existsSync(join(state, "cancel")));
      const health = await engine.request("health", null, null);
      assert.equal((health.result as { pid: number }).pid, await pid());
      await sleep(25);
      assert.deepEqual(unhandled, []);
      console.log(JSON.stringify({ settled, unhandledRejections: unhandled.length, cancelForwarded: true, subsequentSucceeded: true }));
    } finally { process.removeListener("unhandledRejection", record); }
  } else if (scenario === "failure") {
    engine = new Engine(await launcher("stubborn"));
    await engine.ready;
    const child = await pid();
    await assert.rejects(engine.request("call", "malformed", {}));
    const first = engine.close();
    const second = engine.close();
    assert.equal(first, second, "Concurrent shutdown calls must share completion.");
    await Promise.race([first, sleep(10_000).then(() => { throw new Error("Forced shutdown timed out."); })]);
    assert.equal(existsSync(join(state, "term")), true);
    assert.equal(running(child), false, "Shutdown must await termination of a SIGTERM-resistant child.");
    console.log(JSON.stringify({ termReceived: true, childExited: true, sameShutdown: true }));
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
      assert.equal(await Promise.race([processExit.then(() => "exited"), sleep(150).then(() => "alive")]), "alive");
      const health = await fetch(url + "/health");
      assert.equal(health.status, 200);
      const call = await fetch(url + "/mcp", {
        method: "POST", headers: { "Content-Type": "application/json", Accept: "application/json, text/event-stream", "MCP-Protocol-Version": "2025-11-25" },
        body: JSON.stringify({ jsonrpc: "2.0", id: 1, method: "tools/call", params: { name: "sources_status", arguments: {} } })
      });
      assert.equal(call.status, 200);
      const body = await call.text();
      assert.match(body, /fixture-snapshot/);
      processUnderTest.kill("SIGTERM");
      assert.equal(await processExit, 0);
      assert.equal(running(child), false);
      console.log(JSON.stringify({ healthStatus: health.status, toolStatus: call.status, aliveAfterEof: true, childExitedAfterSignal: true }));
    } else {
      processUnderTest.stdin.end();
      assert.equal(await processExit, 0);
      assert.equal(running(child), false);
      console.log(JSON.stringify({ exitedOnEof: true, childExited: true }));
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
