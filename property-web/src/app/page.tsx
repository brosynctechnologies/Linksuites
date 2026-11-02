import Link from "next/link";
import { connection } from "next/server";
import { Suspense } from "react";
import { HOME_CITY } from "@/lib/config";
import { serverGet } from "@/lib/server-api";
import type { ListingCardDto, LocationDto, SearchResultDto } from "@/lib/types";
import { ListingGrid } from "@/components/listing-card";
import { LoadProblem, SitePage } from "@/components/site-chrome";
import { chipClass, linkClass } from "@/components/styles";
import { HomeSearch } from "./home-search";

export default function HomePage() {
  return (
    <SitePage>
      <section className="bg-plaster">
        <div className="mx-auto max-w-6xl px-4 pt-10 pb-10 sm:px-6 sm:pt-16 sm:pb-14">
          <h1 className="display-wide max-w-3xl text-3xl font-bold text-asphalt sm:text-4xl">Real homes from checked agents. Every cost shown.</h1>
          <p className="mt-3 max-w-2xl text-lg text-slate-700">
            Rent, buy or book a short-let in Lagos and beyond. Each listing adds up the rent, agency fee, legal fee and caution so you know what moving in
            really costs.
          </p>
          <div className="mt-7">
            <HomeSearch />
          </div>
        </div>
      </section>

      <Suspense fallback={<FeaturedSkeleton />}>
        <Featured />
      </Suspense>

      <Suspense fallback={null}>
        <Areas />
      </Suspense>

      <section aria-labelledby="real-h" className="mx-auto max-w-6xl px-4 py-12 sm:px-6">
        <h2 id="real-h" className="display-wide text-xl font-bold text-asphalt">
          How we keep listings real
        </h2>
        <div className="mt-4 max-w-3xl space-y-3 text-slate-700">
          <p>Agents and landlords show us their ID and business documents before they get the green Verified seal.</p>
          <p>Every photo is checked against photos already on the site, so the same flat can&apos;t be posted by someone who doesn&apos;t have it.</p>
          <p>Listings expire unless the agent confirms they&apos;re still available, so what you see is on the market today.</p>
        </div>
        <Link href="/how-verification-works" className={`${linkClass} mt-4 inline-flex min-h-11 items-center`}>
          How verification works
        </Link>
      </section>
    </SitePage>
  );
}

function FeaturedSkeleton() {
  return (
    <div className="mx-auto max-w-6xl px-4 py-10 sm:px-6" aria-hidden="true">
      <div className="h-7 w-56 animate-pulse rounded bg-plaster-2" />
      <div className="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {[0, 1, 2, 3].map((i) => (
          <div key={i} className="aspect-[4/5] animate-pulse rounded-panel bg-plaster-2" />
        ))}
      </div>
    </div>
  );
}

/**
 * Featured listings come first: every listing with a running Featured boost, from any city and of any purpose (the API
 * shuffles them on each request so they take turns when there are many). The recommendations for the home city follow,
 * leaving out anything already shown above.
 */
async function Featured() {
  await connection();
  const [featuredRes, res] = await Promise.all([
    serverGet<ListingCardDto[]>("/api/listings/featured"),
    serverGet<SearchResultDto>(`/api/listings?location=${HOME_CITY.slug}&sort=recommended&page=1&pageSize=24`),
  ]);
  const featured = featuredRes.ok && Array.isArray(featuredRes.data) ? featuredRes.data : [];
  if (!res.ok && featured.length === 0) {
    return (
      <div className="mx-auto max-w-6xl px-4 py-10 sm:px-6">
        <LoadProblem status={res.status} what="listings" />
      </div>
    );
  }
  const shown = new Set(featured.map((l) => l.id));
  const recommended = (res.ok ? (res.data.items.items ?? []) : []).filter((l) => !shown.has(l.id)).slice(0, 8);
  if (featured.length === 0 && recommended.length === 0) return null;
  return (
    <>
      {featured.length > 0 && (
        <section aria-labelledby="featured-h" className="mx-auto max-w-6xl px-4 pt-10 sm:px-6">
          <h2 id="featured-h" className="display-wide text-xl font-bold text-asphalt">
            Featured listings
          </h2>
          <ListingGrid listings={featured} className="mt-4" />
        </section>
      )}
      {recommended.length > 0 && (
        <section aria-labelledby="recommended-h" className="mx-auto max-w-6xl px-4 pt-10 sm:px-6">
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <h2 id="recommended-h" className="display-wide text-xl font-bold text-asphalt">
              Recommended in {HOME_CITY.name}
            </h2>
            <Link href={`/rent/${HOME_CITY.slug}`} className={`${linkClass} inline-flex min-h-11 items-center`}>
              Homes to rent in {HOME_CITY.name}
            </Link>
          </div>
          <ListingGrid listings={recommended} className="mt-4" />
        </section>
      )}
    </>
  );
}

async function Areas() {
  await connection();
  const res = await serverGet<LocationDto[]>(`/api/locations?parent=${HOME_CITY.slug}`);
  if (!res.ok || !Array.isArray(res.data) || res.data.length === 0) return null;
  return (
    <section aria-labelledby="areas-h" className="mx-auto max-w-6xl px-4 pt-12 sm:px-6">
      <h2 id="areas-h" className="display-wide text-xl font-bold text-asphalt">
        {HOME_CITY.name} areas
      </h2>
      <p className="mt-1 text-sm text-muted">Typical rents, sale prices and the latest homes in each area.</p>
      <ul className="mt-4 flex flex-wrap gap-2">
        {res.data.map((a) => (
          <li key={a.id}>
            <Link href={`/areas/${a.slug}`} className={chipClass(false)}>
              {a.name}
            </Link>
          </li>
        ))}
      </ul>
    </section>
  );
}
