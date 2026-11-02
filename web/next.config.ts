import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  cacheComponents: true,
  partialPrefetching: true,
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
