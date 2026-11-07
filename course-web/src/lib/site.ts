import type { Metadata } from "next";
import { APP_NAME, SITE_URL } from "./config";

/** The learners' marketplace: the root of the site. */
export const SITE_TITLE = `${APP_NAME}: online courses from tutors you trust, paid in naira`;
export const SITE_DESCRIPTION =
  "WAEC and JAMB prep, professional exams and digital skills, taught by tutors you already follow. Pay in naira and learn on your phone, at your own pace.";

/** The tutors' landing at /teach: LearnLink's business page, where the LinkSuite hub sends tutors. */
export const TEACH_TITLE = "Sell online courses in naira: LearnLink for tutors and academies";
export const TEACH_DESCRIPTION =
  "Turn your classes into online courses with your own academy page, video lessons, quizzes and the LearnLink marketplace. Free to start, for tutors in Nigeria.";

/**
 * Next merges metadata shallowly: a page that sets `openGraph` replaces the root object, and the root's share image with
 * it. So every page that sets its own Open Graph builds it with `pageOpenGraph`, which keeps the base fields and the image.
 */
export const baseOpenGraph = { type: "website" as const, siteName: APP_NAME, locale: "en_NG" };
export const baseTwitter = { card: "summary_large_image" as const };

const DEFAULT_IMAGE = { url: "/opengraph-image", width: 1200, height: 630, alt: `${APP_NAME}: online courses from tutors you trust` };

export function pageOpenGraph({
  title,
  description,
  url,
  images,
}: {
  title: string;
  description: string;
  url: string;
  images?: string[];
}): NonNullable<Metadata["openGraph"]> {
  return { ...baseOpenGraph, title, description, url, images: images?.length ? images : [DEFAULT_IMAGE] };
}

/** Absolute URL on this site, for structured data and the sitemap. */
export const absolute = (path: string) => `${SITE_URL}${path}`;
