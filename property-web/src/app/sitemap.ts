import type { MetadataRoute } from "next";
import { connection } from "next/server";
import { PURPOSES, SITE_URL } from "@/lib/config";
import { listingPath } from "@/lib/format";
import { serverGet } from "@/lib/server-api";
import type { AgencyPageDto, ListingCardDto, LocationDto, SearchResultDto } from "@/lib/types";

/** One sitemap holds at most 50,000 URLs. Past that, split it with generateSitemaps. */
const MAX_URLS = 50_000;
const PAGE_SIZE = 48;
const MAX_LISTING_PAGES = 400;
const MAX_LOCATIONS = 3_000;
const MAX_AGENCIES = 1_000;

const url = (path: string) => `${SITE_URL}${path}`;

/** Run `fn` over `items`, a few at a time, so a big sitemap doesn't open hundreds of API requests at once. */
async function inBatches<T, R>(items: T[], fn: (item: T) => Promise<R>, size = 8): Promise<R[]> {
  const out: R[] = [];
  for (let i = 0; i < items.length; i += size) out.push(...(await Promise.all(items.slice(i, i + size).map(fn))));
  return out;
}

/** Every place in the location tree (states, cities, areas), walked level by level from the top. */
async function allLocations(): Promise<LocationDto[]> {
  const roots = await serverGet<LocationDto[]>("/api/locations");
  const found: LocationDto[] = roots.ok && Array.isArray(roots.data) ? [...roots.data] : [];
  let level = found;
  while (level.length > 0 && found.length < MAX_LOCATIONS) {
    const children = await inBatches(level, async (l) => {
      const res = await serverGet<LocationDto[]>(`/api/locations?parent=${encodeURIComponent(l.slug)}`);
      return res.ok && Array.isArray(res.data) ? res.data : [];
    });
    level = children.flat();
    found.push(...level);
  }
  return found.slice(0, MAX_LOCATIONS);
}

/** Every live listing, newest first, from the public search. */
async function allListings(): Promise<ListingCardDto[]> {
  const path = (page: number) => `/api/listings?sort=newest&page=${page}&pageSize=${PAGE_SIZE}`;
  const first = await serverGet<SearchResultDto>(path(1));
  if (!first.ok) return [];
  const pages = Math.min(MAX_LISTING_PAGES, Math.ceil(first.data.items.total / PAGE_SIZE));
  const rest = await inBatches(
    Array.from({ length: Math.max(0, pages - 1) }, (_, i) => i + 2),
    async (page) => {
      const res = await serverGet<SearchResultDto>(path(page));
      return res.ok ? res.data.items.items : [];
    },
  );
  return [first.data.items.items, ...rest].flat();
}

/**
 * The marketplace's public pages: the landing pages, every search by purpose and place, area guides, listings,
 * agencies and their agents. Built per request from the public API (cached for minutes), so a build without the API
 * never bakes in an empty sitemap.
 */
export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  await connection();
  const [locations, listings] = await Promise.all([allLocations(), allListings()]);

  const agencySlugs = [...new Set(listings.map((l) => l.agencySlug))].slice(0, MAX_AGENCIES);
  const agencies = await inBatches(agencySlugs, async (slug) => {
    const res = await serverGet<AgencyPageDto>(`/api/agencies/${encodeURIComponent(slug)}`);
    return res.ok ? res.data : null;
  });

  const entries: MetadataRoute.Sitemap = [
    { url: url("/"), changeFrequency: "daily", priority: 1 },
    { url: url("/for-agents"), changeFrequency: "monthly", priority: 0.8 },
    ...PURPOSES.map((p) => ({ url: url(p.path), changeFrequency: "daily" as const, priority: 0.8 })),
    { url: url("/how-verification-works"), changeFrequency: "yearly", priority: 0.4 },
    { url: url("/signup"), changeFrequency: "yearly", priority: 0.3 },
    { url: url("/studio/signup"), changeFrequency: "yearly", priority: 0.4 },
    ...locations.flatMap((l) => [
      { url: url(`/areas/${l.slug}`), changeFrequency: "weekly" as const, priority: 0.6 },
      ...PURPOSES.map((p) => ({ url: url(`${p.path}/${l.slug}`), changeFrequency: "daily" as const, priority: 0.6 })),
    ]),
    ...listings.map((l) => ({
      url: url(listingPath(l.agencySlug, l.slug)),
      lastModified: l.publishedAt ?? undefined,
      changeFrequency: "weekly" as const,
      priority: 0.7,
      images: l.coverUrl ? [l.coverUrl] : undefined,
    })),
    ...agencies.flatMap((a) =>
      a
        ? [
            { url: url(`/agencies/${a.agency.slug}`), changeFrequency: "weekly" as const, priority: 0.5 },
            ...a.agents.map((agent) => ({ url: url(`/agents/${agent.slug}`), changeFrequency: "weekly" as const, priority: 0.4 })),
          ]
        : [],
    ),
  ];
  return [...new Map(entries.map((e) => [e.url, e])).values()].slice(0, MAX_URLS);
}
