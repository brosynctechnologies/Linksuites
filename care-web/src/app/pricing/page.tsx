import type { Metadata } from "next";
import Link from "next/link";
import { SiteFooter, SiteHeader } from "@/components/site-chrome";
import { buttonClass, cx } from "@/components/styles";
import { CARE_PLANS } from "@/lib/config";
import { naira } from "@/lib/format";

export const metadata: Metadata = { title: "Pricing", description: "CareLink plans by clinic size, paid monthly." };

export default function PricingPage() {
  return (
    <div className="flex min-h-screen flex-col">
      <SiteHeader />
      <main className="mx-auto w-full max-w-6xl px-4 py-10 sm:px-6 sm:py-14">
        <h1 className="display text-3xl font-bold text-ink">Pricing</h1>
        <p className="mt-2 max-w-prose text-slate-700">
          Priced by the size of your clinic, paid monthly. Patients&apos; online payments go straight to your clinic&apos;s own Paystack account;
          we never take a cut.
        </p>

        <div className="mt-8 grid gap-4 md:grid-cols-3">
          {CARE_PLANS.map((p) => (
            <section
              key={p.code}
              aria-labelledby={`plan-${p.code}`}
              className={cx("flex flex-col rounded-panel border bg-surface p-5", p.code === "clinic" ? "border-2 border-scrub" : "border-rule")}
            >
              <h2 id={`plan-${p.code}`} className="display text-xl font-bold text-ink">
                {p.name}
              </h2>
              <p className="mt-2">
                <span className="display tnum text-3xl font-bold text-ink">{p.priceMinor === 0 ? "₦0" : naira(p.priceMinor)}</span>
                <span className="text-sm text-muted"> a month</span>
              </p>
              <ul className="mt-4 space-y-2 text-sm text-slate-700">
                <li>Up to {p.staff} staff logins</li>
                <li>{p.beds === 0 ? "Outpatients only, no beds" : `Up to ${p.beds} beds`}</li>
                <li>{p.onlinePayments ? "Patients pay online with your Paystack" : "Record cash, POS and transfer payments"}</li>
                <li>{p.branding ? "CareLink name on receipts and the portal" : "Your clinic's name only"}</li>
              </ul>
              <Link href="/signup" className={buttonClass(p.code === "clinic" ? "primary" : "secondary", "mt-6 w-full")}>
                {p.priceMinor === 0 ? "Start free" : `Start on ${p.name}`}
              </Link>
            </section>
          ))}
        </div>

        <p className="mt-6 text-sm text-muted">
          Every clinic starts free and upgrades from Settings. Reaching a limit only stops new staff or beds; your records are never blocked.
        </p>
      </main>
      <SiteFooter />
    </div>
  );
}
