import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";
import { CourseGrid, CourseRowSkeleton } from "@/components/course-card";
import { LoadProblem, SiteFooter, SiteHeader } from "@/components/site-chrome";
import { buttonClass, cx } from "@/components/styles";
import { formatCount } from "@/lib/format";
import { first, query, serverGet } from "@/lib/server-api";
import type { CategoryDto, CourseCardDto, Paged } from "@/lib/types";
import { CatalogFilters, type CatalogParams } from "./catalog-filters";

export const metadata: Metadata = {
  title: "Browse courses",
  description: "Every course on the LearnLink marketplace: exam prep, professional certificates and digital skills.",
};

const PAGE_SIZE = 24;

export default function CataloguePage({ searchParams }: PageProps<"/courses">) {
  return (
    <div className="flex min-h-screen flex-col">
      <Suspense fallback={<SiteHeader />}>
        {searchParams.then((sp) => (
          <SiteHeader query={first(sp.q)} />
        ))}
      </Suspense>
      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-6 sm:px-6 sm:py-8">
        <Suspense
          fallback={
            <>
              <h1 className="text-2xl font-semibold tracking-[-0.01em] text-ink">Browse courses</h1>
              <div className="mt-6">
                <CourseRowSkeleton count={8} />
              </div>
            </>
          }
        >
          {searchParams.then((sp) => (
            <Catalogue
              params={{
                q: first(sp.q),
                category: first(sp.category),
                level: first(sp.level),
                price: first(sp.price),
                sort: first(sp.sort),
              }}
              page={Math.max(1, Number(first(sp.page)) || 1)}
            />
          ))}
        </Suspense>
      </main>
      <SiteFooter />
    </div>
  );
}

async function Catalogue({ params, page }: { params: CatalogParams; page: number }) {
  const [cats, res] = await Promise.all([
    serverGet<CategoryDto[]>("/api/catalog/categories"),
    serverGet<Paged<CourseCardDto>>(`/api/catalog/courses${query({ ...params, page, pageSize: PAGE_SIZE })}`),
  ]);
  const categories = cats.ok && Array.isArray(cats.data) ? cats.data : [];
  const categoryName = categories.find((c) => c.slug === params.category)?.name;
  const title = params.q ? `Results for “${params.q}”` : categoryName ? categoryName : "Browse courses";
  const filtered = !!(params.q || params.category || params.level || params.price);

  return (
    <>
      <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
        <h1 className="text-2xl font-semibold tracking-[-0.01em] text-ink">{title}</h1>
        {res.ok && (
          <p className="text-sm text-muted">
            {formatCount(res.data.total)} course{res.data.total === 1 ? "" : "s"}
          </p>
        )}
      </div>
      <div className="mt-4 border-b border-line pb-5">
        <CatalogFilters params={params} categories={categories} />
      </div>
      <div className="mt-6">
        {!res.ok ? (
          <LoadProblem status={res.status} />
        ) : res.data.items.length === 0 ? (
          <div className="rounded-panel border border-dashed border-slate-300 px-6 py-12 text-center">
            <p className="font-semibold text-ink">No courses match{params.q ? ` “${params.q}”` : " these filters"}.</p>
            <p className="mt-1 text-sm text-muted">Try fewer words or a different category.</p>
            {filtered && (
              <Link href="/courses" className={buttonClass("secondary", "mt-5")}>
                Clear filters
              </Link>
            )}
          </div>
        ) : (
          <>
            <CourseGrid courses={res.data.items} />
            <PageLinks params={params} page={page} total={res.data.total} pageSize={res.data.pageSize || PAGE_SIZE} />
          </>
        )}
      </div>
    </>
  );
}

function PageLinks({ params, page, total, pageSize }: { params: CatalogParams; page: number; total: number; pageSize: number }) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  if (pages <= 1) return null;
  const link = (p: number) => `/courses${query({ ...params, page: p > 1 ? p : undefined })}`;
  const cls = (disabled: boolean) => cx(buttonClass("secondary"), disabled && "pointer-events-none opacity-45");
  return (
    <nav className="mt-8 flex items-center justify-between gap-3 text-sm" aria-label="Pages">
      <Link href={link(page - 1)} className={cls(page <= 1)} aria-disabled={page <= 1} tabIndex={page <= 1 ? -1 : undefined}>
        Previous
      </Link>
      <span className="text-muted">
        Page {page} of {pages}
      </span>
      <Link href={link(page + 1)} className={cls(page >= pages)} aria-disabled={page >= pages} tabIndex={page >= pages ? -1 : undefined}>
        Next
      </Link>
    </nav>
  );
}
