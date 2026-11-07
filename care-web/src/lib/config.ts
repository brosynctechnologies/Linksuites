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
