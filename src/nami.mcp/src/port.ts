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

/**
 * The host of a URL that reaches a listener bound to `bind`, bracketed when it is an IPv6 address. A wildcard
 * listener is reached over loopback; a listener on one address only at that address.
 */
export function reachableHost(bind: string): string {
  const host = bind === "0.0.0.0" ? "127.0.0.1" : bind === "::" ? "::1" : bind;
  return host.includes(":") ? `[${host}]` : host;
}
