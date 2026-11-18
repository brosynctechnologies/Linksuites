import type { MetadataRoute } from "next";
import { API_URL } from "@/lib/config";
import { SITE_URL } from "@/lib/site";


interface SlugPage {
  items: { slug: string; updatedAt: string }[];
  total: number;
}

const PAGE_SIZE = 1000;
/** A single sitemap holds up to 50,000 URLs; past that, split with generateSitemaps. */
const MAX_PAGES = 45_000;

/** Every public booking page (active business taking bookings online), from GET /api/public/book. */
async function bookingSlugs(): Promise<SlugPage["items"]> {
  const all: SlugPage["items"] = [];
  try {
    for (let page = 1; all.length < MAX_PAGES; page++) {
      const res = await fetch(`${API_URL}/api/public/book?page=${page}&pageSize=${PAGE_SIZE}`, {
        headers: { Accept: "application/json" },
        cache: "no-store", // read when the sitemap is requested, so the build never needs the API
      });
      if (!res.ok) break;
      const body = (await res.json()) as SlugPage;
      all.push(...body.items);
      if (body.items.length < PAGE_SIZE || page * PAGE_SIZE >= body.total) break;
    }
  } catch {
    // The API is down: still serve BookLink's own pages.
  }
  return all;
}

export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const pages = await bookingSlugs();
  return [
    { url: `${SITE_URL}/`, changeFrequency: "monthly", priority: 1 },
    { url: `${SITE_URL}/signup`, changeFrequency: "yearly", priority: 0.6 },
    ...pages.map((p) => ({
      url: `${SITE_URL}/${encodeURIComponent(p.slug)}`,
      lastModified: p.updatedAt,
      changeFrequency: "weekly" as const,
      priority: 0.8,
    })),
  ];
}
