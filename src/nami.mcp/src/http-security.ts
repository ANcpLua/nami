import { hostHeaderValidationResponse } from "@modelcontextprotocol/server";

/** The names this machine has for itself, as a Host header and a URL carry them. */
export const LOOPBACK_NAMES: readonly string[] = ["localhost", "127.0.0.1", "[::1]"];
/** Every interface, IPv4, IPv6 and IPv4 mapped into IPv6, as a URL writes them. */
const WILDCARD_NAMES: readonly string[] = ["0.0.0.0", "[::]", "[::ffff:0:0]"];

/** What a request may name: its Host, and the Origin of the page that sent it if a browser did. */
export interface RequestNames {
  /** Host names, compared without a port: a proxy or a published container port may use any. */
  readonly hosts: readonly string[];
  /** The one origin outside this machine: scheme, host and port of NAMI_PUBLIC_URL. */
  readonly publicOrigin: string | undefined;
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
  return { hosts: [...hosts], publicOrigin: publicUrl?.origin };
}

function refusedOrigin(request: Request, publicOrigin: string | undefined): Response | undefined {
  // No Origin: not a browser's cross-origin request. Other clients do not send one.
  const header = request.headers.get("origin");
  if (header === null || header === "") return undefined;
  let origin: URL | undefined;
  try { origin = new URL(header); } catch { /* "null" and anything else that is no URL is refused below */ }
  // A page of this machine has no fixed port (a local MCP client, a container port published on a port of
  // Docker's choice), so every HTTP(S) origin on a loopback name passes. Any other origin has to be the public
  // one in scheme, host and port: https://nami.example admits neither http://nami.example nor
  // https://nami.example:8443.
  const local = origin !== undefined && (origin.protocol === "http:" || origin.protocol === "https:")
    && LOOPBACK_NAMES.includes(origin.hostname);
  if (local || (origin !== undefined && publicOrigin !== undefined && origin.origin === publicOrigin)) return undefined;
  return Response.json({ jsonrpc: "2.0", error: { code: -32000, message: "Invalid Origin." }, id: null }, { status: 403 });
}

/**
 * The Host under which a platform's deploy check asks GET /health. Railway's does not use the service's own
 * name: "Railway uses the hostname healthcheck.railway.app when performing healthchecks", and a service that
 * restricts hosts has to allow it (https://docs.railway.com/deployments/healthchecks).
 */
const HEALTH_PROBE_HOSTS: readonly string[] = ["healthcheck.railway.app"];

/** True for GET /health under a platform's probe Host: the one request a foreign Host is answered. */
function isPlatformHealthProbe(request: Request): boolean {
  if (request.method !== "GET") return false;
  try {
    const host = new URL(`http://${request.headers.get("host") ?? ""}`).hostname;
    return HEALTH_PROBE_HOSTS.includes(host) && new URL(request.url).pathname === "/health";
  } catch { return false; }
}

/**
 * DNS-rebinding protection in front of the MCP handler, which validates neither header itself: 403 unless the
 * Host is one of the listener's names and the Origin, if there is one, is an origin of this machine or the
 * public one. A platform's health probe is the exception for the Host, and for GET /health alone: under its
 * Host /mcp and every other path stay refused, and its Origin is judged like any other.
 */
export function dnsRebindingResponse(request: Request, names: RequestNames): Response | undefined {
  const refusedHost = hostHeaderValidationResponse(request, [...names.hosts]);
  if (refusedHost && !isPlatformHealthProbe(request)) return refusedHost;
  return refusedOrigin(request, names.publicOrigin);
}
