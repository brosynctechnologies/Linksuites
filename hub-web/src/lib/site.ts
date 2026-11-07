import { appJsonLd, products } from "./products";

/** The hub's own address and words, used by metadata, the sitemap, robots and structured data. */
export const SITE_URL = (process.env.NEXT_PUBLIC_SITE_URL || "http://localhost:3600").replace(/\/+$/, "");
export const SITE_NAME = "LinkSuite";
export const SITE_TITLE = "LinkSuite: business apps for Nigerian small businesses";
export const SITE_DESCRIPTION =
  "Seven apps for Nigerian small businesses: orders on WhatsApp, invoices, bookings, courses, property listings, school and clinic management. One sign-in, free to start.";

/** An optional public contact address, shown in the footer when set. */
export const CONTACT_EMAIL = process.env.NEXT_PUBLIC_CONTACT_EMAIL || "";

/** Organization, WebSite and the seven apps as an ItemList of SoftwareApplication. */
export function hubJsonLd() {
  return {
    "@context": "https://schema.org",
    "@graph": [
      { "@type": "Organization", "@id": `${SITE_URL}/#org`, name: SITE_NAME, url: SITE_URL, logo: `${SITE_URL}/icon.svg` },
      { "@type": "WebSite", "@id": `${SITE_URL}/#site`, name: SITE_NAME, url: SITE_URL, inLanguage: "en-NG", publisher: { "@id": `${SITE_URL}/#org` } },
      {
        "@type": "ItemList",
        name: "LinkSuite apps",
        itemListElement: products.map((p, i) => ({ "@type": "ListItem", position: i + 1, item: appJsonLd(p) })),
      },
    ],
  };
}

/**
 * Next merges metadata shallowly: a page that sets `openGraph` replaces the root object. So every page that sets its own
 * Open Graph or Twitter fields spreads these and always sets title, description and url along with any image.
 */
export const baseOpenGraph = { type: "website" as const, siteName: SITE_NAME, locale: "en_NG" };
export const baseTwitter = { card: "summary_large_image" as const };
