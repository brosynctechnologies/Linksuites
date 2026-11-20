import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  output: "standalone",
  /* config options here */
  cacheComponents: true,
  // Wave 4 (PWA): the service worker is registered as /sw.js?v=<this>, so each build installs a fresh worker (and drops old caches).
  env: { NEXT_PUBLIC_SW_VERSION: process.env.SW_VERSION || Date.now().toString(36) },
  headers() {
    return Promise.resolve([{ source: "/sw.js", headers: [{ key: "Cache-Control", value: "no-cache" }] }]);
  },
  partialPrefetching: true,
  // Signed-in pages are client-rendered behind a session guard, so only segments that opt in with `instant` are checked.
  experimental: { instantInsights: { validationLevel: "manual-warning" } },
  // Public pages show no plan prices; tutors' plans live on the teaching page and inside the studio.
  async redirects() {
    return [{ source: "/pricing", destination: "/teach#plans", permanent: true }];
  },
  // Course thumbnails use next/image when they come from the API's own /uploads (components/upload-image.tsx);
  // that host is read from NEXT_PUBLIC_API_URL at build time. Other images stay plain <img> tags.
  images: {
    remotePatterns: [new URL(`${(process.env.NEXT_PUBLIC_API_URL || "http://localhost:5080").replace(/\/+$/, "")}/uploads/**`)],
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
