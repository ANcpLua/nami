import { CONFIG, CONFIG_DEFAULTS } from "./contracts/generated/manifest.js";

/**
 * The port of the MCP listener as configured: NAMI_PORT, else the platform's PORT, else the default. The host
 * binds it and the container's health probe asks it, so both read it here. 0 lets the operating system choose;
 * only the ready manifest then names the port.
 */
export function configuredPort(environment: NodeJS.ProcessEnv): number {
  // Decimal digits only, as for the stop budget (main.ts): Number() also took "", " ", 0x10 and 1e3.
  const name = environment[CONFIG.Port] !== undefined ? CONFIG.Port : environment[CONFIG.PlatformPort] !== undefined ? CONFIG.PlatformPort : undefined;
  const text = name ? environment[name]! : CONFIG_DEFAULTS.Port;
  const port = /^[0-9]{1,5}$/.test(text) ? Number(text) : Number.NaN;
  if (!Number.isInteger(port) || port > 65535)
    throw new Error(`${name ?? CONFIG.Port} must be a port from 0 to 65535 in decimal digits only; unset it to use `
      + (name === CONFIG.Port ? `${CONFIG.PlatformPort} or ${CONFIG_DEFAULTS.Port}.` : `${CONFIG_DEFAULTS.Port}.`));
  return port;
}
