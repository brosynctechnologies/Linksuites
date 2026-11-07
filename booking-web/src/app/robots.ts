import type { MetadataRoute } from "next";
import { SITE_URL } from "@/lib/site";

/**
 * The signed-in app, customers' manage-booking links and the token pages stay out of search. Rules match by prefix and
 * businesses' pages live at /{slug}, so each private path is anchored: "/app" alone would also block /apple-icon and a
 * business called "applause".
 */
const PRIVATE = ["/app", "/invite", "/support", "/reset-password"].flatMap((p) => [`${p}$`, `${p}/`]);

export default function robots(): MetadataRoute.Robots {
  return {
    rules: { userAgent: "*", allow: "/", disallow: [...PRIVATE, "/b/"] },
    sitemap: `${SITE_URL}/sitemap.xml`,
    host: SITE_URL,
  };
}
