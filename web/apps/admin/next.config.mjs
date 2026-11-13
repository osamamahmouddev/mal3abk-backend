const securityHeaders = [
  { key: "X-Content-Type-Options", value: "nosniff" },
  { key: "X-Frame-Options", value: "DENY" },
  { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
  { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=()" },
  ...(process.env.NODE_ENV === "production"
    ? [{ key: "Strict-Transport-Security", value: "max-age=63072000; includeSubDomains; preload" }]
    : []),
];

/** @type {import('next').NextConfig} */
const nextConfig = {
  basePath: "/admin",
  // next/image does not prefix static (svg) sources with basePath; components read this to do it.
  env: { NEXT_PUBLIC_BASE_PATH: "/admin" },
  reactStrictMode: true,
  // The dev-mode indicator floats over bottom-left buttons; E2E runs (E2E=1) hide it so clicks reach the page.
  ...(process.env.E2E === "1" ? { devIndicators: false } : {}),
  poweredByHeader: false,
  transpilePackages: ["@malabak/ui", "@malabak/api-client", "@malabak/i18n", "@malabak/bff"],
  async redirects() {
    return [
      {
        source: "/",
        destination: "/admin",
        basePath: false,
        permanent: false,
      },
      {
        source: "/images/:path*",
        destination: "/admin/images/:path*",
        basePath: false,
        permanent: false,
      },
      {
        source: "/:path(login|featured|venues|approvals|billing|teams|support|settings|accounts|audit)",
        destination: "/admin/:path",
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
