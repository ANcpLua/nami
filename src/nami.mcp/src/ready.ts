import { renameSync, rmSync, writeFileSync } from "node:fs";
import { ENGINE_LIMITS } from "./contracts/generated/manifest.js";

/** Where the engine listens and what it stores in, as the engine itself reports after binding. */
export interface EngineListeners { otlp_http: string; otlp_grpc: string; data_path: string }

/**
 * The engine's one line on its fourth pipe (`--listeners-fd 3`), written once its listeners are bound: with a
 * port of 0 only the engine knows what the operating system chose. Fails when the engine ends without the line.
 */
export function readListeners(report: NodeJS.ReadableStream): Promise<EngineListeners> {
  const { promise, resolve: done, reject: refuse } = Promise.withResolvers<EngineListeners>();
  let text = "";
  report.setEncoding("utf8");
  report.on("data", (chunk: string) => { if (text.length <= ENGINE_LIMITS.max_frame_bytes) text += chunk; });
  report.on("error", refuse);
  report.on("end", () => {
    let value: unknown;
    try { value = JSON.parse(text); } catch { value = null; }
    if (isListeners(value)) done(value);
    else refuse(new Error("The engine did not report its listeners."));
  });
  // Only a host that writes a ready manifest reads the report; nobody else has to observe a failure.
  promise.catch(() => {});
  return promise;
}

function isListeners(value: unknown): value is EngineListeners {
  return typeof value === "object" && value !== null
    && "otlp_http" in value && typeof value.otlp_http === "string"
    && "otlp_grpc" in value && typeof value.otlp_grpc === "string"
    && "data_path" in value && typeof value.data_path === "string";
}

/**
 * The ready manifest: one JSON file that exists only while this nami is ready, with the URLs it really listens on.
 * It is written under another name and renamed, so a reader sees no file or the whole file, never a part of it.
 */
export function writeReadyManifest(file: string, mcp: string | null, listeners: EngineListeners): void {
  const manifest = { mcp, otlp_http: listeners.otlp_http, otlp_grpc: listeners.otlp_grpc, data_path: listeners.data_path };
  const partial = `${file}.${process.pid}.tmp`;
  writeFileSync(partial, JSON.stringify(manifest) + "\n");
  renameSync(partial, file);
}

/** Removes the manifest: before the engine starts (a nami that was killed left one behind) and when nami stops. */
export function removeReadyManifest(file: string): void {
  rmSync(file, { force: true });
}
