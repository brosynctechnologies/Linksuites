import type { Metadata } from "next";
import Link from "next/link";
import { shareMetadata } from "@/lib/site";
import { VerifiedSeal } from "@/components/badges";
import { ShieldIcon } from "@/components/icons";
import { SitePage } from "@/components/site-chrome";
import { FinalCta } from "@/components/suite/sections";
import { lsButton, lsLink } from "@/components/suite/ui";

const TITLE = "How verification works";
const DESCRIPTION =
  "What the green Verified seal means on PropertyLink, how we check agents' papers and photos, why listings expire, and why you should never pay to inspect.";

export const metadata: Metadata = {
  title: TITLE,
  description: DESCRIPTION,
  alternates: { canonical: "/how-verification-works" },
  ...shareMetadata({ title: TITLE, description: DESCRIPTION, url: "/how-verification-works" }),
};

const CHECKS = [
  {
    id: "photos",
    title: "Duplicate-photo checks",
    text: "Every photo uploaded is compared with photos already on PropertyLink. If the same pictures turn up under another agency, the listing goes to a moderator before anyone sees it. Photos carry the agency's watermark, so copies are easy to spot.",
  },
  {
    id: "expiry",
    title: "Listings expire unless confirmed",
    text: "Listings come down after 30 or 60 days unless the agent confirms the home is still available. Each listing shows when it was last confirmed.",
  },
  {
    id: "costs",
    title: "Every cost on the listing",
    text: "Agents list the rent and every fee: agency, legal, caution and service charge. We add them into one move-in total. Agents can't add an inspection fee: viewings on PropertyLink are free.",
  },
];

export default function HowVerificationWorksPage() {
  return (
    <SitePage>
      <div className="ls-container pt-10 pb-14 sm:pt-14 md:pb-20">
        <div className="max-w-[46rem]">
          <h1 className="ls-h1 max-w-[14ch] text-ls-ink">How verification works</h1>
          <p className="ls-lead mt-5 text-ls-ink-2">What you see is real, and what you pay is all here. This is how we hold agents to that.</p>

          <section className="mt-12 border-t border-ls-line pt-8" aria-labelledby="seal-h">
            <h2 id="seal-h" className="ls-h3 flex flex-wrap items-center gap-2 text-ls-ink">
              The <VerifiedSeal verified className="font-ls-text" /> seal
            </h2>
            <div className="mt-3 max-w-[60ch] space-y-3 text-ls-ink-2">
              <p>
                An agency, agent or landlord gets the seal after they send us a government ID and, for businesses, a registration document or estate
                surveyor&apos;s licence. A PropertyLink moderator checks the documents by hand. Until then, their listings go through moderation before
                they appear.
              </p>
              <p>No seal doesn&apos;t mean a scam: many honest landlords haven&apos;t sent documents yet. It means we haven&apos;t checked them, so take more care.</p>
            </div>
          </section>

          {CHECKS.map((c) => (
            <section key={c.id} className="mt-10 border-t border-ls-line pt-8" aria-labelledby={`${c.id}-h`}>
              <h2 id={`${c.id}-h`} className="ls-h3 text-ls-ink">
                {c.title}
              </h2>
              <p className="mt-3 max-w-[60ch] text-ls-ink-2">{c.text}</p>
            </section>
          ))}

          <div className="mt-12 flex gap-3 rounded-ls-panel border border-ls-line bg-ls-surface p-5 sm:p-6">
            <ShieldIcon className="mt-0.5 h-5 w-5 shrink-0 text-brick" />
            <div>
              <p className="ls-h4 text-ls-ink">Never pay before you have inspected the property and met the agent.</p>
              <p className="mt-1 text-ls-ink-2">
                If anyone asks for money first, use &quot;Report this listing&quot; on the listing page. Reported listings are hidden while we look.
              </p>
            </div>
          </div>

          <div className="mt-10 flex flex-wrap items-center gap-4">
            <Link href="/rent" className={lsButton("app")}>
              Find a home
            </Link>
            <Link href="/for-agents" className={`${lsLink} inline-flex min-h-11 items-center`}>
              Agents: how to get verified
            </Link>
          </div>
        </div>
      </div>
      <FinalCta
        title="Agents: get the green seal on every listing."
        text="Create a free studio, upload your ID and papers once, and a moderator checks them by hand."
        action={{ href: "/studio/signup", label: "Start PropertyLink free" }}
      />
    </SitePage>
  );
}
