import { cache } from "react";
import { API_URL } from "./config";
import type { PublicBookingPageDto } from "./types";

/** A public read on the server: the data, or the status that explains why there is none (0 = API unreachable). */
export type Loaded<T> = { ok: true; data: T } | { ok: false; status: number };

async function getPublic<T>(path: string): Promise<Loaded<T>> {
  try {
    const res = await fetch(`${API_URL}${path}`, { cache: "no-store", headers: { Accept: "application/json" } });
    if (!res.ok) return { ok: false, status: res.status };
    return { ok: true, data: (await res.json()) as T };
  } catch {
    return { ok: false, status: 0 };
  }
}

/** A business's public booking page. Shared by generateMetadata and the page within one request. */
export const getPublicBookingPage = cache((slug: string) =>
  getPublic<PublicBookingPageDto>(`/api/public/book/${encodeURIComponent(slug)}`),
);
