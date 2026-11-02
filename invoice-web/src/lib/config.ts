/** Product name shown across the app. Rename here only. */
export const APP_NAME = "InvoiceLink";

export const API_URL = (process.env.NEXT_PUBLIC_API_URL || "http://localhost:5080").replace(/\/+$/, "");

export const SITE_URL = (process.env.NEXT_PUBLIC_SITE_URL || "http://localhost:3800").replace(/\/+$/, "");

/** Default accent / invoice brand colour. */
export const DEFAULT_ACCENT = "#2563eb";

/** Product key sent to the shared auth endpoints. */
export const PRODUCT = "invoices";

/** Prefix for everything this app keeps in localStorage. */
export const STORAGE_PREFIX = "invoicelink.";

export const COUNTRIES = [
  { code: "NG", name: "Nigeria" },
  { code: "GH", name: "Ghana" },
  { code: "KE", name: "Kenya" },
  { code: "ZA", name: "South Africa" },
  { code: "GB", name: "United Kingdom" },
  { code: "US", name: "United States" },
] as const;

export const CURRENCIES = ["NGN", "GHS", "KES", "ZAR", "GBP", "USD", "EUR"] as const;
