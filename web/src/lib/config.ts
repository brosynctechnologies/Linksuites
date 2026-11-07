/** Product name shown across the app. Rename here only. */
export const APP_NAME = "OrderLink";

export const API_URL = (process.env.NEXT_PUBLIC_API_URL || "http://localhost:5080").replace(/\/+$/, "");

export const SITE_URL = (process.env.NEXT_PUBLIC_SITE_URL || "http://localhost:3700").replace(/\/+$/, "");

/** The landing page's title and description, also used by the root metadata and the share image. */
export const SITE_TITLE = `${APP_NAME}: take WhatsApp orders from your own online store in Nigeria`;
export const SITE_DESCRIPTION =
  "An online store link for Nigerian food vendors, boutiques and small shops. Customers pick, choose delivery and send you the full order on WhatsApp. Free to start.";

/**
 * Next merges metadata shallowly: a page that sets `openGraph` or `twitter` replaces the root object. So every page that
 * sets its own spreads these and always sets title, description and url along with any image.
 */
export const baseOpenGraph = { type: "website" as const, siteName: APP_NAME, locale: "en_NG" };
export const baseTwitter = { card: "summary_large_image" as const };

/** Default accent (WhatsApp-ish green). */
export const DEFAULT_ACCENT = "#16a34a";

export const COUNTRIES = [
  { code: "NG", name: "Nigeria" },
  { code: "GH", name: "Ghana" },
  { code: "KE", name: "Kenya" },
  { code: "ZA", name: "South Africa" },
  { code: "GB", name: "United Kingdom" },
  { code: "US", name: "United States" },
] as const;

export const CURRENCIES = ["NGN", "GHS", "KES", "ZAR", "GBP", "USD"] as const;

/** Store URL as customers see it. */
export function storeLink(slug: string) {
  return `${SITE_URL}/${slug}`;
}
