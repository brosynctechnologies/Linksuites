import { APP_NAME } from "./config";

/** PropertyLink's own words for metadata, the sitemap and structured data. */
export const DEFAULT_TITLE = `${APP_NAME}: flats and houses to rent or buy in Lagos, every cost shown`;
export const DEFAULT_DESCRIPTION =
  "Rent, buy or book a short-let in Lagos from checked agents. Every listing adds up the rent, agency fee, legal fee and caution, so you know what moving in costs.";

/**
 * Next merges metadata shallowly: a page that sets `openGraph` or `twitter` replaces the root object. So every page that
 * sets its own spreads these and always sets title, description and url along with any image.
 */
export const baseOpenGraph = { type: "website" as const, siteName: APP_NAME, locale: "en_NG" };
export const baseTwitter = { card: "summary_large_image" as const };

/** The root share image (src/app/opengraph-image.tsx). A page that sets its own `openGraph` loses it unless it names it. */
const DEFAULT_IMAGE = { url: "/opengraph-image", width: 1200, height: 630, alt: `${APP_NAME}: homes in Lagos with every cost shown` };

/**
 * Open Graph and Twitter fields for one page. `image`: a photo URL, nothing for the default share image, or `false`
 * when the page's own segment has an opengraph-image file (Next only adds it when `images` is left unset).
 */
export function shareMetadata({ title, description, url, image }: { title: string; description: string; url: string; image?: string | false | null }) {
  const images = image === false ? undefined : image ? [image] : [DEFAULT_IMAGE];
  return {
    openGraph: { ...baseOpenGraph, title, description, url, ...(images ? { images } : {}) },
    twitter: { ...baseTwitter, title, description, ...(images ? { images } : {}) },
  };
}
