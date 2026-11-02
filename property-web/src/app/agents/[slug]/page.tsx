import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { formatDay } from "@/lib/format";
import { serverGet } from "@/lib/server-api";
import type { AgentPageDto } from "@/lib/types";
import { Avatar, Rating, Stars, VerifiedSeal } from "@/components/badges";
import { ListingGrid } from "@/components/listing-card";
import { ProfileContact, ReportReviewButton, ReviewForm } from "@/components/profile-bits";
import { LoadProblem, SiteFooter, SiteHeader } from "@/components/site-chrome";

const BASIS: Record<string, string> = { Enquiry: "After an enquiry", Inspection: "After a viewing", Stay: "After a stay" };

export async function generateMetadata({ params }: PageProps<"/agents/[slug]">): Promise<Metadata> {
  const { slug } = await params;
  const res = await serverGet<AgentPageDto>(`/api/agents/${encodeURIComponent(slug)}`);
  // A real 404 must be decided here: metadata resolves before the page streams, so the response gets a 404 status.
  if (!res.ok && res.status === 404) notFound();
  return res.ok ? { title: `${res.data.agent.displayName}, ${res.data.agency.name}` } : { title: "Agent" };
}

export default function AgentPage({ params }: PageProps<"/agents/[slug]">) {
  return (
    <div className="flex min-h-screen flex-col">
      <SiteHeader />
      <main className="flex-1">
        <Suspense fallback={<div className="mx-auto h-48 max-w-6xl animate-pulse px-4 py-10" aria-hidden="true" />}>{params.then((p) => <Agent slug={p.slug} />)}</Suspense>
      </main>
      <SiteFooter />
    </div>
  );
}

async function Agent({ slug }: { slug: string }) {
  const res = await serverGet<AgentPageDto>(`/api/agents/${encodeURIComponent(slug)}`);
  if (!res.ok && res.status === 404) notFound();
  if (!res.ok) {
    return (
      <div className="mx-auto max-w-6xl px-4 py-10 sm:px-6">
        <LoadProblem status={res.status} what="this agent" />
      </div>
    );
  }
  const { agent, agency, bio, licenceNo, listings, reviews } = res.data;
  return (
    <div className="mx-auto max-w-6xl px-4 py-8 sm:px-6">
      <header className="flex flex-col gap-5 sm:flex-row sm:items-start">
        <Avatar src={agent.photoUrl} name={agent.displayName} size={88} />
        <div className="min-w-0 flex-1">
          <h1 className="display-wide flex flex-wrap items-center gap-2 text-2xl font-bold text-asphalt">
            {agent.displayName} <VerifiedSeal verified={agent.verified} />
          </h1>
          <p className="mt-1 flex flex-wrap items-center gap-1.5 text-slate-600">
            <Link href={`/agencies/${agency.slug}`} className="font-semibold text-asphalt hover:underline">
              {agency.name}
            </Link>
            <VerifiedSeal verified={agency.verified} />
          </p>
          <div className="mt-1 flex flex-wrap items-center gap-3 text-sm">
            <Rating average={agent.ratingAverage} count={agent.ratingCount} />
            {licenceNo && <span className="text-muted">Licence {licenceNo}</span>}
          </div>
          {bio && <p className="prose-listing mt-3">{bio}</p>}
          <div className="mt-4">
            <ProfileContact phone={agent.phone} whatsApp={agent.whatsApp} name={agent.displayName} />
          </div>
        </div>
      </header>

      <section aria-labelledby="listings-h" className="mt-10">
        <h2 id="listings-h" className="display-wide text-xl font-bold text-asphalt">
          Live listings
        </h2>
        {listings.length ? <ListingGrid listings={listings} className="mt-4" /> : <p className="mt-2 text-sm text-muted">No live listings right now.</p>}
      </section>

      <section aria-labelledby="reviews-h" className="mt-10 grid gap-6 lg:grid-cols-[minmax(0,1fr)_22rem]">
        <div>
          <h2 id="reviews-h" className="display-wide text-xl font-bold text-asphalt">
            Reviews
          </h2>
          {reviews.length === 0 ? (
            <p className="mt-2 text-sm text-muted">No reviews yet.</p>
          ) : (
            <ul className="mt-3 divide-y divide-line rounded-panel border border-line bg-surface">
              {reviews.map((r) => (
                <li key={r.id} className="p-4">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <p className="flex items-center gap-2">
                      <Stars value={r.rating} />
                      <span className="font-semibold text-asphalt">{r.userName}</span>
                    </p>
                    <span className="text-xs text-muted">
                      {BASIS[r.basis] ?? r.basis}, {formatDay(r.createdAt)}
                    </span>
                  </div>
                  {r.comment && <p className="mt-1.5 text-slate-700">{r.comment}</p>}
                  <ReportReviewButton reviewId={r.id} />
                </li>
              ))}
            </ul>
          )}
        </div>
        <div className="lg:pt-10">
          <ReviewForm agentSlug={agent.slug} agentName={agent.displayName} />
        </div>
      </section>
    </div>
  );
}
