import { CONFIG, CONFIG_DEFAULTS } from "./contracts/generated/manifest.js";

/**
 * The port of the MCP listener as configured: NAMI_PORT, else the platform's PORT, else the default. The host
 * binds it and the container's health probe asks it, so both read it here. 0 lets the operating system choose;
 * only the ready manifest then names the port.
 */
export function configuredPort(environment: NodeJS.ProcessEnv): number {
  const port = Number(environment[CONFIG.Port] ?? environment[CONFIG.PlatformPort] ?? CONFIG_DEFAULTS.Port);
  if (!Number.isInteger(port) || port < 0 || port > 65535) throw new Error("NAMI_PORT is outside its supported range.");
  return port;
}
