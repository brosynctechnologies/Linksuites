import type { Metadata } from "next";
import Link from "next/link";
import { SiteFooter, SiteHeader } from "@/components/site-chrome";
import { buttonClass, cx } from "@/components/styles";
import { SCHOOL_PLANS } from "@/lib/config";
import { naira } from "@/lib/format";

export const metadata: Metadata = { title: "Pricing", description: "SchoolLink plans by number of students, paid per term." };

export default function PricingPage() {
  return (
    <div className="flex min-h-screen flex-col">
      <SiteHeader />
      <main className="mx-auto w-full max-w-6xl px-4 py-10 sm:px-6 sm:py-14">
        <h1 className="slab text-3xl font-bold text-ink">Pricing</h1>
        <p className="mt-2 max-w-prose text-slate-700">
          Priced by how many students you have, paid per term. Fees parents pay go straight to your school&apos;s own Paystack account; we never take a
          cut.
        </p>

        <div className="mt-8 grid gap-4 md:grid-cols-3">
          {SCHOOL_PLANS.map((p) => (
            <section
              key={p.code}
              aria-labelledby={`plan-${p.code}`}
              className={cx("flex flex-col rounded-panel border bg-surface p-5", p.code === "standard" ? "border-blazer" : "border-rule")}
            >
              <h2 id={`plan-${p.code}`} className="slab text-xl font-bold text-ink">
                {p.name}
              </h2>
              <p className="mt-2">
                <span className="slab tnum text-3xl font-bold text-ink">{p.priceMinor === 0 ? "₦0" : naira(p.priceMinor)}</span>
                <span className="text-sm text-muted"> per term</span>
              </p>
              <ul className="mt-4 space-y-2 text-sm text-slate-700">
                <li>Up to {p.students.toLocaleString("en-NG")} students</li>
                <li>Up to {p.staff} staff</li>
                <li>{p.onlineFees ? "Parents pay fees online" : "Record cash and transfer payments"}</li>
                <li>{p.branding ? "SchoolLink name on report cards" : "Your school's name only on report cards"}</li>
              </ul>
              <Link href="/signup" className={buttonClass(p.code === "standard" ? "primary" : "secondary", "mt-6 w-full")}>
                {p.priceMinor === 0 ? "Start free" : `Start on ${p.name}`}
              </Link>
            </section>
          ))}
        </div>

        <p className="mt-6 text-sm text-slate-700">
          More than 1,000 students, or several branches?{" "}
          <Link href="/demo" className="font-bold text-blazer underline-offset-2 hover:underline">
            Book a demo
          </Link>{" "}
          and we&apos;ll set up an enterprise plan.
        </p>
        <p className="mt-2 text-sm text-muted">Every school starts free. Reaching a limit only stops new students or staff; your records are never blocked.</p>
      </main>
      <SiteFooter />
    </div>
  );
}
