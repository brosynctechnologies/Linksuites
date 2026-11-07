import type { MetadataRoute } from "next";
import { SITE_URL } from "@/lib/config";

/**
 * The signed-in app, team invites, password resets, the support view and clients' private invoice and quote links stay
 * out. "/app/" and "/app$" rather than "/app", which as a prefix would also block "/apple-icon".
 */
export default function robots(): MetadataRoute.Robots {
  return {
    rules: { userAgent: "*", allow: "/", disallow: ["/app$", "/app/", "/invite/", "/reset-password", "/support", "/i/", "/q/"] },
    sitemap: `${SITE_URL}/sitemap.xml`,
    host: SITE_URL,
  };
}
