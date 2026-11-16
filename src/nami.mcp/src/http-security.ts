import { hostHeaderValidationResponse, originValidationResponse } from "@modelcontextprotocol/server";

/** The names this machine has for itself, as a Host header and a URL carry them. */
const LOOPBACK_NAMES: readonly string[] = ["localhost", "127.0.0.1", "[::1]"];
/** Every interface, IPv4, IPv6 and IPv4 mapped into IPv6, as a URL writes them. */
const WILDCARD_NAMES: readonly string[] = ["0.0.0.0", "[::]", "[::ffff:0:0]"];

/** What a request may name: its Host, and the Origin of the page that sent it if a browser did. */
export interface RequestNames {
  /** Host names, compared without a port: a proxy or a published container port may use any. */
  readonly hosts: readonly string[];
}

/** The host name of an address or URL host as a URL writes it (`0` is `0.0.0.0`, `::1` is `[::1]`), or undefined. */
function urlHostName(host: string): string | undefined {
  try { return new URL(`http://${host.includes(":") && !host.startsWith("[") ? `[${host}]` : host}`).hostname; } catch { return undefined; }
}

/** True for an address that means every interface: where a listener binds, never a name a client reached it by. */
export function isWildcardHost(host: string): boolean {
  const name = urlHostName(host);
  return name !== undefined && WILDCARD_NAMES.includes(name);
}

export function isLoopbackBindHost(host: string): boolean {
  return host === "127.0.0.1" || host === "localhost" || host === "::1";
}

/**
 * The names of this listener: this machine's own, the address it is bound to unless that is a wildcard, and the
 * public URL's. A wildcard is not a name: a request with Host 0.0.0.0, or from a page at http://0.0.0.0:5100,
 * says nothing about how it got here.
 */
export function requestNames(bindAddress: string, publicUrl: URL | undefined): RequestNames {
  const hosts = new Set(LOOPBACK_NAMES);
  const bound = urlHostName(bindAddress);
  if (bound !== undefined && !WILDCARD_NAMES.includes(bound)) hosts.add(bound);
  if (publicUrl) hosts.add(publicUrl.hostname);
  return { hosts: [...hosts] };
}

/**
 * DNS-rebinding protection in front of the MCP handler, which validates neither header itself: 403 unless the
 * Host, and the host of the Origin if there is one, is one of the listener's names.
 */
export function dnsRebindingResponse(request: Request, names: RequestNames): Response | undefined {
  return hostHeaderValidationResponse(request, [...names.hosts]) ?? originValidationResponse(request, [...names.hosts]);
}
