import { cacheLife } from "next/cache";
import { API_URL } from "./config";

/** Result of a public read on the server: data, or why there is none. */
export type Loaded<T> = { ok: true; data: T } | { ok: false; status: number };

class Unreachable extends Error {}

/**
 * The cached read. Successful answers and clean 4xx answers (404 for an unknown listing) are cached for
 * minutes; an unreachable API or a 5xx throws, which `use cache` never stores, so the next request retries.
 */
async function cachedGet<T>(path: string): Promise<Loaded<T>> {
  "use cache";
  cacheLife("minutes");
  let res: Response;
  try {
    res = await fetch(`${API_URL}${path}`, { headers: { Accept: "application/json" } });
  } catch {
    throw new Unreachable("unreachable");
  }
  if (res.status >= 500) throw new Unreachable(`status ${res.status}`);
  if (!res.ok) return { ok: false, status: res.status };
  return { ok: true, data: (await res.json()) as T };
}

/**
 * GET a public endpoint from a Server Component. Never throws: a 404, a 5xx or an unreachable API comes back
 * as `{ ok: false, status }` (status 0 = unreachable) so the page can explain itself instead of crashing.
 * Callers render inside Suspense after reading request data (params, searchParams or `connection()`), so a
 * build without the API never bakes an error into a static page.
 */
export async function serverGet<T>(path: string): Promise<Loaded<T>> {
  try {
    return await cachedGet<T>(path);
  } catch {
    return { ok: false, status: 0 };
  }
}

/** Build a query string from defined, non-empty values. */
export function query(params: Record<string, string | number | boolean | null | undefined>): string {
  const sp = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v === null || v === undefined || v === "" || v === false) continue;
    sp.set(k, String(v));
  }
  const s = sp.toString();
  return s ? `?${s}` : "";
}

/** First value of a search param. */
export function first(v: string | string[] | undefined): string | undefined {
  return Array.isArray(v) ? v[0] : v;
}
