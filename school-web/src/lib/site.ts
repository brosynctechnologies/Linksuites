import { APP_NAME, SITE_URL } from "./config";

/** SchoolLink's public words, used by metadata, the share image, the sitemap and robots. */
export const DEFAULT_TITLE = `${APP_NAME}: school management software for Nigerian schools`;
export const DEFAULT_DESCRIPTION =
  "Fees, results, attendance and a parent portal for nursery, primary and secondary schools in Nigeria. Import students from Excel and start free.";

export { APP_NAME, SITE_URL };

/**
 * Next merges metadata shallowly: a page that sets `openGraph` replaces the root object. So every page that sets its own
 * Open Graph or Twitter fields spreads these and always sets title, description and url along with any image.
 */
export const baseOpenGraph = { type: "website" as const, siteName: APP_NAME, locale: "en_NG" };
export const baseTwitter = { card: "summary_large_image" as const };
