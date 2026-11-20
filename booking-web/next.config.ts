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
};

export default nextConfig;
