import type { Metadata } from "next";
import Link from "next/link";
import { connection } from "next/server";
import { Suspense } from "react";
import { APP_NAME, HOME_CITY, SITE_URL } from "@/lib/config";
import { serverGet } from "@/lib/server-api";
import { DEFAULT_DESCRIPTION, DEFAULT_TITLE } from "@/lib/site";
import { hubUrl } from "@/lib/suite";
import type { ListingCardDto, LocationDto, SearchResultDto } from "@/lib/types";
import { ListingGrid } from "@/components/listing-card";
import { LoadProblem, SiteFooter, SiteHeader } from "@/components/site-chrome";
import { Drawing, MoveInLedger } from "@/components/site-screens";
import { FinalCta, JsonLd, Section } from "@/components/suite/sections";
import { lsLink } from "@/components/suite/ui";
import { chipClass } from "@/components/styles";
import { HomeSearch } from "./home-search";

export const metadata: Metadata = {
  title: { absolute: DEFAULT_TITLE },
  description: DEFAULT_DESCRIPTION,
  alternates: { canonical: "/" },
};

const REAL = [
  {
    title: "Checked agents",
    text: "Agents and landlords show us a government ID and their business papers before they get the green Verified seal.",
  },
  {
    title: "Checked photos",
    text: "Every photo is compared with photos already on PropertyLink, so nobody can post a flat that isn't theirs.",
  },
  {
    title: "Listings that are still available",
    text: "A listing comes down unless the agent confirms the home is still on the market. Each one shows when it was last confirmed.",
  },
];

export default function HomePage() {
  return (
    <>
      <JsonLd
        data={{
          "@context": "https://schema.org",
          "@type": "WebSite",
          name: APP_NAME,
          url: `${SITE_URL}/`,
          inLanguage: "en-NG",
          description: DEFAULT_DESCRIPTION,
          publisher: { "@type": "Organization", name: "LinkSuite", url: hubUrl },
        }}
      />
      <SiteHeader />
      <main id="main" tabIndex={-1} className="flex-1 outline-none">
        <section aria-labelledby="hero-title" className="ls-container pt-8 pb-12 sm:pt-12 md:pb-16 lg:pt-16">
          <div className="grid gap-10 lg:grid-cols-[minmax(0,1fr)_19rem] lg:gap-14">
            <div className="min-w-0">
              <h1 id="hero-title" className="ls-h1 max-w-[14ch] text-ls-ink">
                Find a home in Lagos, with every cost shown.
              </h1>
              <p className="ls-lead mt-5 max-w-[38rem] text-ls-ink-2">
                Rent, buy or book a short-let from checked agents. Every listing adds up the rent, agency fee, legal fee and caution, so you know what
                moving in really costs.
              </p>
              <div className="mt-7">
                <HomeSearch />
              </div>
              <ul className="ls-small mt-5 grid gap-1.5 text-ls-ink-2 sm:grid-cols-3 sm:gap-4">
                <li>Viewings are free. Never pay an inspection fee.</li>
                <li>The green seal means we checked the agent&apos;s papers.</li>
                <li>Agents confirm each home is still available.</li>
              </ul>
            </div>
            <div className="hidden lg:block lg:pt-3">
              <Drawing
                className="ls-paint rounded-[10px]"
                label="A PropertyLink listing for a 2 bedroom flat in Yaba from a verified agent, with the cost to move in added up: first year's rent ₦3,500,000, agency, legal and caution fees ₦350,000 each, ₦4,550,000 in total."
              >
                <MoveInLedger />
              </Drawing>
            </div>
          </div>
        </section>

        <Suspense fallback={<FeaturedSkeleton />}>
          <Featured />
        </Suspense>

        <Suspense fallback={null}>
          <Areas />
        </Suspense>

        <Section id="real" title="How we keep listings real" tone="surface">
          <ul className="mt-8 grid gap-x-10 gap-y-2 md:grid-cols-3">
            {REAL.map((r) => (
              <li key={r.title} className="border-t border-ls-line py-5">
                <h3 className="ls-h4 text-ls-ink">{r.title}</h3>
                <p className="mt-1.5 max-w-[40ch] text-ls-ink-2">{r.text}</p>
              </li>
            ))}
          </ul>
          <p className="mt-4">
            <Link href="/how-verification-works" className={`${lsLink} inline-flex min-h-11 items-center`}>
              How verification works
            </Link>
          </p>
        </Section>

        <FinalCta
          title="Letting or selling a home? List it free."
          text="Agents, agencies and landlords post homes with the full move-in cost, take enquiries on WhatsApp and book viewings into their own hours."
          action={{ href: "/for-agents", label: "See PropertyLink for agents" }}
        />
      </main>
      <SiteFooter />
    </>
  );
}

function FeaturedSkeleton() {
  return (
    <div className="border-t border-ls-line" aria-hidden="true">
      <div className="ls-container ls-section-y">
        <div className="h-9 w-64 animate-pulse rounded bg-ls-concrete" />
        <div className="mt-8 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {[0, 1, 2, 3].map((i) => (
            <div key={i} className="aspect-[4/5] animate-pulse rounded-panel bg-ls-concrete" />
          ))}
        </div>
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
      <div className="border-t border-ls-line">
        <div className="ls-container py-10">
          <LoadProblem status={res.status} what="listings" />
        </div>
      </div>
    );
  }
  const shown = new Set(featured.map((l) => l.id));
  const recommended = (res.ok ? (res.data.items.items ?? []) : []).filter((l) => !shown.has(l.id)).slice(0, 8);
  if (featured.length === 0 && recommended.length === 0) return null;
  return (
    <>
      {featured.length > 0 && (
        <Section id="featured" title="Featured homes">
          <ListingGrid listings={featured} className="mt-8" />
        </Section>
      )}
      {recommended.length > 0 && (
        <Section id="recommended" title={`Recommended in ${HOME_CITY.name}`}>
          <ListingGrid listings={recommended} className="mt-8" />
          <p className="mt-6">
            <Link href={`/rent/${HOME_CITY.slug}`} className={`${lsLink} inline-flex min-h-11 items-center`}>
              See every home to rent in {HOME_CITY.name}
            </Link>
          </p>
        </Section>
      )}
    </>
  );
}

async function Areas() {
  await connection();
  const res = await serverGet<LocationDto[]>(`/api/locations?parent=${HOME_CITY.slug}`);
  if (!res.ok || !Array.isArray(res.data) || res.data.length === 0) return null;
  return (
    <Section id="areas" title={`Areas of ${HOME_CITY.name}`} intro="Typical rents, sale prices and the latest homes in each area.">
      <ul className="mt-6 flex flex-wrap gap-2">
        {res.data.map((a) => (
          <li key={a.id}>
            <Link href={`/areas/${a.slug}`} className={chipClass(false)}>
              {a.name}
            </Link>
          </li>
        ))}
      </ul>
    </Section>
  );
}
