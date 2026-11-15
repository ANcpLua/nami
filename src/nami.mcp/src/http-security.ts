import {
  hostHeaderValidationResponse,
  localhostAllowedHostnames,
  localhostAllowedOrigins,
  originValidationResponse,
} from "@modelcontextprotocol/server";

function unique(values: readonly string[]): string[] {
  return [...new Set(values)];
}

/**
 * DNS-rebinding protection in front of the MCP handler, which validates
 * neither header itself. Without explicit allowlists this is a loopback
 * process, and the SDK's localhost sets are the correct answer — the same
 * default the framework app factories arm.
 */
export function dnsRebindingResponse(
  request: Request,
  allowedHosts?: readonly string[],
  allowedOrigins?: readonly string[],
): Response | undefined {
  return hostHeaderValidationResponse(
    request,
    unique(allowedHosts ?? localhostAllowedHostnames()),
  ) ?? originValidationResponse(
    request,
    unique(allowedOrigins ?? localhostAllowedOrigins()),
  );
}

export function isLoopbackBindHost(host: string): boolean {
  return host === "127.0.0.1" || host === "localhost" || host === "::1";
}
