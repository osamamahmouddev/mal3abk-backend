/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  transpilePackages: ["@malabak/ui", "@malabak/api-client", "@malabak/i18n"],
  i18n: undefined, // handled via next-intl App Router
};

export default nextConfig;
