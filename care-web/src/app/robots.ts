import type { MetadataRoute } from "next";
import { SITE_URL } from "@/lib/config";

/**
 * The clinic app, the portal, invitations, password resets and support views are private. The portal's sign-in page stays crawlable so its
 * noindex is seen. "/app$" and "/app/" rather than "/app", which would also match /apple-icon.
 */
export default function robots(): MetadataRoute.Robots {
  return {
    rules: {
      userAgent: "*",
      allow: ["/", "/portal/sign-in"],
      disallow: ["/app$", "/app/", "/portal", "/invite", "/reset-password", "/support"],
    },
    sitemap: `${SITE_URL}/sitemap.xml`,
    host: SITE_URL,
  };
}
