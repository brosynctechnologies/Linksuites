import { APP_NAME, SITE_URL } from "./config";

/** BookLink's own words for metadata, the share image and structured data. */
export const SITE_TITLE = "BookLink: online booking and deposits for Nigerian salons and barbers";
export const SITE_DESCRIPTION =
  "A booking page for salons, barbers, braiders and make-up artists in Nigeria. Customers pick a time and pay a deposit by card or transfer. Free to start.";

export { APP_NAME, SITE_URL };

/**
 * Next merges metadata shallowly: a page that sets `openGraph` or `twitter` replaces the root object. So every page that
 * sets its own spreads these and always sets title, description and url along with any image.
 */
export const baseOpenGraph = { type: "website" as const, siteName: APP_NAME, locale: "en_NG" };
export const baseTwitter = { card: "summary_large_image" as const };
