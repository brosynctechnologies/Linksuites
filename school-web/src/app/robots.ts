import type { MetadataRoute } from "next";
import { SITE_URL } from "@/lib/site";

/**
 * The signed-in and token areas are closed to crawlers. Each school's sign-in page (`/{slug}`) sits at the top level, so it
 * can't be listed here without closing the whole site: it carries `noindex` instead. Rules are prefix matches, so `/app` is
 * written as `/app$` and `/app/`: a bare `/app` would also block `/apple-icon`.
 */
export default function robots(): MetadataRoute.Robots {
  return {
    rules: {
      userAgent: "*",
      allow: "/",
      disallow: ["/app$", "/app/", "/parent", "/student", "/invite", "/support", "/reset-password"],
    },
    sitemap: `${SITE_URL}/sitemap.xml`,
    host: SITE_URL,
  };
}
