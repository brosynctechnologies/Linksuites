import { API_URL } from "./config";
import type { ListingEventKind } from "./types";

/**
 * Record a listing event (View on page load, WhatsApp/Call on button taps). Fire-and-forget with `keepalive`
 * so the request survives the browser leaving for wa.me or the dialler.
 */
export function trackEvent(listingId: string, kind: ListingEventKind) {
  try {
    void fetch(`${API_URL}/api/listings/${encodeURIComponent(listingId)}/events`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ kind }),
      keepalive: true,
    }).catch(() => {});
  } catch {
    /* tracking never blocks the visitor */
  }
}
