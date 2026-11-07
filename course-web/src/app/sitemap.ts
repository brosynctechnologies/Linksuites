import type { MetadataRoute } from "next";
import { coursePath } from "@/lib/format";
import { query, serverGet } from "@/lib/server-api";
import { absolute } from "@/lib/site";
import type { CategoryDto, CourseCardDto, Paged } from "@/lib/types";

/** The catalogue API returns at most 60 courses a page. */
const PAGE_SIZE = 60;
/** 50,000 URLs is the sitemap limit; past that, split with generateSitemaps. */
const MAX_PAGES = 500;

async function allCourses(): Promise<CourseCardDto[]> {
  const page = (n: number) => serverGet<Paged<CourseCardDto>>(`/api/catalog/courses${query({ sort: "newest", page: n, pageSize: PAGE_SIZE })}`);
  const first = await page(1);
  if (!first.ok) return [];
  const pages = Math.min(MAX_PAGES, Math.ceil(first.data.total / PAGE_SIZE));
  const rest = await Promise.all(Array.from({ length: Math.max(0, pages - 1) }, (_, i) => page(i + 2)));
  return [first, ...rest].flatMap((r) => (r.ok ? (r.data.items ?? []) : []));
}

/**
 * The marketplace: the landing pages, every category, every published course and every academy that has one. Read from
 * the public catalogue at request time, so the sitemap is always current and the build never needs the API.
 */
export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const [courses, cats] = await Promise.all([allCourses(), serverGet<CategoryDto[]>("/api/catalog/categories")]);
  const categories = cats.ok && Array.isArray(cats.data) ? cats.data : [];
  const academies = [...new Set(courses.map((c) => c.academySlug))];

  return [
    { url: absolute("/"), changeFrequency: "daily", priority: 1 },
    { url: absolute("/teach"), changeFrequency: "monthly", priority: 0.9 },
    { url: absolute("/courses"), changeFrequency: "daily", priority: 0.8 },
    { url: absolute("/teach/signup"), changeFrequency: "yearly", priority: 0.5 },
    { url: absolute("/signup"), changeFrequency: "yearly", priority: 0.4 },
    ...categories.map((c) => ({ url: absolute(`/courses${query({ category: c.slug })}`), changeFrequency: "weekly" as const, priority: 0.6 })),
    ...courses.map((c) => ({
      url: absolute(coursePath(c.academySlug, c.slug)),
      changeFrequency: "weekly" as const,
      priority: 0.7,
      ...(c.thumbnailUrl ? { images: [c.thumbnailUrl] } : {}),
    })),
    ...academies.map((slug) => ({ url: absolute(`/a/${encodeURIComponent(slug)}`), changeFrequency: "weekly" as const, priority: 0.6 })),
  ];
}
