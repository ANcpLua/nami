import { existsSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { createInterface } from "node:readline";
import { CONTRACT_REVISION } from "../../../src/nami.mcp/src/contracts/generated/manifest.ts";

const [mode, state] = process.argv.slice(2) as [string, string];
const reply = (id: string, result: unknown) => console.log(JSON.stringify({
  id, revision: CONTRACT_REVISION, is_error: false, result
}));
const result = {
  freshness: { watermark: "2026-10-30T00:00:00Z", complete: true, examined: 0, matched: 0, returned: 0, truncated: false },
  snapshot_id: "fixture-snapshot", services: []
};
let held: string | undefined;
if (mode === "stubborn") process.on("SIGTERM", () => writeFileSync(join(state, "term"), "received"));

function readCommands() {
  const input = createInterface({ input: process.stdin });
  input.on("line", line => {
    const command = JSON.parse(line);
    if (command.method === "cancel") {
      writeFileSync(join(state, "cancel"), command.id);
    } else if (command.method === "health") {
      reply(command.id, { pid: process.pid });
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
  input.on("close", () => { if (mode !== "stubborn") process.exit(0); });
}

if (mode === "paused") {
  const waiting = setInterval(() => {
    if (!existsSync(join(state, "resume"))) return;
    clearInterval(waiting);
    readCommands();
  }, 10);
} else readCommands();
if (mode === "stubborn") setInterval(() => {}, 1000);
writeFileSync(join(state, "engine.json"), JSON.stringify({ pid: process.pid }));
reply("ready", { pid: process.pid });
