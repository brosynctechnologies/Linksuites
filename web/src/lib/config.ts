/** Product name shown across the app. Rename here only. */
export const APP_NAME = "OrderLink";

export const API_URL = (process.env.NEXT_PUBLIC_API_URL || "http://localhost:5080").replace(/\/+$/, "");

export const SITE_URL = (process.env.NEXT_PUBLIC_SITE_URL || "http://localhost:3700").replace(/\/+$/, "");

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
