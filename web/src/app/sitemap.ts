import type { MetadataRoute } from "next";
import { API_URL, SITE_URL } from "@/lib/config";


interface SlugPage {
  items: { slug: string; updatedAt: string }[];
  total: number;
}

const PAGE_SIZE = 1000;
/** A single sitemap holds up to 50,000 URLs; past that, split with generateSitemaps. */
const MAX_STORES = 45_000;

/** Every public store (active business with something for sale), from GET /api/public/stores. */
async function storeSlugs(): Promise<SlugPage["items"]> {
  const all: SlugPage["items"] = [];
  try {
    for (let page = 1; all.length < MAX_STORES; page++) {
      const res = await fetch(`${API_URL}/api/public/stores?page=${page}&pageSize=${PAGE_SIZE}`, {
        headers: { Accept: "application/json" },
        cache: "no-store", // read when the sitemap is requested, so the build never needs the API
      });
      if (!res.ok) break;
      const body = (await res.json()) as SlugPage;
      all.push(...body.items);
      if (body.items.length < PAGE_SIZE || page * PAGE_SIZE >= body.total) break;
    }
  } catch {
    // The API is down: still serve the marketing pages.
  }
  return all;
}

export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const stores = await storeSlugs();
  return [
    { url: `${SITE_URL}/`, changeFrequency: "monthly", priority: 1 },
    { url: `${SITE_URL}/signup`, changeFrequency: "yearly", priority: 0.6 },
    ...stores.map((s) => ({
      url: `${SITE_URL}/${encodeURIComponent(s.slug)}`,
      lastModified: s.updatedAt,
      changeFrequency: "daily" as const,
      priority: 0.8,
    })),
  ];
}
