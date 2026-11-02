import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { formatCount } from "@/lib/format";
import { purposeLabel, readSearch, searchApiPath, searchHref, type SearchState } from "@/lib/search";
import { serverGet } from "@/lib/server-api";
import type { ListingPurpose, RefItem, SearchResultDto } from "@/lib/types";
import { LoadProblem, SiteFooter, SiteHeader } from "../site-chrome";
import { buttonClass } from "../styles";
import { SearchFilters } from "./filters";
import { SearchView } from "./search-view";

const PAGE_SIZE = 20;

type SP = Record<string, string | string[] | undefined>;

function titleFor(purpose: ListingPurpose, place: string | undefined) {
  const what = purpose === "Sale" ? "Homes for sale" : purpose === "ShortLet" ? "Short-lets" : "Homes for rent";
  return place ? `${what} in ${place}` : what;
}

/** Metadata for /rent/[location] etc.: the place name from the slug (the API names it on the page itself). */
export async function searchMetadata(purpose: ListingPurpose, params: Promise<{ location?: string }>): Promise<Metadata> {
  const { location } = await params;
  const place = location ? decodeURIComponent(location).replace(/-/g, " ").replace(/\b\w/g, (c) => c.toUpperCase()) : undefined;
  return {
    title: titleFor(purpose, place),
    description: `${titleFor(purpose, place)} from checked agents on PropertyLink, with the full move-in cost on every listing.`,
  };
}

/** The shared search page behind /rent, /buy, /shortlet and their /[location] variants. */
export function SearchPage({ purpose, params, searchParams }: { purpose: ListingPurpose; params: Promise<{ location?: string }>; searchParams: Promise<SP> }) {
  return (
    <div className="flex min-h-screen flex-col">
      <SiteHeader />
      <main className="flex-1">
        <Suspense fallback={<SearchSkeleton purpose={purpose} />}>
          {Promise.all([params, searchParams]).then(([p, sp]) => (
            <Results state={readSearch(purpose, p.location, sp)} />
          ))}
        </Suspense>
      </main>
      <SiteFooter />
    </div>
  );
}

function SearchSkeleton({ purpose }: { purpose: ListingPurpose }) {
  return (
    <div className="px-4 py-6 sm:px-6" aria-hidden="true">
      <p className="display-wide text-2xl font-bold text-asphalt">{titleFor(purpose, undefined)}</p>
      <div className="mt-6 space-y-3">
        {[0, 1, 2].map((i) => (
          <div key={i} className="h-40 animate-pulse rounded-panel bg-plaster-2" />
        ))}
      </div>
    </div>
  );
}

async function Results({ state }: { state: SearchState }) {
  const [res, types, amenities] = await Promise.all([
    serverGet<SearchResultDto>(searchApiPath(state, PAGE_SIZE)),
    serverGet<RefItem[]>("/api/property-types"),
    serverGet<RefItem[]>("/api/amenities"),
  ]);
  if (!res.ok && res.status === 404 && state.location) notFound();

  const location = res.ok ? res.data.location : null;
  const place = location?.name ?? (state.bbox ? "this map area" : undefined);
  const title = titleFor(state.purpose, place);
  const typeList = types.ok && Array.isArray(types.data) ? types.data : [];
  const amenityList = amenities.ok && Array.isArray(amenities.data) ? amenities.data : [];
  const center: [number, number] | null = location?.lat != null && location?.lng != null ? [location.lng, location.lat] : null;

  return (
    <>
      <div className="border-b border-line bg-plaster px-4 pt-5 pb-4 sm:px-6">
        <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
          <h1 className="display-wide text-2xl font-bold text-asphalt sm:text-[1.75rem]">{title}</h1>
          {res.ok && (
            <p className="text-sm text-muted">
              {formatCount(res.data.items.total)} home{res.data.items.total === 1 ? "" : "s"} {purposeLabel(state.purpose)}
            </p>
          )}
        </div>
        <div className="mt-4">
          <SearchFilters key={searchHref(state)} state={state} locationName={location?.name} types={typeList} amenities={amenityList} />
        </div>
      </div>
      <div className="pt-4">
        {!res.ok ? (
          <div className="px-4 pb-10 sm:px-6">
            {res.status === 400 ? (
              <div className="rounded-panel border border-line bg-surface px-5 py-8 text-center">
                <p className="font-semibold text-asphalt">These filters don&apos;t work together.</p>
                <p className="mt-1 text-sm text-muted">Check that the lowest price is below the highest, then try again.</p>
                <Link href={searchHref({ purpose: state.purpose, location: state.location })} className={buttonClass("secondary", "mt-4")}>
                  Clear filters
                </Link>
              </div>
            ) : (
              <LoadProblem status={res.status} what="homes" />
            )}
          </div>
        ) : res.data.items.items.length === 0 && res.data.items.total > 0 ? (
          // The API doesn't clamp the page: past the last one there are results, just not on this page.
          <div className="px-4 pb-10 sm:px-6">
            <div className="rounded-panel border border-line bg-surface px-5 py-8 text-center">
              <p className="font-semibold text-asphalt">This page is past the end of the results.</p>
              <p className="mt-1 text-sm text-muted">
                {formatCount(res.data.items.total)} home{res.data.items.total === 1 ? "" : "s"} match, on{" "}
                {Math.max(1, Math.ceil(res.data.items.total / (res.data.items.pageSize || PAGE_SIZE)))} page
                {Math.ceil(res.data.items.total / (res.data.items.pageSize || PAGE_SIZE)) === 1 ? "" : "s"}.
              </p>
              <Link href={searchHref({ ...state, page: 1 })} className={buttonClass("secondary", "mt-4")}>
                Go to page 1
              </Link>
            </div>
          </div>
        ) : (
          <SearchView
            state={state}
            items={res.data.items.items}
            pins={res.data.pins}
            total={res.data.items.total}
            page={res.data.items.page || state.page || 1}
            pageSize={res.data.items.pageSize || PAGE_SIZE}
            center={center}
          />
        )}
      </div>
    </>
  );
}
