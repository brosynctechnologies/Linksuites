import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  output: "standalone",
  /* config options here */
  cacheComponents: true,
  partialPrefetching: true,
  // Signed-in pages are client-rendered behind a session guard, so only segments that opt in with `instant` are checked.
  experimental: { instantInsights: { validationLevel: "manual-warning" } },
  // Plan prices are never public: the old pricing page sends agents to the plans section of their landing page.
  async redirects() {
    return [{ source: "/pricing", destination: "/for-agents#plans", permanent: true }];
  },
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
};

export default nextConfig;
