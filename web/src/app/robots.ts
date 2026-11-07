import type { MetadataRoute } from "next";
import { SITE_URL } from "@/lib/config";

/**
 * Stores and products are public. The dashboard, support view, password reset and customers' order pages are not.
 * Rules match by prefix, and stores live at /{slug}, so each private path is anchored: "/support" alone would also block a
 * store called "support-foods".
 */
export default function robots(): MetadataRoute.Robots {
  return {
    rules: {
      userAgent: "*",
      allow: "/",
      disallow: ["/dashboard$", "/dashboard/", "/support$", "/support/", "/reset-password$", "/reset-password/", "/o/"],
    },
    sitemap: `${SITE_URL}/sitemap.xml`,
    host: SITE_URL,
  };
}
