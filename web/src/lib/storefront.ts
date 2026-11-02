import { cache } from "react";
import { API_URL } from "./config";
import type { PublicOrderDto, PublicProductDto, PublicStoreDto } from "./types";

/** Server-side GET against the public API. Returns null on 404; throws on other failures. */
async function getPublic<T>(path: string): Promise<T | null> {
  const res = await fetch(`${API_URL}${path}`, {
    cache: "no-store",
    headers: { Accept: "application/json" },
  });
  if (res.status === 404) return null;
  if (!res.ok) throw new Error(`API ${res.status} for ${path}`);
  return (await res.json()) as T;
}

const enc = encodeURIComponent;

export const getStore = cache((slug: string) => getPublic<PublicStoreDto>(`/api/public/stores/${enc(slug)}`));

export const getProducts = cache(
  async (slug: string) => (await getPublic<PublicProductDto[]>(`/api/public/stores/${enc(slug)}/products`)) ?? [],
);

export const getProduct = cache((slug: string, productSlug: string) =>
  getPublic<PublicProductDto>(`/api/public/stores/${enc(slug)}/products/${enc(productSlug)}`),
);

export const getPublicOrder = cache((code: string) => getPublic<PublicOrderDto>(`/api/public/orders/${enc(code)}`));

/** Sanitise a theme colour coming from the API before putting it in a style attribute. */
export function accentOf(themeColor: string | null | undefined): string {
  return themeColor && /^#[0-9a-fA-F]{3,8}$/.test(themeColor) ? themeColor : "#16a34a";
}

/** Text colour (white or indigo ink) that reads best on the given background colour. */
export function readableOn(hex: string): string {
  let h = hex.replace("#", "");
  if (h.length === 3 || h.length === 4) h = h.slice(0, 3).split("").map((c) => c + c).join("");
  h = h.slice(0, 6);
  const n = parseInt(h, 16);
  if (h.length !== 6 || Number.isNaN(n)) return "#ffffff";
  const lin = (v: number) => {
    const s = v / 255;
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  };
  const lum = 0.2126 * lin((n >> 16) & 255) + 0.7152 * lin((n >> 8) & 255) + 0.0722 * lin(n & 255);
  const ink = 0.0152; // relative luminance of #1b1f3b
  const onWhite = 1.05 / (lum + 0.05);
  const onInk = (lum + 0.05) / (ink + 0.05);
  return onWhite >= onInk ? "#ffffff" : "#1b1f3b";
}
