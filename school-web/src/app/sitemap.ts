import type { MetadataRoute } from "next";
import { SITE_URL } from "@/lib/site";

/** SchoolLink's public pages. Schools' own sign-in pages are private to each school and stay out. */
export default function sitemap(): MetadataRoute.Sitemap {
  return [
    { url: `${SITE_URL}/`, changeFrequency: "monthly", priority: 1 },
    { url: `${SITE_URL}/signup`, changeFrequency: "yearly", priority: 0.8 },
    { url: `${SITE_URL}/demo`, changeFrequency: "yearly", priority: 0.6 },
  ];
}
