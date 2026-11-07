import type { MetadataRoute } from "next";
import { SITE_URL } from "@/lib/config";

/**
 * The marketing pages. Stores and their products are public and indexable, but the API has no endpoint that lists every
 * store, so they can't be enumerated here yet; add them (with generateSitemaps past 50,000 URLs) once it does.
 */
export default function sitemap(): MetadataRoute.Sitemap {
  return [
    { url: `${SITE_URL}/`, changeFrequency: "monthly", priority: 1 },
    { url: `${SITE_URL}/signup`, changeFrequency: "yearly", priority: 0.6 },
  ];
}
