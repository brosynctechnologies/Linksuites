import type { MetadataRoute } from "next";
import { SITE_URL } from "@/lib/config";

/** Signed-in, payment and token pages stay out of search. Certificates stay crawlable so their noindex is seen. */
export default function robots(): MetadataRoute.Robots {
  return {
    rules: {
      userAgent: "*",
      allow: "/",
      disallow: ["/learn", "/studio", "/checkout", "/admin", "/invite", "/support", "/reset-password"],
    },
    sitemap: `${SITE_URL}/sitemap.xml`,
    host: SITE_URL,
  };
}
