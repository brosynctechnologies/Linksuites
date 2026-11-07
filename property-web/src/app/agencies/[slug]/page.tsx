import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { serverGet } from "@/lib/server-api";
import { shareMetadata } from "@/lib/site";
import { agencyJsonLd } from "@/lib/structured-data";
import type { AgencyPageDto } from "@/lib/types";
import { Avatar, Rating, VerifiedSeal } from "@/components/badges";
import { ListingGrid } from "@/components/listing-card";
import { ProfileContact } from "@/components/profile-bits";
import { LoadProblem, SitePage } from "@/components/site-chrome";
import { linkClass } from "@/components/styles";
import { JsonLd } from "@/components/suite/sections";

const KIND: Record<string, string> = { Agency: "Estate agency", Agent: "Independent agent", Landlord: "Landlord" };

export async function generateMetadata({ params }: PageProps<"/agencies/[slug]">): Promise<Metadata> {
  const { slug } = await params;
  const res = await serverGet<AgencyPageDto>(`/api/agencies/${encodeURIComponent(slug)}`);
  // A real 404 must be decided here: metadata resolves before the page streams, so the response gets a 404 status.
  if (!res.ok && res.status === 404) notFound();
  if (!res.ok) return { title: "Agency" };
  const { agency, address, listings } = res.data;
  const title = agency.name;
  const where = address ? ` in ${address}` : "";
  const homes = listings.length ? `${listings.length} live listing${listings.length === 1 ? "" : "s"}` : "Homes";
  const description = `${KIND[agency.kind] ?? agency.kind}${where}${agency.verified ? ", verified by PropertyLink" : ""}. ${homes} to rent, buy or short-let, each with the full move-in cost.`;
  const url = `/agencies/${agency.slug}`;
  return { title, description, alternates: { canonical: url }, ...shareMetadata({ title, description, url, image: agency.logoUrl }) };
}

export default function AgencyPage({ params }: PageProps<"/agencies/[slug]">) {
  return (
    <SitePage>
      <Suspense fallback={<div className="mx-auto h-48 max-w-6xl animate-pulse px-4 py-10" aria-hidden="true" />}>{params.then((p) => <Agency slug={p.slug} />)}</Suspense>
    </SitePage>
  );
}

async function Agency({ slug }: { slug: string }) {
  const res = await serverGet<AgencyPageDto>(`/api/agencies/${encodeURIComponent(slug)}`);
  if (!res.ok && res.status === 404) notFound();
  if (!res.ok) {
    return (
      <div className="mx-auto max-w-6xl px-4 py-10 sm:px-6">
        <LoadProblem status={res.status} what="this agency" />
      </div>
    );
  }
  const { agency, bio, address, agents, listings, ratingAverage, ratingCount } = res.data;
  return (
    <div className="mx-auto max-w-6xl px-4 py-8 sm:px-6">
      <JsonLd data={agencyJsonLd(agency, { bio, address, ratingAverage, ratingCount })} />
      <header className="flex flex-col gap-5 sm:flex-row sm:items-start">
        {agency.logoUrl ? (
          <img src={agency.logoUrl} alt="" className="h-20 w-20 shrink-0 rounded-panel border border-line bg-surface object-contain p-1" />
        ) : (
          <Avatar name={agency.name} size={80} />
        )}
        <div className="min-w-0 flex-1">
          <h1 className="display-wide flex flex-wrap items-center gap-2 text-2xl font-bold text-asphalt">
            {agency.name} <VerifiedSeal verified={agency.verified} />
          </h1>
          <p className="mt-1 text-slate-600">
            {KIND[agency.kind] ?? agency.kind}
            {address ? `, ${address}` : ""}
          </p>
          <Rating average={ratingAverage} count={ratingCount} className="mt-1" />
          {bio && <p className="prose-listing mt-3">{bio}</p>}
          <div className="mt-4">
            <ProfileContact phone={agency.phone} name={agency.name} />
          </div>
          {!agency.verified && (
            <p className="mt-4 max-w-prose rounded-panel border border-line bg-surface p-3 text-sm text-slate-700">
              {agency.name} hasn&apos;t completed document checks yet. Their listings still pass our duplicate-photo checks.{" "}
              <Link href="/how-verification-works" className={linkClass}>
                How verification works
              </Link>
            </p>
          )}
        </div>
      </header>

      {agents.length > 0 && (
        <section aria-labelledby="agents-h" className="mt-10">
          <h2 id="agents-h" className="display-wide text-xl font-bold text-asphalt">
            Agents
          </h2>
          <ul className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {agents.map((a) => (
              <li key={a.id}>
                <Link href={`/agents/${a.slug}`} className="flex min-h-11 items-center gap-3 rounded-panel border border-line bg-surface p-3 hover:border-slate-300">
                  <Avatar src={a.photoUrl} name={a.displayName} size={44} />
                  <span className="min-w-0">
                    <span className="flex flex-wrap items-center gap-1.5 font-semibold text-asphalt">
                      {a.displayName} <VerifiedSeal verified={a.verified} />
                    </span>
                    <Rating average={a.ratingAverage} count={a.ratingCount} />
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}

      <section aria-labelledby="listings-h" className="mt-10">
        <h2 id="listings-h" className="display-wide text-xl font-bold text-asphalt">
          Live listings
        </h2>
        {listings.length ? <ListingGrid listings={listings} className="mt-4" /> : <p className="mt-2 text-sm text-muted">No live listings right now.</p>}
      </section>
    </div>
  );
}
