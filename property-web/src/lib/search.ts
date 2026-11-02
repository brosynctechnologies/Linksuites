// Search state lives in the URL so results are shareable and server-rendered:
//   /rent, /rent/{location}, /buy[/{location}], /shortlet[/{location}]
//   ?type=&minPrice=&maxPrice=&beds=&amenities=pool,parking&verified=1&bbox=w,s,e,n&sort=&page=
// Prices in the URL are whole naira (readable when shared); the API takes kobo.

import { PURPOSES } from "./config";
import type { ListingFilter, ListingPurpose } from "./types";

export interface SearchState {
  purpose: ListingPurpose;
  location?: string;
  type?: string;
  minPrice?: string;
  maxPrice?: string;
  beds?: string;
  amenities?: string;
  verified?: boolean;
  bbox?: string;
  sort?: string;
  page?: number;
}

type Params = Record<string, string | string[] | undefined>;
const one = (v: string | string[] | undefined) => (Array.isArray(v) ? v[0] : v) || undefined;
const digits = (v: string | undefined) => (v && /^\d+$/.test(v.replace(/,/g, "")) ? v.replace(/,/g, "") : undefined);

export function purposePath(purpose: ListingPurpose): string {
  return PURPOSES.find((p) => p.value === purpose)?.path ?? "/rent";
}

export function purposeLabel(purpose: ListingPurpose): string {
  return purpose === "Sale" ? "for sale" : purpose === "ShortLet" ? "short-lets" : "for rent";
}

export function readSearch(purpose: ListingPurpose, location: string | undefined, sp: Params): SearchState {
  return {
    purpose,
    location: location ? decodeURIComponent(location) : undefined,
    type: one(sp.type),
    minPrice: digits(one(sp.minPrice)),
    maxPrice: digits(one(sp.maxPrice)),
    beds: digits(one(sp.beds)),
    amenities: one(sp.amenities),
    verified: one(sp.verified) === "1" || one(sp.verified) === "true",
    bbox: one(sp.bbox),
    sort: one(sp.sort),
    page: Math.max(1, Number(one(sp.page)) || 1),
  };
}

/** The page URL for a search state. */
export function searchHref(s: SearchState): string {
  const base = purposePath(s.purpose) + (s.location ? `/${encodeURIComponent(s.location)}` : "");
  const q = new URLSearchParams();
  if (s.type) q.set("type", s.type);
  if (s.minPrice) q.set("minPrice", s.minPrice);
  if (s.maxPrice) q.set("maxPrice", s.maxPrice);
  if (s.beds) q.set("beds", s.beds);
  if (s.amenities) q.set("amenities", s.amenities);
  if (s.verified) q.set("verified", "1");
  if (s.bbox) q.set("bbox", s.bbox);
  if (s.sort && s.sort !== "recommended") q.set("sort", s.sort);
  if (s.page && s.page > 1) q.set("page", String(s.page));
  const qs = q.toString();
  return qs ? `${base}?${qs}` : base;
}

const kobo = (naira: string | undefined) => (naira ? String(Number(naira) * 100) : undefined);

/** The `/api/listings` query for a search state. */
export function searchApiPath(s: SearchState, pageSize = 20): string {
  const q = new URLSearchParams();
  q.set("purpose", s.purpose);
  if (s.location) q.set("location", s.location);
  if (s.type) q.set("type", s.type);
  const min = kobo(s.minPrice);
  const max = kobo(s.maxPrice);
  if (min) q.set("minPrice", min);
  if (max) q.set("maxPrice", max);
  if (s.beds) q.set("beds", s.beds);
  if (s.amenities) q.set("amenities", s.amenities);
  if (s.verified) q.set("verified", "true");
  if (s.bbox) q.set("bbox", s.bbox);
  if (s.sort) q.set("sort", s.sort);
  q.set("page", String(s.page ?? 1));
  q.set("pageSize", String(pageSize));
  return `/api/listings?${q.toString()}`;
}

/** A search state as the `ListingFilter` stored on saved searches. */
export function toListingFilter(s: SearchState): ListingFilter {
  const bbox = s.bbox?.split(",").map(Number);
  return {
    purpose: s.purpose,
    location: s.location ?? null,
    type: s.type ?? null,
    minPrice: s.minPrice ? Number(s.minPrice) * 100 : null,
    maxPrice: s.maxPrice ? Number(s.maxPrice) * 100 : null,
    minBeds: s.beds ? Number(s.beds) : null,
    amenities: s.amenities ? s.amenities.split(",").filter(Boolean) : null,
    verifiedOnly: !!s.verified,
    bbox: bbox && bbox.length === 4 && bbox.every(Number.isFinite) ? bbox : null,
  };
}

/** A saved search's filter back to the page that shows it. */
export function filterHref(f: ListingFilter): string {
  return searchHref({
    purpose: (f.purpose as ListingPurpose) || "Rent",
    location: f.location ?? undefined,
    type: f.type ?? undefined,
    minPrice: f.minPrice ? String(Math.round(f.minPrice / 100)) : undefined,
    maxPrice: f.maxPrice ? String(Math.round(f.maxPrice / 100)) : undefined,
    beds: f.minBeds ? String(f.minBeds) : undefined,
    amenities: f.amenities?.length ? f.amenities.join(",") : undefined,
    verified: f.verifiedOnly,
    bbox: f.bbox?.length === 4 ? f.bbox.map((n) => n.toFixed(5)).join(",") : undefined,
  });
}

/** True when anything beyond purpose and location narrows the results. */
export function hasFilters(s: SearchState): boolean {
  return !!(s.type || s.minPrice || s.maxPrice || s.beds || s.amenities || s.verified || s.bbox);
}
