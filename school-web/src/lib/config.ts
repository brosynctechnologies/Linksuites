/** Product name shown across the app. Rename here only. */
export const APP_NAME = "SchoolLink";

export const API_URL = (process.env.NEXT_PUBLIC_API_URL || "http://localhost:5080").replace(/\/+$/, "");

export const SITE_URL = (process.env.NEXT_PUBLIC_SITE_URL || "http://localhost:4200").replace(/\/+$/, "");

/** Product key sent to the shared business auth endpoints. */
export const PRODUCT = "school";

/** Prefix for everything this app keeps in localStorage (the session lives at `schoollink.auth`). */
export const STORAGE_PREFIX = "schoollink.";

/** Import limits (the API enforces them too). */
export const IMPORT_MAX_ROWS = 2000;
export const IMPORT_MAX_BYTES = 5 * 1024 * 1024;

/**
 * Plans as the spec lists them (the public pricing page shows these before anyone has an account). Prices per term, in kobo.
 */
export const SCHOOL_PLANS = [
  { code: "free", name: "Free", priceMinor: 0, students: 50, staff: 3, branding: true, onlineFees: false },
  { code: "standard", name: "Standard", priceMinor: 6_000_000, students: 300, staff: 15, branding: true, onlineFees: true },
  { code: "premium", name: "Premium", priceMinor: 15_000_000, students: 1000, staff: 50, branding: false, onlineFees: true },
] as const;

/** The most students the list endpoint returns in one call (the API's cap, above every plan's student limit). */
export const STUDENT_LIST_TAKE = 2000;
