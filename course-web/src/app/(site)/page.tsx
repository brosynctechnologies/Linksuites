import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";
import { CourseRow, CourseRowSkeleton } from "@/components/course-card";
import { LoadProblem, SearchForm } from "@/components/site-chrome";
import { SitePage } from "@/components/site-page";
import { chipClass } from "@/components/styles";
import { FinalCta, JsonLd } from "@/components/suite/sections";
import { lsLink } from "@/components/suite/ui";
import { APP_NAME, SITE_URL } from "@/lib/config";
import { query, serverGet } from "@/lib/server-api";
import { SITE_DESCRIPTION, SITE_TITLE, pageOpenGraph } from "@/lib/site";
import { hubUrl } from "@/lib/suite";
import type { CategoryDto, CourseCardDto, Paged } from "@/lib/types";

export const metadata: Metadata = {
  title: { absolute: SITE_TITLE },
  description: SITE_DESCRIPTION,
  alternates: { canonical: "/" },
  openGraph: pageOpenGraph({ title: SITE_TITLE, description: SITE_DESCRIPTION, url: "/" }),
};

const ROWS = [
  { key: "popular", title: "Popular right now", params: { sort: "popular" }, more: "/courses?sort=popular" },
  { key: "new", title: "New this month", params: { sort: "newest" }, more: "/courses?sort=newest" },
  { key: "rated", title: "Top rated", params: { sort: "rating" }, more: "/courses?sort=rating" },
  { key: "free", title: "Free to start", params: { price: "free", sort: "popular" }, more: "/courses?price=free" },
] as const;

/** The marketplace as a site search: Google can offer the course search straight from its results. */
const websiteJsonLd = {
  "@context": "https://schema.org",
  "@type": "WebSite",
  name: APP_NAME,
  url: SITE_URL,
  inLanguage: "en-NG",
  publisher: { "@type": "Organization", name: "LinkSuite", url: hubUrl },
  potentialAction: {
    "@type": "SearchAction",
    target: { "@type": "EntryPoint", urlTemplate: `${SITE_URL}/courses?q={search_term_string}` },
    "query-input": "required name=search_term_string",
  },
};

export default function MarketplaceHome() {
  return (
    <SitePage hideSearch>
      <JsonLd data={websiteJsonLd} />
      <section aria-labelledby="hero-title" className="ls-container pt-10 pb-12 sm:pt-14 md:pb-16 lg:pt-20">
        <h1 id="hero-title" className="ls-h1 max-w-[14ch] text-ls-ink">
          What do you want to learn?
        </h1>
        <p className="ls-lead mt-5 max-w-[36rem] text-ls-ink-2">
          WAEC and JAMB prep, professional exams and digital skills, taught by tutors you already follow. Pay in naira and learn on your phone.
        </p>
        <SearchForm id="hero-search" big className="mt-7 max-w-2xl" />
        <Suspense fallback={<div className="mt-5 h-9" aria-hidden="true" />}>
          <CategoryChips />
        </Suspense>
      </section>

      {ROWS.map((row, i) => (
        <section key={row.key} aria-labelledby={`row-${row.key}`} className="border-t border-ls-line">
          <div className="ls-container py-10 sm:py-12">
            <div className="mb-5 flex items-baseline justify-between gap-3">
              <h2 id={`row-${row.key}`} className="ls-h3 text-ls-ink">
                {row.title}
              </h2>
              <Link href={row.more} className={`${lsLink} inline-flex min-h-11 shrink-0 items-center`}>
                See all<span className="sr-only">: {row.title.toLowerCase()}</span>
              </Link>
            </div>
            <Suspense fallback={<CourseRowSkeleton />}>
              <Row params={row.params} quietOnError={i > 0} />
            </Suspense>
          </div>
        </section>
      ))}

      <FinalCta
        title="Already teaching? Sell your courses here."
        text="Open your own academy, sell to your followers in naira and reach new learners on the marketplace. Free to start."
        action={{ href: "/teach", label: "Teach on LearnLink" }}
      />
    </SitePage>
  );
}

async function CategoryChips() {
  const res = await serverGet<CategoryDto[]>("/api/catalog/categories");
  if (!res.ok || !Array.isArray(res.data)) return null;
  const top = res.data.filter((c) => !c.parentId).slice(0, 12);
  if (!top.length) return null;
  return (
    <nav aria-label="Categories" className="no-scrollbar -mx-4 mt-5 flex gap-2 overflow-x-auto px-4 sm:mx-0 sm:flex-wrap sm:px-0">
      {top.map((c) => (
        <Link key={c.id} href={`/courses${query({ category: c.slug })}`} className={chipClass(false, "min-h-11 shrink-0")}>
          {c.name}
        </Link>
      ))}
    </nav>
  );
}

/** One row of course cards. Only the first row explains a failed load, so an outage reads as one message. */
async function Row({ params, quietOnError }: { params: Record<string, string>; quietOnError?: boolean }) {
  const res = await serverGet<Paged<CourseCardDto>>(`/api/catalog/courses${query({ ...params, pageSize: 8 })}`);
  if (!res.ok) return quietOnError ? <p className="text-sm text-muted">Not available right now.</p> : <LoadProblem status={res.status} />;
  const items = res.data.items ?? [];
  if (!items.length) {
    return <p className="rounded-panel border border-dashed border-slate-300 px-4 py-6 text-sm text-muted">No courses here yet. New courses appear as tutors publish them.</p>;
  }
  return <CourseRow courses={items} />;
}
