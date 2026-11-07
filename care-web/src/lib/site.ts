import { APP_NAME } from "./config";

/** CareLink's words for search and sharing, used by the root metadata, the landing page and the share image. */
export const DEFAULT_TITLE = "CareLink: clinic software for Nigerian clinics and hospitals";
export const DEFAULT_DESCRIPTION =
  "Clinic software for private clinics and small hospitals in Nigeria: front desk, patient records, wards, bills and a patient portal. Free to start.";

/**
 * Next merges metadata shallowly: a page that sets `openGraph` or `twitter` replaces the root object. So every page that sets its own
 * spreads these and always sets title, description and url along with any image.
 */
export const baseOpenGraph = { type: "website" as const, siteName: APP_NAME, locale: "en_NG" };
export const baseTwitter = { card: "summary_large_image" as const };
