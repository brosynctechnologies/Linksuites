import type { Metadata } from "next";
import Link from "next/link";
import { VerifiedSeal } from "@/components/badges";
import { ShieldIcon } from "@/components/icons";
import { SitePage } from "@/components/site-chrome";
import { buttonClass, linkClass } from "@/components/styles";

export const metadata: Metadata = {
  title: "How verification works",
  description: "What the green Verified seal means on PropertyLink, how we check photos, and why listings expire.",
};

export default function HowVerificationWorksPage() {
  return (
    <SitePage>
      <div className="mx-auto max-w-3xl px-4 py-10 sm:px-6 sm:py-14">
        <h1 className="display-wide text-3xl font-bold text-asphalt">How verification works</h1>
        <p className="mt-3 text-lg text-slate-700">What you see is real, and what you pay is all here. This is how we hold agents to that.</p>

        <section className="mt-10 space-y-3" aria-labelledby="seal-h">
          <h2 id="seal-h" className="flex flex-wrap items-center gap-2 text-xl font-semibold text-asphalt">
            The <VerifiedSeal verified /> seal
          </h2>
          <p className="text-slate-700">
            An agency, agent or landlord gets the seal after they send us a government ID and, for businesses, a registration document or estate surveyor
            licence. A PropertyLink moderator checks the documents by hand. Until then their listings go through moderation before they appear.
          </p>
          <p className="text-slate-700">
            No seal doesn&apos;t mean a scam: many honest landlords haven&apos;t sent documents yet. It means we haven&apos;t checked them, so take more care.
          </p>
        </section>

        <section className="mt-8 space-y-3" aria-labelledby="photos-h">
          <h2 id="photos-h" className="text-xl font-semibold text-asphalt">
            Duplicate-photo checks
          </h2>
          <p className="text-slate-700">
            Every photo uploaded is compared with photos already on the site. If the same pictures turn up under another agency, the listing goes to a
            moderator before anyone sees it. Photos carry the agency&apos;s watermark, so copies are easy to spot.
          </p>
        </section>

        <section className="mt-8 space-y-3" aria-labelledby="expiry-h">
          <h2 id="expiry-h" className="text-xl font-semibold text-asphalt">
            Listings expire unless confirmed
          </h2>
          <p className="text-slate-700">
            Listings come down after 30 or 60 days unless the agent confirms the home is still available. Each listing shows when it was last confirmed.
          </p>
        </section>

        <section className="mt-8 space-y-3" aria-labelledby="costs-h">
          <h2 id="costs-h" className="text-xl font-semibold text-asphalt">
            Every cost on the listing
          </h2>
          <p className="text-slate-700">
            Agents list the rent and every fee: agency, legal, caution, service charge. We add them into one move-in total. Agents can&apos;t add an
            &quot;inspection fee&quot;: viewings on PropertyLink are free.
          </p>
        </section>

        <div className="mt-10 flex gap-3 rounded-panel border border-line bg-surface p-5">
          <ShieldIcon className="mt-0.5 h-5 w-5 shrink-0 text-brick" />
          <div>
            <p className="font-semibold text-asphalt">Never pay before you have inspected the property and met the agent.</p>
            <p className="mt-1 text-sm text-slate-700">
              If anyone asks for money first, use &quot;Report this listing&quot; on the listing page. Reported listings are hidden while we look.
            </p>
          </div>
        </div>

        <div className="mt-8 flex flex-wrap items-center gap-4">
          <Link href="/rent" className={buttonClass("primary")}>
            Find a home
          </Link>
          <Link href="/studio/verification" className={`${linkClass} inline-flex min-h-11 items-center`}>
            Agents: get verified
          </Link>
        </div>
      </div>
    </SitePage>
  );
}
