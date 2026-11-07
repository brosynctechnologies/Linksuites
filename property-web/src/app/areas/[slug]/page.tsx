import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { formatCount } from "@/lib/format";
import { formatMoney } from "@/lib/money";
import { serverGet } from "@/lib/server-api";
import { shareMetadata } from "@/lib/site";
import type { AreaGuideDto } from "@/lib/types";
import { ListingGrid } from "@/components/listing-card";
import { LoadProblem, SitePage } from "@/components/site-chrome";
import { buttonClass, chipClass } from "@/components/styles";

export async function generateMetadata({ params }: PageProps<"/areas/[slug]">): Promise<Metadata> {
  const { slug } = await params;
  const res = await serverGet<AreaGuideDto>(`/api/areas/${encodeURIComponent(slug)}`);
  // A real 404 must be decided here: metadata resolves before the page streams, so the response gets a 404 status.
  if (!res.ok && res.status === 404) notFound();
  if (!res.ok) return { title: "Area guide" };
  const name = res.data.location.name;
  const title = `Living in ${name}: rents, sale prices and homes`;
  const description = `Typical yearly rents by bedrooms, sale prices and the latest homes in ${name}, from checked agents with the full move-in cost on every listing.`;
  const url = `/areas/${res.data.location.slug}`;
  return { title, description, alternates: { canonical: url }, ...shareMetadata({ title, description, url, image: res.data.latest[0]?.coverUrl }) };
}

export default function AreaPage({ params }: PageProps<"/areas/[slug]">) {
  return (
    <SitePage>
      <Suspense fallback={<div className="mx-auto h-48 max-w-6xl animate-pulse px-4 py-10" aria-hidden="true" />}>{params.then((p) => <Area slug={p.slug} />)}</Suspense>
    </SitePage>
  );
}

async function Area({ slug }: { slug: string }) {
  const res = await serverGet<AreaGuideDto>(`/api/areas/${encodeURIComponent(slug)}`);
  if (!res.ok && res.status === 404) notFound();
  if (!res.ok) {
    return (
      <div className="mx-auto max-w-6xl px-4 py-10 sm:px-6">
        <LoadProblem status={res.status} what="this area guide" />
      </div>
    );
  }
  const g = res.data;
  const loc = g.location;
  const bands = [...(g.rentByBedrooms ?? [])].sort((a, b) => a.bedrooms - b.bedrooms);
  const max = Math.max(1, ...bands.map((b) => b.medianMinor));

  return (
    <div className="mx-auto max-w-6xl px-4 py-8 sm:px-6">
      <nav aria-label="Breadcrumb">
        <ol className="flex flex-wrap items-center gap-1.5 text-sm text-muted">
          {g.breadcrumb
            .filter((b) => b.slug !== loc.slug)
            .map((b) => (
              <li key={b.slug} className="flex items-center gap-1.5">
                <Link href={`/areas/${b.slug}`} className="hover:text-asphalt hover:underline">
                  {b.name}
                </Link>
                <span aria-hidden="true">/</span>
              </li>
            ))}
          <li aria-current="page" className="text-asphalt">
            {loc.name}
          </li>
        </ol>
      </nav>
      <h1 className="display-wide mt-2 text-3xl font-bold text-asphalt">Living in {loc.name}</h1>
      <p className="mt-1 text-slate-600">{formatCount(g.activeCount)} live listing{g.activeCount === 1 ? "" : "s"} right now.</p>
      <div className="mt-4 flex flex-wrap gap-2">
        <Link href={`/rent/${loc.slug}`} className={buttonClass("primary")}>
          Homes for rent
        </Link>
        <Link href={`/buy/${loc.slug}`} className={buttonClass("secondary")}>
          For sale
        </Link>
        <Link href={`/shortlet/${loc.slug}`} className={buttonClass("secondary")}>
          Short-lets
        </Link>
      </div>

      <div className="mt-8 grid gap-6 lg:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)]">
        <section aria-labelledby="rent-h" className="rounded-panel border border-line bg-surface p-5">
          <h2 id="rent-h" className="font-semibold text-asphalt">
            Typical yearly rent by bedrooms
          </h2>
          <p className="text-sm text-muted">Median of live listings. Counts show how many homes each figure is based on.</p>
          {bands.length === 0 ? (
            <p className="mt-4 text-sm text-slate-700">Not enough rentals here yet to show typical rents.</p>
          ) : (
            <ul className="mt-4 space-y-3">
              {bands.map((b) => (
                <li key={b.bedrooms} className="grid grid-cols-[4.5rem_minmax(0,1fr)] items-center gap-3">
                  <span className="text-sm text-slate-700">{b.bedrooms >= 5 ? "5+ beds" : b.bedrooms === 0 ? "Studio" : `${b.bedrooms} bed${b.bedrooms === 1 ? "" : "s"}`}</span>
                  <div className="min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="h-3 rounded-full bg-adire" style={{ width: `${Math.max(4, (b.medianMinor / max) * 100)}%` }} aria-hidden="true" />
                    </div>
                    <p className="mt-0.5 flex flex-wrap justify-between gap-x-2 text-sm">
                      <span className="price text-asphalt">{formatMoney(b.medianMinor)}/year</span>
                      <span className="text-xs text-muted">{formatCount(b.count)} home{b.count === 1 ? "" : "s"}</span>
                    </p>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </section>
        <section aria-label="Other prices" className="space-y-3">
          <div className="rounded-panel border border-line bg-surface p-5">
            <p className="text-sm text-muted">Typical sale price</p>
            <p className="price mt-1 text-2xl text-asphalt">{g.medianSaleMinor != null ? formatMoney(g.medianSaleMinor) : "Not enough sales yet"}</p>
          </div>
          <div className="rounded-panel border border-line bg-surface p-5">
            <p className="text-sm text-muted">Typical short-let night</p>
            <p className="price mt-1 text-2xl text-asphalt">{g.medianNightlyMinor != null ? `${formatMoney(g.medianNightlyMinor)}/night` : "Not enough short-lets yet"}</p>
          </div>
        </section>
      </div>

      {g.latest.length > 0 && (
        <section aria-labelledby="latest-h" className="mt-10">
          <h2 id="latest-h" className="display-wide text-xl font-bold text-asphalt">
            Latest in {loc.name}
          </h2>
          <ListingGrid listings={g.latest} className="mt-4" />
        </section>
      )}

      {g.areas.length > 0 && (
        <section aria-labelledby="areas-h" className="mt-10">
          <h2 id="areas-h" className="display-wide text-xl font-bold text-asphalt">
            Areas in {loc.name}
          </h2>
          <ul className="mt-4 flex flex-wrap gap-2">
            {g.areas.map((a) => (
              <li key={a.id}>
                <Link href={`/areas/${a.slug}`} className={chipClass(false)}>
                  {a.name}
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}
