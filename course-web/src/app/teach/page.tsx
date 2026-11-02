import type { Metadata } from "next";
import Link from "next/link";
import { LearningPath } from "@/components/learning-path";
import { SitePage } from "@/components/site-chrome";
import { buttonClass, cx } from "@/components/styles";
import { CheckIcon } from "@/components/icons";
import { APP_NAME, MARKETPLACE_FEE_PCT, MIN_PAYOUT_MINOR, REFUND_WINDOW_DAYS, TUTOR_PLANS } from "@/lib/config";
import { formatMoney } from "@/lib/money";

export const metadata: Metadata = {
  title: "Teach on LearnLink",
  description: "Open your own academy, sell courses in naira to the people who already follow you, and reach new learners on the marketplace.",
};

const EXAMPLE_PRICE = 2_000_000; // ₦20,000

const SAMPLE_PATH = [
  {
    id: "s1",
    title: "Mechanics",
    lessons: [
      { id: "l1", title: "Scalars, vectors and how JAMB asks about them", type: "Video" as const, durationSec: 840, done: true },
      { id: "l2", title: "Equations of motion, worked past questions", type: "Video" as const, durationSec: 1260, done: true },
      { id: "l3", title: "Mechanics checkpoint", type: "Quiz" as const, isQuiz: true, passed: true },
    ],
  },
  {
    id: "s2",
    title: "Waves and optics",
    lessons: [
      { id: "l4", title: "Reflection and refraction in one diagram", type: "Video" as const, durationSec: 960 },
      { id: "l5", title: "Formula sheet", type: "File" as const },
    ],
  },
];

