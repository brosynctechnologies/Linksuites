import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  output: "standalone",
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
