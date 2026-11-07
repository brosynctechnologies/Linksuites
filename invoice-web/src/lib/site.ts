import { APP_NAME } from "./config";

/** InvoiceLink's words for metadata, the Open Graph image and structured data. */
export const DEFAULT_TITLE = "InvoiceLink: invoices, quotes and payment links for Nigerian businesses";
export const DEFAULT_DESCRIPTION =
  "Make invoices and quotes, send them on WhatsApp or by email, and get paid by card or transfer. For Nigerian freelancers and agencies. Free to start.";

/**
 * Next merges metadata shallowly: a page that sets `openGraph` or `twitter` replaces the root object. So every page that
 * sets its own spreads these and always sets title, description and url.
 */
export const baseOpenGraph = { type: "website" as const, siteName: APP_NAME, locale: "en_NG" };
export const baseTwitter = { card: "summary_large_image" as const };