export default function TeachPage() {
  return (
    <SitePage hideSearch>
      {/* Hero */}
      <section className="border-b border-line bg-surface">
        <div className="mx-auto grid max-w-6xl gap-10 px-4 py-10 sm:px-6 sm:py-16 lg:grid-cols-[minmax(0,1fr)_24rem] lg:items-center">
          <div>
            <h1 className="max-w-[16ch] font-serif text-3xl font-semibold tracking-[-0.02em] text-ink sm:text-4xl">Teach the people who already follow you.</h1>
            <p className="mt-4 max-w-xl text-lg text-slate-700">
              Turn your YouTube lessons, Instagram tips or Telegram class into a course people pay for in naira. You get your own academy page,
              and the {APP_NAME} marketplace brings you learners you haven&apos;t met yet.
            </p>
            <div className="mt-7 flex flex-wrap gap-3">
              <Link href="/teach/signup" className={buttonClass("primary", "px-6")}>
                Create your academy
              </Link>
              <Link href="/teach/login" className={buttonClass("secondary", "px-6")}>
                Log in to the studio
              </Link>
            </div>
            <p className="mt-3 text-sm text-muted">Free to start. No card needed.</p>
          </div>
          <figure className="rounded-panel border border-line bg-mist p-5">
            <figcaption className="mb-4 text-sm text-muted">What a learner sees in your course: progress, always visible.</figcaption>
            <LearningPath sections={SAMPLE_PATH} currentId="l4" certificate={{ earned: false, label: "Certificate of completion" }} />
          </figure>
        </div>
      </section>

      {/* Two ways to sell */}
      <section className="mx-auto max-w-6xl px-4 py-12 sm:px-6" aria-labelledby="two-ways">
        <h2 id="two-ways" className="font-serif text-2xl font-semibold tracking-[-0.01em] text-ink">
          One course, two ways to sell it
        </h2>
        <div className="mt-6 grid overflow-hidden rounded-panel border border-line bg-surface md:grid-cols-2">
          <div className="p-6">
            <h3 className="text-lg font-semibold text-ink">Your academy</h3>
            <p className="mt-1 text-sm text-muted">For the audience you already have.</p>
            <ul className="mt-4 space-y-2.5 text-[0.9375rem] text-slate-700">
              {[
                "A page at your own link, in your brand colour, with your logo",
                "Share it from your bio, WhatsApp status or Telegram channel",
                "No fee on academy sales on Pro and Business (10% on Free)",
                "Coupons for your community, certificates for your graduates",
              ].map((t) => (
                <li key={t} className="flex gap-2.5">
                  <CheckIcon className="mt-1 h-4 w-4 shrink-0 text-lagoon-text" />
                  {t}
                </li>
              ))}
            </ul>
          </div>
          <div className="border-t border-line p-6 md:border-t-0 md:border-l">
            <h3 className="text-lg font-semibold text-ink">The marketplace</h3>
            <p className="mt-1 text-sm text-muted">For learners searching for what you teach.</p>
            <ul className="mt-4 space-y-2.5 text-[0.9375rem] text-slate-700">
              {[
                "Listed in the shared catalogue once our team approves the course",
                "Found by search, category and ratings",
                `A ${MARKETPLACE_FEE_PCT}% fee on marketplace sales, because we found the learner`,
                "You choose: listing is optional, course by course",
              ].map((t) => (
                <li key={t} className="flex gap-2.5">
                  <CheckIcon className="mt-1 h-4 w-4 shrink-0 text-lagoon-text" />
                  {t}
                </li>
              ))}
            </ul>
          </div>
        </div>
      </section>

      {/* How money works */}
      <section className="border-y border-line bg-surface" aria-labelledby="money">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6">
          <h2 id="money" className="font-serif text-2xl font-semibold tracking-[-0.01em] text-ink">
            How the money works
          </h2>
          <ol className="mt-6 grid gap-6 md:grid-cols-4">
            {[
              { t: "A learner pays", d: "Card, bank transfer or USSD through Paystack, in naira. The payment goes to the platform account." },
              { t: "The fee comes off", d: `Academy sales: 10% on Free, nothing on Pro or Business. Marketplace sales: ${MARKETPLACE_FEE_PCT}%.` },
              { t: `${REFUND_WINDOW_DAYS} days to clear`, d: `Earnings are pending for ${REFUND_WINDOW_DAYS} days, the refund window. Then they're available.` },
              { t: "You get paid", d: `Request a payout to your bank account from ${formatMoney(MIN_PAYOUT_MINOR)}. We pay it and mark it done.` },
            ].map((s, i) => (
              <li key={s.t} className="border-t-2 border-lagoon pt-4">
                <p className="tnum text-sm font-semibold text-lagoon-text">Step {i + 1}</p>
                <h3 className="mt-1 font-semibold text-ink">{s.t}</h3>
                <p className="mt-1.5 text-sm text-slate-700">{s.d}</p>
              </li>
            ))}
          </ol>

          <div className="mt-10 overflow-hidden rounded-panel border border-line">
            <table className="w-full text-left text-sm">
              <caption className="bg-slate-50 px-4 py-3 text-left font-semibold text-ink">
                You sell a {formatMoney(EXAMPLE_PRICE)} course. You keep:
              </caption>
              <thead className="border-y border-line bg-slate-50 text-xs text-muted">
                <tr>
                  <th scope="col" className="px-4 py-2 font-medium">
                    Where the learner bought
                  </th>
                  <th scope="col" className="px-4 py-2 font-medium">
                    Fee
                  </th>
                  <th scope="col" className="px-4 py-2 text-right font-medium">
                    You earn
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-line-soft">
                {[
                  { where: "Your academy, Pro or Business plan", pct: 0 },
                  { where: "Your academy, Free plan", pct: 10 },
                  { where: "The marketplace, any plan", pct: MARKETPLACE_FEE_PCT },
                ].map((r) => (
                  <tr key={r.where}>
                    <td className="px-4 py-3 text-slate-700">{r.where}</td>
                    <td className="tnum px-4 py-3 text-slate-700">{r.pct}%</td>
                    <td className="tnum px-4 py-3 text-right font-semibold text-ink">{formatMoney(Math.round((EXAMPLE_PRICE * (100 - r.pct)) / 100))}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </section>

      {/* Pricing */}
      <section className="mx-auto max-w-6xl px-4 py-12 sm:px-6" aria-labelledby="plans">
        <h2 id="plans" className="font-serif text-2xl font-semibold tracking-[-0.01em] text-ink">
          Plans for tutors
        </h2>
        <p className="mt-1 text-slate-700">Monthly, in naira. Marketplace sales carry the {MARKETPLACE_FEE_PCT}% marketplace fee on every plan.</p>
        <div className="mt-6 grid overflow-hidden rounded-panel border border-line bg-surface lg:grid-cols-3">
          {TUTOR_PLANS.map((plan, i) => (
            <section key={plan.code} aria-label={plan.name} className={cx("flex flex-col p-6", i > 0 && "border-t border-line lg:border-t-0 lg:border-l")}>
              <h3 className="text-lg font-semibold text-ink">{plan.name}</h3>
              <p className="mt-3 flex items-baseline gap-1">
                <span className="tnum text-[2.25rem] leading-none font-bold tracking-[-0.02em] text-ink">{plan.priceMinor === 0 ? "₦0" : formatMoney(plan.priceMinor)}</span>
                <span className="text-sm text-muted">/month</span>
              </p>
              <ul className="mt-5 flex-1 space-y-2 text-sm text-slate-700">
                {[plan.courses, plan.storage, plan.academyFee, ...plan.extras].map((f) => (
                  <li key={f} className="flex gap-2.5">
                    <CheckIcon className="mt-0.5 h-4 w-4 shrink-0 text-lagoon-text" />
                    {f}
                  </li>
                ))}
              </ul>
              <Link href="/teach/signup" className={buttonClass(i === 0 ? "primary" : "secondary", "mt-6 w-full")}>
                {i === 0 ? "Start free" : `Start free, upgrade to ${plan.name}`}
              </Link>
            </section>
          ))}
        </div>
      </section>
    </SitePage>
  );
}
