const securityHeaders = [
  { key: "X-Content-Type-Options", value: "nosniff" },
  { key: "X-Frame-Options", value: "DENY" },
  { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
  { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=()" },
  ...(process.env.NODE_ENV === "production"
    ? [{ key: "Strict-Transport-Security", value: "max-age=63072000; includeSubDomains; preload" }]
    : []),
];

// BASE_PATH: "/venue" (default) when this app shares one domain with the others and is routed by path,
// or "/" when it has its own subdomain (venue.mal3abk.app).
const basePath = (process.env.BASE_PATH ?? "/venue").trim().replace(/\/+$/, "");

/** @type {import('next').NextConfig} */
const nextConfig = {
  ...(basePath ? { basePath } : {}),
  // next/image does not prefix static (svg) sources with basePath; components read this to do it.
  env: { NEXT_PUBLIC_BASE_PATH: basePath },
  reactStrictMode: true,
  poweredByHeader: false,
  transpilePackages: ["@malabak/ui", "@malabak/api-client", "@malabak/i18n", "@malabak/bff"],
  async redirects() {
    if (!basePath) {
      // Own subdomain: old links that still carry /venue land on the same page without it.
      return [
        { source: "/venue", destination: "/", permanent: true },
        { source: "/venue/:path*", destination: "/:path*", permanent: true },
      ];
    }
    return [
      {
        source: "/",
        destination: basePath,
        basePath: false,
        permanent: false,
      },
      {
        source: "/images/:path*",
        destination: `${basePath}/images/:path*`,
        basePath: false,
        permanent: false,
      },
      {
        source: "/:path(accept-invitation|reset-password|verify-email|login|register)",
        destination: `${basePath}/:path`,
        basePath: false,
        permanent: false,
      },
    ];
  },
  async headers() {
    return [{ source: "/:path*", headers: securityHeaders }];
  },
};

export default nextConfig;
