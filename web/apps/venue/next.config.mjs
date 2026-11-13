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
  basePath: "/venue",
  // next/image does not prefix static (svg) sources with basePath; components read this to do it.
  env: { NEXT_PUBLIC_BASE_PATH: "/venue" },
  reactStrictMode: true,
  poweredByHeader: false,
  transpilePackages: ["@malabak/ui", "@malabak/api-client", "@malabak/i18n", "@malabak/bff"],
  async redirects() {
    return [
      {
        source: "/",
        destination: "/venue",
        basePath: false,
        permanent: false,
      },
      {
        source: "/images/:path*",
        destination: "/venue/images/:path*",
        basePath: false,
        permanent: false,
      },
      {
        source: "/:path(accept-invitation|reset-password|verify-email|login|register)",
        destination: "/venue/:path",
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
