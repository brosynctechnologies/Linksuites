import type { MetadataRoute } from "next";
import { SITE_URL } from "@/lib/config";

/** CareLink's public pages. Clinics have no public pages of their own: patients reach the portal from their clinic's invitation. */
export default function sitemap(): MetadataRoute.Sitemap {
  return [
    { url: `${SITE_URL}/`, changeFrequency: "monthly", priority: 1 },
    { url: `${SITE_URL}/signup`, changeFrequency: "yearly", priority: 0.6 },
  ];
}
