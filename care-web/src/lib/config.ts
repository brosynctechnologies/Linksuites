/** Product name shown across the app. Rename here only. */
export const APP_NAME = "CareLink";

export const API_URL = (process.env.NEXT_PUBLIC_API_URL || "http://localhost:5080").replace(/\/+$/, "");

export const SITE_URL = (process.env.NEXT_PUBLIC_SITE_URL || "http://localhost:4400").replace(/\/+$/, "");

/** Product key sent to the shared business auth endpoints. */
export const PRODUCT = "care";

/** Prefix for everything this app keeps in localStorage (the session lives at `carelink.auth`). */
export const STORAGE_PREFIX = "carelink.";

/** Import limits (the API enforces them too). */
export const IMPORT_MAX_ROWS = 2000;
export const IMPORT_MAX_BYTES = 5 * 1024 * 1024;

/**
 * Plans as the spec lists them (the public pricing page shows these before anyone has an account; there is no public plans route).
 * Prices per month, in kobo. Signed-in pages read the live plans from `GET /api/billing`.
 */
export const CARE_PLANS = [
  { code: "free", name: "Free", priceMinor: 0, staff: 3, beds: 0, onlinePayments: false, branding: true },
  { code: "clinic", name: "Clinic", priceMinor: 2_500_000, staff: 15, beds: 20, onlinePayments: true, branding: true },
  { code: "hospital", name: "Hospital", priceMinor: 7_500_000, staff: 60, beds: 150, onlinePayments: true, branding: false },
] as const;
