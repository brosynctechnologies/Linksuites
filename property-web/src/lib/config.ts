/** Product name shown across the app. Rename here only. */
export const APP_NAME = "PropertyLink";

export const API_URL = (process.env.NEXT_PUBLIC_API_URL || "http://localhost:5080").replace(/\/+$/, "");

export const SITE_URL = (process.env.NEXT_PUBLIC_SITE_URL || "http://localhost:4100").replace(/\/+$/, "");

/** The platform admin console (moderation, verifications, payouts, support). */
export const ADMIN_URL = process.env.NEXT_PUBLIC_ADMIN_URL ?? "http://localhost:4300";

/** Product key sent to the shared business auth endpoints. */
export const PRODUCT = "property";

/** Prefix for everything this app keeps in localStorage (the session lives at `propertylink.auth`). */
export const STORAGE_PREFIX = "propertylink.";

/** OpenFreeMap vector style: no API key. */
export const MAP_STYLE = "https://tiles.openfreemap.org/styles/liberty";

/** Where maps open when there is nothing else to centre on (Lagos), as [lng, lat]. */
export const DEFAULT_CENTER: [number, number] = [3.38, 6.52];

/** Platform commission on short-let stays (host earnings = total − 10%). */
export const STAY_COMMISSION_PCT = 10;

/** Smallest payout an agency can request, in kobo (₦5,000). */
export const MIN_PAYOUT_MINOR = 500_000;

/** The city whose areas the home page shows as chips. */
export const HOME_CITY = { slug: "lagos", name: "Lagos" } as const;

export const PURPOSES = [
  { value: "Rent", label: "Rent", path: "/rent" },
  { value: "Sale", label: "Buy", path: "/buy" },
  { value: "ShortLet", label: "Short-let", path: "/shortlet" },
] as const;

export const SORTS = [
  { value: "recommended", label: "Recommended" },
  { value: "newest", label: "Newest" },
  { value: "price_asc", label: "Lowest price" },
  { value: "price_desc", label: "Highest price" },
] as const;

export const COST_KINDS = [
  { value: "AgencyFee", label: "Agency fee" },
  { value: "LegalFee", label: "Legal fee" },
  { value: "Caution", label: "Caution deposit" },
  { value: "ServiceCharge", label: "Service charge" },
  { value: "Other", label: "Other" },
] as const;

export const REPORT_REASONS = [
  { value: "Fake", label: "It looks fake" },
  { value: "AlreadyTaken", label: "It is already taken" },
  { value: "WrongPrice", label: "The price is wrong" },
  { value: "Duplicate", label: "It is a duplicate" },
  { value: "Scam", label: "Someone asked me to pay before inspecting" },
  { value: "Other", label: "Something else" },
] as const;

export const WEEKDAYS = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"] as const;
