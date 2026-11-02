import { API_URL } from "./config";

/** Result of a public read on the server: data, or why there is none. */
export type Loaded<T> = { ok: true; data: T } | { ok: false; status: number };

/**
 * GET a public catalogue endpoint from a Server Component. Never throws: a 404 or an unreachable API
 * comes back as `{ ok: false, status }` so the page can explain itself instead of failing.
 * Called inside Suspense boundaries; the read happens at request time (no `use cache`), so the
 * catalogue is always current and the build never depends on the API being up.
 */
export async function serverGet<T>(path: string): Promise<Loaded<T>> {
  try {
    const res = await fetch(`${API_URL}${path}`, { headers: { Accept: "application/json" }, cache: "no-store" });
    if (!res.ok) return { ok: false, status: res.status };
    return { ok: true, data: (await res.json()) as T };
  } catch {
    return { ok: false, status: 0 };
  }
}

/** Build a query string from defined, non-empty values. */
export function query(params: Record<string, string | number | null | undefined>): string {
  const sp = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v === null || v === undefined || v === "") continue;
    sp.set(k, String(v));
  }
  const s = sp.toString();
  return s ? `?${s}` : "";
}

/** First value of a search param. */
export function first(v: string | string[] | undefined): string | undefined {
  return Array.isArray(v) ? v[0] : v;
}
