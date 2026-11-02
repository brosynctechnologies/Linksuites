import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { formatDay, listingPath, placeLine, plural } from "@/lib/format";
import { formatMoney, formatPrice, periodSuffix } from "@/lib/money";
import { purposePath } from "@/lib/search";
import { serverGet } from "@/lib/server-api";
import type { ListingDetailDto } from "@/lib/types";
import { VerifiedSeal } from "@/components/badges";
import { BathIcon, BedIcon, CheckIcon, ClockIcon, ExternalIcon, HomeIcon, KeyIcon, RulerIcon } from "@/components/icons";
import { Ledger, priceLineLabel } from "@/components/ledger";
import { ListingCard } from "@/components/listing-card";
import { PointMap } from "@/components/maps";
import { SaveHeart } from "@/components/save-heart";
import { LoadProblem, SiteFooter, SiteHeader } from "@/components/site-chrome";
import { linkClass } from "@/components/styles";
import { AgentPanel, MobileContactBar, ViewTracker, type ContactListing } from "./contact";
import { Gallery } from "./gallery";
import { ReportLink } from "./report";
import { StayBooker } from "./stay-booker";

const COST_LABEL: Record<string, string> = {
  AgencyFee: "Agency fee",
  LegalFee: "Legal fee",
  Caution: "Caution deposit",
  ServiceCharge: "Service charge",
  Other: "Other",
};

function detailPath(agency: string, slug: string) {
  return `/api/listings/${encodeURIComponent(agency)}/${encodeURIComponent(slug)}`;
}

export async function generateMetadata({ params }: PageProps<"/listings/[agency]/[slug]">): Promise<Metadata> {
  const { agency, slug } = await params;
  const res = await serverGet<ListingDetailDto>(detailPath(agency, slug));
  // A real 404 must be decided here: metadata resolves before the page streams, so the response gets a 404 status.
  if (!res.ok && res.status === 404) notFound();
  if (!res.ok) return { title: "Listing" };
  const l = res.data;
  const where = `${formatPrice(l.priceMinor, l.pricePeriod, l.currency)} in ${placeLine(...l.breadcrumb.slice(-2).reverse().map((b) => b.name))}.`;
  // A short-let is booked by the night: the online price is nights plus cleaning, and any other cost is paid to the host.
  const hostPaid = (l.costs ?? []).reduce((sum, c) => sum + c.amountMinor, 0);
  const cost =
    l.purpose === "ShortLet"
      ? `Cleaning ${formatMoney(l.shortLet?.cleaningFeeMinor ?? 0, l.currency)}${hostPaid > 0 ? `, plus ${formatMoney(hostPaid, l.currency)} paid to the host, not charged online` : ""}. Book and pay online.`
      : `${l.purpose === "Sale" ? "Total to buy" : "Move-in total"} ${formatMoney(l.moveInTotalMinor, l.currency)}, every cost shown.`;
  return {
    title: l.title,
    description: `${where} ${cost}`,
    openGraph: l.photos[0] ? { images: [l.photos[0].cardUrl] } : undefined,
  };
}

export default function ListingPage({ params }: PageProps<"/listings/[agency]/[slug]">) {
  return (
    <div className="flex min-h-screen flex-col">
      <SiteHeader />
      <main className="flex-1 pb-24 lg:pb-0">
        <Suspense fallback={<ListingSkeleton />}>{params.then((p) => <Listing agency={p.agency} slug={p.slug} />)}</Suspense>
      </main>
      <SiteFooter />
    </div>
  );
}

function ListingSkeleton() {
  return (
    <div className="mx-auto max-w-6xl sm:px-6 sm:pt-6" aria-hidden="true">
      <div className="aspect-[4/3] animate-pulse bg-plaster-2 sm:aspect-[16/9] sm:rounded-panel" />
      <div className="mx-4 mt-5 h-8 w-2/3 animate-pulse rounded bg-plaster-2 sm:mx-0" />
    </div>
  );
}

function Fact({ icon, label, value }: { icon: React.ReactNode; label: string; value: React.ReactNode }) {
  return (
    <div className="flex items-center gap-3 rounded-panel border border-line bg-surface px-3 py-2.5">
      <span className="text-muted">{icon}</span>
      <div className="min-w-0">
        <dt className="text-xs text-muted">{label}</dt>
        <dd className="truncate font-semibold text-asphalt">{value}</dd>
      </div>
    </div>
  );
}

