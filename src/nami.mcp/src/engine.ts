import { spawn } from "node:child_process";
import { once } from "node:events";
import type { EngineRequest, EngineResponse } from "./contracts/generated/api.js";
import type { EngineResponse as EngineResponseJson } from "./contracts/generated/json.js";
import { EngineRequestJson, EngineResponseJson as responseCodec } from "./contracts/generated/serialization.js";
import { CONTRACT_REVISION, ENGINE_LIMITS } from "./contracts/generated/manifest.js";

/** One supervised native engine, with bounded complete JSONL frames and request correlation. */
export class EngineError extends Error {
  constructor(readonly code: "collector_unavailable" | "collector_contract_mismatch", message: string) { super(message); }
}
export class Engine {
  private readonly child;
  private readonly pending = new Map<string, { resolve: (reply: EngineResponse) => void; reject: (error: Error) => void; cleanup: () => void }>();
  private sequence = 0;
  private stopped = false;
  readonly ready: Promise<EngineResponse>;

  constructor(path: string) {
    this.child = spawn(path.endsWith(".dll") ? "dotnet" : path,
      path.endsWith(".dll") ? [path, "--engine"] : ["--engine"], { stdio: ["pipe", "pipe", "inherit"] });
    const stopChild = () => { this.child.kill("SIGKILL"); };
    process.once("exit", stopChild);
    this.child.once("exit", () => process.removeListener("exit", stopChild));
    this.ready = new Promise((resolve, reject) => this.pending.set("ready", { resolve, reject, cleanup() {} }));
    const startup = setTimeout(() => this.fail(new Error("Native engine startup timed out.")), 30000);
    startup.unref();
    void this.ready.then(() => clearTimeout(startup), () => clearTimeout(startup));
    this.child.stdin.on("error", (error) => this.fail(error));
    let buffer = Buffer.alloc(0);
    this.child.stdout.on("data", (data: Buffer) => {
      try {
        buffer = Buffer.concat([buffer, data]);
        let newline: number;
        while ((newline = buffer.indexOf(10)) >= 0) {
          if (newline > ENGINE_LIMITS.max_frame_bytes) throw new Error("Engine response exceeds its bound.");
          const frame: unknown = JSON.parse(buffer.subarray(0, newline).toString("utf8"));
          buffer = buffer.subarray(newline + 1);
          if (!isReply(frame) || frame.revision !== CONTRACT_REVISION) throw new EngineError("collector_contract_mismatch", "Engine contract mismatch.");
          const request = this.pending.get(frame.id);
          if (request) { this.pending.delete(frame.id); request.cleanup(); request.resolve(responseCodec.fromJson(frame)); }
        }
        if (buffer.length > ENGINE_LIMITS.max_frame_bytes) throw new Error("Engine response exceeds its bound.");
      } catch (error) { this.fail(error instanceof Error ? error : new Error("Invalid engine response.")); }
    });
    this.child.on("error", (error) => this.fail(error));
    this.child.on("exit", (code, signal) => this.fail(new Error(`Engine exited (${signal ?? code}).`)));
  }

  async request(method: "call" | "health", name: string | null, input: unknown,
    credential: string | null = null, signal?: AbortSignal, traceparent: string | null = null,
    tracestate: string | null = null): Promise<EngineResponse> {
    await this.ready;
    if (this.stopped) throw new EngineError("collector_unavailable", "Engine has stopped.");
    if (this.pending.size >= ENGINE_LIMITS.max_pending) throw new Error("Engine request queue is full.");
    signal?.throwIfAborted();
    const id = String(++this.sequence);
    const request: EngineRequest = { id, revision: CONTRACT_REVISION, method, name, input, credential, traceparent, tracestate };
    const result = new Promise<EngineResponse>((resolve, reject) => {
      const abort = () => {
        this.pending.delete(id);
        signal?.removeEventListener("abort", abort);
        reject(new Error("Engine request cancelled."));
        void this.write({ ...request, method: "cancel", input: null }).catch((error: unknown) => this.fail(asError(error)));
      };
      this.pending.set(id, { resolve, reject, cleanup: () => signal?.removeEventListener("abort", abort) });
      signal?.addEventListener("abort", abort, { once: true });
    });
    try { await this.write(request); }
    catch (error) { this.fail(asError(error)); }
    return result;
  }

  async close(): Promise<void> {
    if (this.stopped) return;
    const exit = once(this.child, "exit");
    this.child.stdin.end();
    const graceful = setTimeout(() => this.child.kill("SIGTERM"), 5000);
    const forced = setTimeout(() => this.child.kill("SIGKILL"), 8000);
    graceful.unref(); forced.unref();
    try { await exit; } finally { clearTimeout(graceful); clearTimeout(forced); }
  }

  private async write(request: EngineRequest): Promise<void> {
    const frame = JSON.stringify(EngineRequestJson.toJson(request)) + "\n";
    if (Buffer.byteLength(frame) > ENGINE_LIMITS.max_frame_bytes) throw new Error("Engine request exceeds its bound.");
    if (!this.child.stdin.write(frame)) await once(this.child.stdin, "drain");
  }

  private fail(error: Error): void {
    if (this.stopped) return;
    this.stopped = true;
    for (const request of this.pending.values()) { request.cleanup(); request.reject(error); }
    this.pending.clear();
    this.child.kill("SIGTERM");
  }
}

function isReply(value: unknown): value is EngineResponseJson {
  return typeof value === "object" && value !== null && "id" in value && typeof value.id === "string"
    && "revision" in value && typeof value.revision === "string" && "is_error" in value
    && typeof value.is_error === "boolean" && "result" in value;
}
function asError(error: unknown): Error { return error instanceof Error ? error : new Error("Engine stream failure."); }
