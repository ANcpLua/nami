import { fromJsonSchema, type JsonSchemaType } from "@modelcontextprotocol/server";
import schemas from "./contracts/generated/contract-schemas.json" with {type: "json"};
import { TOOLS } from "./contracts/generated/manifest.js";
import type { ToolName, ToolReply, ValidatedToolCall } from "./contracts/generated/tool-contracts.js";
const replySchemas = Object.fromEntries(TOOLS.map(tool => [tool.name, fromJsonSchema(schemas[tool.output] as JsonSchemaType)]));
const errorSchema = fromJsonSchema(schemas["Common.ToolError"] as JsonSchemaType);
import { spawn } from "node:child_process";
import { once } from "node:events";
import type { EngineCommand, EngineResponse } from "./contracts/generated/api.js";
import type { EngineResponse as EngineResponseJson } from "./contracts/generated/json.js";
import { EngineCommandJson, EngineResponseJson as responseCodec } from "./contracts/generated/serialization.js";
import { CONTRACT_REVISION, ENGINE_LIMITS } from "./contracts/generated/manifest.js";

/** One supervised native engine, with bounded complete JSONL frames and request correlation. */
export class EngineError extends Error {
  constructor(readonly code: "collector_unavailable" | "collector_contract_mismatch" | "invalid_argument", message: string) { super(message); }
}
export class Engine {
  private readonly child;
  private readonly exited: Promise<void>;
  private readonly writesClosed = new AbortController();
  private readonly pending = new Map<string, { resolve: (reply: EngineResponse) => void; reject: (error: Error) => void; cleanup: () => void }>();
  private sequence = 0;
  private stopped = false;
  private hasExited = false;
  private shutdown: Promise<void> | undefined;
  readonly ready: Promise<EngineResponse>;

  constructor(path: string) {
    this.child = spawn(path.endsWith(".dll") ? "dotnet" : path,
      path.endsWith(".dll") ? [path, "--engine"] : ["--engine"], { stdio: ["pipe", "pipe", "inherit"] });
    const stopChild = () => { this.child.kill("SIGKILL"); };
    process.once("exit", stopChild);
    this.exited = new Promise(resolve => this.child.once("close", () => {
      this.hasExited = true;
      process.removeListener("exit", stopChild);
      resolve();
    }));
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
    const request: EngineCommand = method === "health" ? { id, revision: CONTRACT_REVISION, method } : { id, revision: CONTRACT_REVISION, method, name: name ?? "", input, credential, traceparent, tracestate };
    // Refuse this request before it owns pending state or can affect the shared child.
    const frame = encode(request);
    const result = new Promise<EngineResponse>((resolve, reject) => {
      const abort = () => {
        this.pending.delete(id);
        signal?.removeEventListener("abort", abort);
        reject(new Error("Engine request cancelled."));
        void this.write(encode({ id, revision: CONTRACT_REVISION, method: "cancel" })).catch((error: unknown) => this.fail(asError(error)));
      };
      this.pending.set(id, { resolve, reject, cleanup: () => signal?.removeEventListener("abort", abort) });
      signal?.addEventListener("abort", abort, { once: true });
    });
    // The response promise must be observed even while the pipe is waiting for drain.
    void this.write(frame).catch((error: unknown) => this.fail(asError(error)));
    return result;
  }

  async call<N extends ToolName>(call: Extract<ValidatedToolCall, {name: N}>,
    credential: string | null, signal?: AbortSignal, traceparent: string | null = null,
    tracestate: string | null = null): Promise<ToolReply<N>> {
    const reply = await this.request("call", call.name, call.input, credential, signal, traceparent, tracestate);
    const validator = reply.isError ? errorSchema : replySchemas[call.name]!;
    const checked = await validator["~standard"].validate(reply.result);
    if (checked.issues) throw new EngineError("collector_contract_mismatch", "Engine result violates its declared tool contract.");
    return {isError: reply.isError, result: checked.value} as ToolReply<N>;
  }

  close(): Promise<void> {
    if (this.shutdown) return this.shutdown;
    this.stopped = true;
    this.writesClosed.abort();
    this.rejectPending(new EngineError("collector_unavailable", "Engine is closing."));
    this.shutdown = this.stopChild();
    return this.shutdown;
  }

  private async stopChild(): Promise<void> {
    if (this.hasExited) return;
    this.child.stdin.end();
    const graceful = setTimeout(() => this.child.kill("SIGTERM"), 5000);
    const forced = setTimeout(() => this.child.kill("SIGKILL"), 8000);
    graceful.unref(); forced.unref();
    try { await this.exited; } finally { clearTimeout(graceful); clearTimeout(forced); }
  }

  private async write(frame: string): Promise<void> {
    if (!this.child.stdin.write(frame)) await once(this.child.stdin, "drain", { signal: this.writesClosed.signal });
  }

  private fail(error: Error): void {
    if (this.stopped) return;
    this.stopped = true;
    this.writesClosed.abort();
    this.rejectPending(error);
    this.child.kill("SIGTERM");
    void this.close();
  }

  private rejectPending(error: Error): void {
    for (const request of this.pending.values()) { request.cleanup(); request.reject(error); }
    this.pending.clear();
  }
}

function encode(request: EngineCommand): string {
  const frame = JSON.stringify(EngineCommandJson.toJson(request)) + "\n";
  if (Buffer.byteLength(frame) > ENGINE_LIMITS.max_frame_bytes)
    throw new EngineError("invalid_argument", "The encoded request exceeds the engine frame limit.");
  return frame;
}

function isReply(value: unknown): value is EngineResponseJson {
  return typeof value === "object" && value !== null && "id" in value && typeof value.id === "string"
    && "revision" in value && typeof value.revision === "string" && "is_error" in value
    && typeof value.is_error === "boolean" && "result" in value;
}
function asError(error: unknown): Error { return error instanceof Error ? error : new Error("Engine stream failure."); }
