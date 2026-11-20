import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  output: "standalone",
  cacheComponents: true,
  partialPrefetching: true,
  // Public pages show no prices. Any old pricing link lands on the plans section of the home page.
  redirects() {
    return [
      { source: "/pricing", destination: "/#plans", permanent: true },
      // Suite notifications and emails link to /app/...; OrderLink's dashboard lives at /dashboard/...
      { source: "/app/:path*", destination: "/dashboard/:path*", permanent: false },
    ];
  },
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
  // Storefront product photos use next/image when they come from the API's own /uploads (components/upload-image.tsx);
  // that host is read from NEXT_PUBLIC_API_URL at build time. Other images stay plain <img> tags.
  images: {
    remotePatterns: [
      new URL(`${(process.env.NEXT_PUBLIC_API_URL || "http://localhost:5080").replace(/\/+$/, "")}/uploads/**`),
      { protocol: "https", hostname: "*.r2.dev" },
    ],
  },
};

export default nextConfig;
