import type { MetadataRoute } from "next";
import { SITE_URL } from "@/lib/config";

/** The public pages. Invoices and quotes are private links, so none are listed. */
export default function sitemap(): MetadataRoute.Sitemap {
  return [
    { url: `${SITE_URL}/`, changeFrequency: "monthly", priority: 1 },
    { url: `${SITE_URL}/create`, changeFrequency: "monthly", priority: 0.9 },
    { url: `${SITE_URL}/signup`, changeFrequency: "yearly", priority: 0.5 },
  ];
}
