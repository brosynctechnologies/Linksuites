import type { MetadataRoute } from "next";
import { SITE_URL } from "@/lib/config";

/** Signed-in and token areas. Each blocks only that path and what sits under it, not every path sharing its first letters. */
const PRIVATE = ["/studio", "/me", "/trips", "/renew", "/admin", "/invite", "/support", "/reset-password"];

export default function robots(): MetadataRoute.Robots {
  return {
    rules: {
      userAgent: "*",
      // The agents' sign-up is public. It is the longer match, so it wins over the studio's "/studio/" rule.
      allow: ["/", "/studio/signup"],
      disallow: PRIVATE.flatMap((p) => [`${p}$`, `${p}/`, `${p}?`]),
    },
    sitemap: `${SITE_URL}/sitemap.xml`,
    host: SITE_URL,
  };
}
