import Link from "next/link";
import { Suspense } from "react";
import { CourseRow, CourseRowSkeleton } from "@/components/course-card";
import { LoadProblem, SearchForm, SitePage } from "@/components/site-chrome";
import { buttonClass, chipClass } from "@/components/styles";
import { APP_NAME } from "@/lib/config";
import { query, serverGet } from "@/lib/server-api";
import type { CategoryDto, CourseCardDto, Paged } from "@/lib/types";

const ROWS = [
  { key: "popular", title: "Popular right now", params: { sort: "popular" }, more: "/courses?sort=popular" },
  { key: "new", title: "New this month", params: { sort: "newest" }, more: "/courses?sort=newest" },
  { key: "rated", title: "Top rated", params: { sort: "rating" }, more: "/courses?sort=rating" },
  { key: "free", title: "Free to start", params: { price: "free", sort: "popular" }, more: "/courses?price=free" },
] as const;

export default function MarketplaceHome() {
  return (
    <SitePage hideSearch>
      <section className="border-b border-line bg-surface">
        <div className="mx-auto max-w-6xl px-4 pt-8 pb-7 sm:px-6 sm:pt-14 sm:pb-10">
          <h1 className="max-w-[18ch] font-serif text-3xl font-semibold tracking-[-0.02em] text-ink sm:text-4xl">What do you want to learn?</h1>
          <p className="mt-3 max-w-xl text-base text-slate-700 sm:text-lg">
            WAEC and JAMB prep, professional certificates and digital skills, taught by tutors you already follow. Pay in naira and learn on your phone.
          </p>
          <SearchForm id="hero-search" big className="mt-6 max-w-2xl" />
          <Suspense fallback={<div className="mt-5 h-9" aria-hidden="true" />}>
            <CategoryChips />
          </Suspense>
        </div>
      </section>

      <div className="mx-auto max-w-6xl space-y-10 px-4 py-8 sm:px-6 sm:py-10">
        {ROWS.map((row, i) => (
          <section key={row.key} aria-labelledby={`row-${row.key}`}>
            <div className="mb-3 flex items-baseline justify-between gap-3">
              <h2 id={`row-${row.key}`} className="text-xl font-semibold tracking-[-0.01em] text-ink">
                {row.title}
              </h2>
              <Link href={row.more} className="shrink-0 text-sm font-semibold text-lagoon-text hover:underline">
                See all
              </Link>
            </div>
            <Suspense fallback={<CourseRowSkeleton />}>
              <Row params={row.params} quietOnError={i > 0} />
            </Suspense>
          </section>
        ))}
      </div>

      <section className="border-t border-line bg-surface" aria-labelledby="teach-band">
        <div className="mx-auto grid max-w-6xl gap-5 px-4 py-9 sm:px-6 md:grid-cols-[minmax(0,1fr)_auto] md:items-center">
          <div>
            <h2 id="teach-band" className="font-serif text-2xl font-semibold tracking-[-0.01em] text-ink">
              Teach on {APP_NAME}
            </h2>
            <p className="mt-2 max-w-2xl text-slate-700">
              Already teaching on YouTube, Instagram or Telegram? Open your own academy, sell to your followers in naira, and reach new learners
              through the marketplace. Free to start.
            </p>
          </div>
          <Link href="/teach" className={buttonClass("secondary", "justify-self-start px-5")}>
            How teaching works
          </Link>
        </div>
      </section>
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
        <Link key={c.id} href={`/courses${query({ category: c.slug })}`} className={chipClass(false, "shrink-0")}>
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
