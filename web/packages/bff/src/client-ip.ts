/**
 * The API rate-limits and audits by client IP. Calls arrive from this server, so pass on the browser's
 * IP the way the API expects it from a trusted proxy (CF-Connecting-IP). In production the BFF's
 * egress address must be listed in the API's Cloudflare:AdditionalTrustedProxies.
 */
export function clientIpHeaders(incoming: Headers): Record<string, string> {
  const ip = incoming.get("cf-connecting-ip") ?? incoming.get("x-forwarded-for")?.split(",")[0]?.trim() ?? incoming.get("x-real-ip");
  return ip ? { "cf-connecting-ip": ip } : {};
}
