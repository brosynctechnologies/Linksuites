/** The product keys used by the API, with their app names. */
export const PRODUCTS = [
  { key: "whatsapp-orders", name: "OrderLink" },
  { key: "invoices", name: "InvoiceLink" },
  { key: "bookings", name: "BookLink" },
  { key: "courses", name: "LearnLink" },
  { key: "property", name: "PropertyLink" },
  { key: "school", name: "SchoolLink" },
  { key: "care", name: "CareLink" },
] as const;

export function productName(key: string | null | undefined): string {
  if (!key) return "—";
  return PRODUCTS.find((p) => p.key === key)?.name ?? key;
}

/** Known plan limit keys and labels (docs/*_API.md). Plans may carry other keys; those show their key. */
export const LIMIT_LABELS: Record<string, string> = {
  // shared
  team_seats: "Team seats",
  remove_branding: "Remove branding (0/1)",
  // invoices
  expenses: "Expenses (0/1)",
  reports: "Reports (0/1)",
  // bookings
  staff_members: "Staff members",
  bookings_per_month: "Bookings a month",
  deposits: "Deposits (0/1)",
  reminders: "Reminders (0/1)",
  // courses
  courses: "Courses",
  video_storage_mb: "Video storage (MB)",
  academy_fee_pct: "Fee on academy sales (%)",
  coupons: "Coupons (0/1)",
  certificates: "Certificates (0/1)",
  // property
  listings: "Listings (live, in review or hidden)",
  listing_expiry_days: "Listing stays up (days)",
  listing_stats: "Views and lead stats (0/1)",
  // school
  students: "Active students",
  online_fees: "Online fee payments (0/1)",
  // care
  beds: "Active beds",
  online_payments: "Online patient payments (0/1)",
};

/** Limit keys documented per product, so a new plan starts with the right inputs. */
export const PRODUCT_LIMIT_KEYS: Record<string, string[]> = {
  bookings: ["staff_members", "bookings_per_month", "deposits", "reminders", "remove_branding", "team_seats"],
  courses: ["courses", "video_storage_mb", "academy_fee_pct", "coupons", "certificates", "remove_branding", "team_seats"],
  property: ["listings", "listing_expiry_days", "listing_stats", "team_seats"],
  invoices: ["expenses", "reports", "team_seats"],
  school: ["students", "team_seats", "remove_branding", "online_fees"],
  care: ["team_seats", "beds", "online_payments", "remove_branding"],
  "whatsapp-orders": [],
};

export function limitLabel(key: string): string {
  return LIMIT_LABELS[key] ?? key.replace(/_/g, " ").replace(/^./, (c) => c.toUpperCase());
}
