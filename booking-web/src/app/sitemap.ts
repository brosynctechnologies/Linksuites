import type { MetadataRoute } from "next";
import { SITE_URL } from "@/lib/site";

/**
 * BookLink's own pages. Businesses' booking pages (/{slug}) are not listed: the API has no public list of them yet
 * (docs/BOOKINGS_API.md), so they are found through the links businesses share.
 */
export default function sitemap(): MetadataRoute.Sitemap {
  return [
    { url: `${SITE_URL}/`, changeFrequency: "monthly", priority: 1 },
    { url: `${SITE_URL}/signup`, changeFrequency: "yearly", priority: 0.6 },
  ];
}
