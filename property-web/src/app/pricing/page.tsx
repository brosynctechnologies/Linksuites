import type { Metadata } from "next";
import Link from "next/link";
import { AGENCY_PLANS, STAY_COMMISSION_PCT } from "@/lib/config";
import { formatMoney } from "@/lib/money";
import { CheckIcon, CloseIcon } from "@/components/icons";
import { SitePage } from "@/components/site-chrome";
import { buttonClass, cx } from "@/components/styles";

export const metadata: Metadata = {
  title: "Pricing for agents and landlords",
  description: "PropertyLink plans for agencies, agents and landlords: Free, Pro and Agency, plus Featured and Top boosts.",
};

export default function PricingPage() {
  return (
    <SitePage>
      <div className="mx-auto max-w-5xl px-4 py-10 sm:px-6 sm:py-14">
        <h1 className="display-wide text-3xl font-bold text-asphalt">Plans for agents and landlords</h1>
        <p className="mt-3 max-w-2xl text-lg text-slate-700">
          Start free. Upgrade when you need more live listings, longer listing time, view and lead stats or more team seats. Prices are per month.
        </p>

        <div className="mt-8 grid overflow-hidden rounded-panel border border-line bg-surface lg:grid-cols-3">
          {AGENCY_PLANS.map((p, i) => (
            <section key={p.code} aria-label={p.name} className={cx("flex flex-col p-6", i > 0 && "border-t border-line lg:border-t-0 lg:border-l")}>
              <h2 className="text-lg font-semibold text-asphalt">{p.name}</h2>
              <p className="mt-2 flex items-baseline gap-1">
                <span className="price text-[2.25rem] leading-none text-asphalt">{p.priceMinor === 0 ? "₦0" : formatMoney(p.priceMinor)}</span>
                <span className="text-sm text-muted">/month</span>
              </p>
              <ul className="mt-5 flex-1 space-y-2 text-sm text-slate-700">
                {[p.listings, p.expiry, p.seats].map((f) => (
                  <li key={f} className="flex gap-2.5">
                    <CheckIcon className="mt-0.5 h-4 w-4 shrink-0 text-adire" />
                    {f}
                  </li>
                ))}
                <li className="flex gap-2.5">
                  {p.stats ? <CheckIcon className="mt-0.5 h-4 w-4 shrink-0 text-adire" /> : <CloseIcon className="mt-0.5 h-4 w-4 shrink-0 text-faint" />}
                  <span className={p.stats ? undefined : "text-muted"}>Views and lead stats</span>
                </li>
              </ul>
              <Link href="/studio/signup" className={buttonClass(i === 0 ? "secondary" : "primary", "mt-6 w-full")}>
                {i === 0 ? "Start free" : `Start free, upgrade to ${p.name}`}
              </Link>
            </section>
          ))}
        </div>
        <p className="mt-3 text-sm text-muted">Your listing limit counts live listings, those waiting for review and those hidden after reports.</p>

        <section aria-labelledby="boosts-h" className="mt-12">
          <h2 id="boosts-h" className="display-wide text-xl font-bold text-asphalt">
            Boosts
          </h2>
          <p className="mt-2 max-w-2xl text-slate-700">Pay once to give a live listing more reach for a set number of days. Boosts work on every plan.</p>
          <dl className="mt-4 grid gap-3 sm:grid-cols-2">
            <div className="rounded-panel border border-line bg-surface p-5">
              <dt className="font-semibold text-asphalt">Featured</dt>
              <dd className="mt-1 text-sm text-slate-700">An Adire &quot;Featured&quot; tag on the card. Featured listings appear first on the home page, from every city, taking turns when there are many.</dd>
            </div>
            <div className="rounded-panel border border-line bg-surface p-5">
              <dt className="font-semibold text-asphalt">Top of search</dt>
              <dd className="mt-1 text-sm text-slate-700">Your listing sits above others in recommended search results for its area.</dd>
            </div>
          </dl>
          <p className="mt-3 text-sm text-muted">Boost lengths and prices are shown in the studio when you boost a listing.</p>
        </section>

        <section aria-labelledby="stays-h" className="mt-12">
          <h2 id="stays-h" className="display-wide text-xl font-bold text-asphalt">
            Short-lets
          </h2>
          <p className="mt-2 max-w-2xl text-slate-700">
            Guests pay online when they book. You keep the total less a {STAY_COMMISSION_PCT}% commission, available to pay out from the day after check-in (midnight, Lagos time, at the end of the check-in day).
          </p>
        </section>
      </div>
    </SitePage>
  );
}
