import type { MetadataRoute } from "next";
import { SITE_URL } from "@/lib/site";

/** The hub is one page. Each app publishes its own sitemap on its own domain. */
export default function sitemap(): MetadataRoute.Sitemap {
  return [{ url: `${SITE_URL}/`, changeFrequency: "monthly", priority: 1 }];
}
