/** Product name shown across the app. Rename here only. */
export const APP_NAME = "LearnLink";

export const API_URL = (process.env.NEXT_PUBLIC_API_URL || "http://localhost:5080").replace(/\/+$/, "");

export const SITE_URL = (process.env.NEXT_PUBLIC_SITE_URL || "http://localhost:4000").replace(/\/+$/, "");

/** The platform admin console (course reviews, payouts, categories, support). */
export const ADMIN_URL = process.env.NEXT_PUBLIC_ADMIN_URL ?? "http://localhost:4300";

/** Lagoon: the default accent, replaced by an academy's brandColor on its own pages. */
export const DEFAULT_BRAND = "#0B6E6E";

/** Product key sent to the shared tutor auth endpoints. */
export const PRODUCT = "courses";

/** Prefix for everything this app keeps in localStorage. */
export const STORAGE_PREFIX = "learnlink.";

/** Platform fee on marketplace sales (the platform found the learner). */
export const MARKETPLACE_FEE_PCT = 30;

/** Days before a sale's earnings become available (the refund window). */
export const REFUND_WINDOW_DAYS = 7;

/** Smallest payout a tutor can request, in minor units (₦5,000). */
export const MIN_PAYOUT_MINOR = 500_000;

export const COUNTRIES = [
  { code: "NG", name: "Nigeria" },
  { code: "GH", name: "Ghana" },
  { code: "KE", name: "Kenya" },
  { code: "ZA", name: "South Africa" },
  { code: "GB", name: "United Kingdom" },
  { code: "US", name: "United States" },
] as const;

export const LEVELS = [
  { value: "Beginner", label: "Beginner" },
  { value: "Intermediate", label: "Intermediate" },
  { value: "Advanced", label: "Advanced" },
  { value: "AllLevels", label: "All levels" },
] as const;

export const LANGUAGES = ["English", "Yoruba", "Igbo", "Hausa", "Pidgin", "French", "Swahili", "Twi"] as const;

export const SORTS = [
  { value: "popular", label: "Most popular" },
  { value: "newest", label: "Newest" },
  { value: "rating", label: "Top rated" },
  { value: "price", label: "Lowest price" },
] as const;

/**
 * Fee on academy sales on the free plan (paid plans take none). Public pages never show plan prices: the plans and their
 * naira prices are read live from /api/billing inside the studio.
 */
export const FREE_PLAN_ACADEMY_FEE_PCT = 10;
