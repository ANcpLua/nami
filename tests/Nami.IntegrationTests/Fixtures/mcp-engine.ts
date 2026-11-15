import { appendFileSync, existsSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { createInterface } from "node:readline";
import { CONTRACT_REVISION } from "../../../src/nami.mcp/src/contracts/generated/manifest.ts";

const [mode, state] = process.argv.slice(2) as [string, string];
const reply = (id: string, result: unknown, isError = false) => console.log(JSON.stringify({
  id, revision: CONTRACT_REVISION, is_error: isError, result
}));
const result = {
  freshness: { watermark: "2026-10-30T00:00:00Z", complete: true, examined: 0, matched: 0, returned: 0, truncated: false },
  snapshot_id: "fixture-snapshot", services: []
};
let held: string | undefined;
// Mode "holding" keeps every tool call waiting until the file "release" appears; it records the
// ids it received in "calls" and the cancelled ids in "cancels", one per line.
const holding: string[] = [];
let released = false;
// Modes "stubborn" and "gone" ignore SIGTERM and end of input; only SIGKILL ends them.
const resistant = mode === "stubborn" || mode === "gone";
if (resistant) process.on("SIGTERM", () => writeFileSync(join(state, "term"), "received"));

function readCommands() {
  const input = createInterface({ input: process.stdin });
  input.on("line", line => {
    const command = JSON.parse(line);
    if (command.method === "cancel") {
      writeFileSync(join(state, "cancel"), command.id);
      appendFileSync(join(state, "cancels"), command.id + "\n");
    } else if (command.method === "health") {
      if (mode === "unhealthy")
        reply(command.id, { status: "unhealthy", total_duration_ms: 0, contract_revision: CONTRACT_REVISION, entries: {} }, true);
      else reply(command.id, { pid: process.pid });
    } else if (mode === "holding") {
      appendFileSync(join(state, "calls"), command.id + "\n");
      if (released) reply(command.id, result);
      else holding.push(command.id);
    } else if (command.name === "hold") {
      held = command.id;
      writeFileSync(join(state, "held"), "received");
    } else if (command.name === "release") {
      if (held) reply(held, result);
      held = undefined;
      reply(command.id, result);
    } else if (command.name === "malformed") {
      console.log("not-json");
    } else {
      reply(command.id, result);
    }
  });
  input.on("close", () => { if (!resistant) process.exit(0); });
}

if (mode === "paused") {
  const waiting = setInterval(() => {
    if (!existsSync(join(state, "resume"))) return;
    clearInterval(waiting);
    readCommands();
  }, 10);
} else readCommands();
if (resistant) setInterval(() => {}, 1000);
if (mode === "gone") {
  // One line that is not JSON once the file "break" appears: the host must treat its engine as gone.
  const breaking = setInterval(() => {
    if (!existsSync(join(state, "break"))) return;
    clearInterval(breaking);
    console.log("not-json");
  }, 10);
}
if (mode === "holding") {
  const releasing = setInterval(() => {
    if (!existsSync(join(state, "release"))) return;
    clearInterval(releasing);
    released = true;
    for (const id of holding.splice(0)) reply(id, result);
  }, 10);
}
writeFileSync(join(state, "engine.json"), JSON.stringify({ pid: process.pid }));
reply("ready", { pid: process.pid });