async function Listing({ agency, slug }: { agency: string; slug: string }) {
  const res = await serverGet<ListingDetailDto>(detailPath(agency, slug));
  if (!res.ok && res.status === 404) notFound();
  if (!res.ok) {
    return (
      <div className="mx-auto max-w-6xl px-4 py-10 sm:px-6">
        <LoadProblem status={res.status} what="this listing" />
      </div>
    );
  }
  const l = res.data;
  const path = listingPath(l.agency.slug, l.slug);
  const contact: ContactListing = { id: l.id, title: l.title, path, purpose: l.purpose };
  const area = l.breadcrumb[l.breadcrumb.length - 1];
  const place = placeLine(...[...l.breadcrumb].reverse().slice(0, 2).map((b) => b.name));
  const isShortLet = l.purpose === "ShortLet" && !!l.shortLet;
  const base = purposePath(l.purpose);
  const purposeName = l.purpose === "Sale" ? "Buy" : l.purpose === "ShortLet" ? "Short-let" : "Rent";
  const costs = l.costs ?? [];

  return (
    <article>
      <ViewTracker listingId={l.id} />
      <nav aria-label="Breadcrumb" className="mx-auto max-w-6xl px-4 pt-4 sm:px-6">
        <ol className="no-scrollbar flex items-center gap-1.5 overflow-x-auto text-sm whitespace-nowrap text-muted">
          <li>
            <Link href={base} className="hover:text-asphalt hover:underline">
              {purposeName}
            </Link>
          </li>
          {l.breadcrumb.map((b) => (
            <li key={b.slug} className="flex items-center gap-1.5">
              <span aria-hidden="true">/</span>
              <Link href={`${base}/${b.slug}`} className="hover:text-asphalt hover:underline">
                {b.name}
              </Link>
            </li>
          ))}
        </ol>
      </nav>

      <div className="mx-auto mt-3 max-w-6xl sm:px-6">
        <Gallery photos={l.photos ?? []} title={l.title} />
      </div>

      <div className="mx-auto mt-5 grid max-w-6xl gap-8 px-4 sm:px-6 lg:grid-cols-[minmax(0,1fr)_23rem] lg:gap-10">
        <div className="min-w-0 space-y-8">
          <header>
            <div className="flex items-start justify-between gap-3">
              <div className="min-w-0">
                <p className="text-adire">
                  <span className="price text-[1.75rem] leading-tight sm:text-[2.25rem]">{formatMoney(l.priceMinor, l.currency)}</span>
                  <span className="text-lg font-semibold text-muted">{periodSuffix(l.pricePeriod)}</span>
                </p>
                <h1 className="mt-1 text-xl font-semibold text-asphalt sm:text-2xl">{l.title}</h1>
                <p className="mt-1 text-slate-600">{l.address ? `${l.address}, ${place}` : place}</p>
              </div>
              <SaveHeart listingId={l.id} withLabel />
            </div>
            <p className="mt-3 flex flex-wrap items-center gap-x-2 gap-y-1 text-sm text-slate-600">
              Listed by{" "}
              <Link href={`/agencies/${l.agency.slug}`} className="font-semibold text-asphalt hover:underline">
                {l.agency.name}
              </Link>
              <VerifiedSeal verified={l.agency.verified} />
            </p>
          </header>

          <section aria-labelledby="facts-h">
            <h2 id="facts-h" className="sr-only">
              Key facts
            </h2>
            <dl className="grid grid-cols-2 gap-2 sm:grid-cols-3">
              <Fact icon={<HomeIcon />} label="Type" value={l.type.name} />
              {l.bedrooms > 0 && <Fact icon={<BedIcon />} label="Bedrooms" value={l.bedrooms} />}
              {l.bathrooms > 0 && <Fact icon={<BathIcon />} label="Bathrooms" value={l.bathrooms} />}
              {l.toilets > 0 && <Fact icon={<BathIcon />} label="Toilets" value={l.toilets} />}
              {l.sizeSqm ? <Fact icon={<RulerIcon />} label="Size" value={`${l.sizeSqm} m²`} /> : null}
              <Fact icon={<KeyIcon />} label="Furnishing" value={l.furnished ? "Furnished" : "Unfurnished"} />
              {isShortLet && <Fact icon={<CheckIcon />} label="Guests" value={`Up to ${l.shortLet!.maxGuests}`} />}
              {isShortLet && (l.shortLet!.checkInTime || l.shortLet!.checkOutTime) && (
                <Fact icon={<ClockIcon />} label="Check-in / out" value={`${l.shortLet!.checkInTime ?? "—"} / ${l.shortLet!.checkOutTime ?? "—"}`} />
              )}
            </dl>
          </section>

          {l.description && (
            <section aria-labelledby="desc-h">
              <h2 id="desc-h" className="text-lg font-semibold text-asphalt">
                About this home
              </h2>
              <p className="prose-listing mt-2">{l.description}</p>
            </section>
          )}

          {isShortLet && l.shortLet!.houseRules && (
            <section aria-labelledby="rules-h">
              <h2 id="rules-h" className="text-lg font-semibold text-asphalt">
                House rules
              </h2>
              <p className="prose-listing mt-2">{l.shortLet!.houseRules}</p>
            </section>
          )}

          {l.amenities.length > 0 && (
            <section aria-labelledby="amen-h">
              <h2 id="amen-h" className="text-lg font-semibold text-asphalt">
                Amenities
              </h2>
              <ul className="mt-3 grid grid-cols-2 gap-x-4 gap-y-2 sm:grid-cols-3">
                {l.amenities.map((a) => (
                  <li key={a.slug} className="flex items-center gap-2 text-slate-700">
                    <CheckIcon className="h-4 w-4 shrink-0 text-adire" />
                    {a.name}
                  </li>
                ))}
              </ul>
            </section>
          )}

          {(l.tourUrl || l.videoUrl) && (
            <section aria-label="Tours" className="flex flex-wrap gap-4">
              {l.tourUrl && (
                <a href={l.tourUrl} target="_blank" rel="noopener noreferrer" className={`${linkClass} inline-flex min-h-11 items-center gap-1.5`}>
                  <ExternalIcon className="h-4 w-4" /> Virtual tour
                </a>
              )}
              {l.videoUrl && (
                <a href={l.videoUrl} target="_blank" rel="noopener noreferrer" className={`${linkClass} inline-flex min-h-11 items-center gap-1.5`}>
                  <ExternalIcon className="h-4 w-4" /> Video walkthrough
                </a>
              )}
            </section>
          )}

          <section aria-labelledby="map-h">
            <div className="flex flex-wrap items-baseline justify-between gap-2">
              <h2 id="map-h" className="text-lg font-semibold text-asphalt">
                Location
              </h2>
              {area && (
                <Link href={`/areas/${area.slug}`} className={linkClass}>
                  Living in {area.name}: rents and listings
                </Link>
              )}
            </div>
            <PointMap lat={l.lat} lng={l.lng} label={`Map showing ${l.title} in ${place}`} className="mt-3 h-64 sm:h-80" />
            <p className="mt-2 text-xs text-muted">
              {l.address ? "The pin shows the street, at the address above. " : "The pin shows the street. "}
              {isShortLet
                ? "The host's number and check-in details come with your confirmed booking."
                : "Check the exact unit with the agent when you book a viewing."}
            </p>
          </section>

          <section className="space-y-1 border-t border-line pt-4 text-sm text-muted">
            <p>
              Listed {formatDay(l.publishedAt)}
              {l.lastConfirmedAt ? `. Agent last confirmed it was available on ${formatDay(l.lastConfirmedAt)}.` : "."}
            </p>
            <ReportLink listingId={l.id} />
          </section>
        </div>

        <aside className="min-w-0 lg:sticky lg:top-[calc(var(--support-bar,0px)+1rem)] lg:max-h-[calc(100dvh-2rem-var(--support-bar,0px))] lg:self-start lg:overflow-y-auto lg:pb-4 lg:[scrollbar-width:thin]" aria-label="Costs and contact">
          <div className="space-y-4">
            {isShortLet ? (
              <StayBooker
                listingId={l.id}
                title={l.title}
                nightlyMinor={l.priceMinor}
                currency={l.currency}
                shortLet={l.shortLet!}
                hostPaid={costs.map((c, i) => ({ key: `c${i}`, label: c.label || COST_LABEL[c.kind] || c.kind, amountMinor: c.amountMinor }))}
              />
            ) : (
              <Ledger
                currency={l.currency}
                title={l.purpose === "Sale" ? "What it costs to buy" : "What it costs to move in"}
                totalLabel={l.purpose === "Sale" ? "Total to buy" : "Move-in total"}
                lines={[
                  { key: "price", label: priceLineLabel(l.purpose, l.pricePeriod), amountMinor: l.priceMinor },
                  ...costs.map((c, i) => ({
                    key: `c${i}`,
                    label: c.label || COST_LABEL[c.kind] || c.kind,
                    note: c.label && COST_LABEL[c.kind] && !c.label.toLowerCase().includes(COST_LABEL[c.kind].toLowerCase().split(" ")[0]) ? COST_LABEL[c.kind] : undefined,
                    amountMinor: c.amountMinor,
                  })),
                ]}
                totalMinor={l.moveInTotalMinor}
                footnote={costs.length === 0 ? "The agent lists no other fees for this home." : `${plural(costs.length + 1, "line")}, all set by the agent. If anyone asks for a fee that is not here, report the listing.`}
              />
            )}
            <AgentPanel agent={l.agent} agency={l.agency} listing={contact} showViewing={!isShortLet} />
          </div>
        </aside>
      </div>

      {l.similar.length > 0 && (
        <section aria-labelledby="similar-h" className="mx-auto mt-12 max-w-6xl px-4 pb-12 sm:px-6">
          <h2 id="similar-h" className="display-wide text-xl font-bold text-asphalt">
            Similar homes nearby
          </h2>
          <ul className="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            {l.similar.slice(0, 4).map((s) => (
              <li key={s.id} className="flex">
                <ListingCard listing={s} className="w-full" />
              </li>
            ))}
          </ul>
        </section>
      )}

      <MobileContactBar price={formatMoney(l.priceMinor, l.currency)} period={periodSuffix(l.pricePeriod)} />
    </article>
  );
}
