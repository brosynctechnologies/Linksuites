import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  output: "standalone",
  cacheComponents: true,
  partialPrefetching: true,
  // Public pages show no prices. Any old pricing link lands on the plans section of the home page.
  redirects() {
    return [{ source: "/pricing", destination: "/#plans", permanent: true }];
  },
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
  // Images are rendered with plain <img> tags (smaller bundles, no optimizer
  // round-trip). These patterns are here in case next/image is adopted later.
  images: {
    remotePatterns: [
      { protocol: "http", hostname: "localhost", port: "5080" },
      { protocol: "https", hostname: "*.r2.dev" },
    ],
  },
};

export default nextConfig;
