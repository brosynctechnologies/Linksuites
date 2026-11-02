/** Product name shown across the app. Rename here only. */
export const APP_NAME = "BookLink";

export const API_URL = (process.env.NEXT_PUBLIC_API_URL || "http://localhost:5080").replace(/\/+$/, "");

export const SITE_URL = (process.env.NEXT_PUBLIC_SITE_URL || "http://localhost:3900").replace(/\/+$/, "");

/** BookLink rose: the default brand colour for a business that hasn't picked one. */
export const DEFAULT_BRAND = "#C2255C";

/** Product key sent to the shared auth endpoints. */
export const PRODUCT = "bookings";

/** Prefix for everything this app keeps in localStorage. */
export const STORAGE_PREFIX = "booklink.";

export const COUNTRIES = [
  { code: "NG", name: "Nigeria" },
  { code: "GH", name: "Ghana" },
  { code: "KE", name: "Kenya" },
  { code: "ZA", name: "South Africa" },
  { code: "GB", name: "United Kingdom" },
  { code: "US", name: "United States" },
] as const;

/** Common IANA zones for the settings time zone select. */
export const TIMEZONES = [
  "Africa/Lagos",
  "Africa/Accra",
  "Africa/Nairobi",
  "Africa/Johannesburg",
  "Africa/Cairo",
  "Africa/Casablanca",
  "Africa/Kigali",
  "Africa/Kampala",
  "Africa/Dar_es_Salaam",
  "Africa/Addis_Ababa",
  "Africa/Abidjan",
  "Africa/Dakar",
  "Europe/London",
  "Europe/Dublin",
  "Europe/Paris",
  "Europe/Berlin",
  "America/New_York",
  "America/Chicago",
  "America/Denver",
  "America/Los_Angeles",
  "America/Toronto",
  "Asia/Dubai",
  "Asia/Kolkata",
  "Asia/Singapore",
  "Australia/Sydney",
  "UTC",
] as const;

export const CATEGORIES = [
  { value: "Salon", label: "Hair salon" },
  { value: "Barber", label: "Barber" },
  { value: "Spa", label: "Spa and beauty" },
  { value: "MakeUp", label: "Make-up artist" },
  { value: "Photography", label: "Photography studio" },
  { value: "Clinic", label: "Clinic" },
  { value: "Fitness", label: "Fitness" },
  { value: "Other", label: "Something else" },
] as const;

/**
 * Staff colours: eight muted tints derived from the BookLink palette, so the calendar reads by person
 * without turning into a rainbow. Each is dark enough to carry as a left rule and a tinted block.
 */
export const STAFF_COLORS = ["#7A4E9C", "#C2255C", "#2F8F6E", "#A86A12", "#3C6E9E", "#9C4E6E", "#5E7A3A", "#8A5A44"] as const;
