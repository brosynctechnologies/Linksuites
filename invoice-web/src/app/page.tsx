import Link from "next/link";
import { Generator } from "@/components/generator";
import { InvoicePreview, type PreviewDoc } from "@/components/invoice-preview";
import { SiteFooter, SiteHeader } from "@/components/site-header";
import { buttonClass, cx } from "@/components/styles";
import { TEMPLATES } from "@/lib/templates";

const SAMPLE: Omit<PreviewDoc, "template" | "brandColor"> = {
  kind: "invoice",
  number: "INV-0042",
  business: { name: "Ada Designs Ltd", address: "12 Admiralty Way, Lekki, Lagos", email: "hello@adadesigns.ng" },
  client: { name: "Tunde Bakare", company: "Bakare Foods" },
  issueDate: "2026-10-01",
  secondDate: "2026-10-15",
  currency: "NGN",
  lines: [
    { description: "Logo design", quantity: 1, unitPriceMinor: 15000000, discountPercent: 0, taxLabel: "VAT 7.5%", lineTotalMinor: 15000000 },
    { description: "Business cards (box)", quantity: 2, unitPriceMinor: 1250000, discountPercent: 10, lineTotalMinor: 2250000 },
  ],
  subtotalMinor: 17500000,
  discountTotalMinor: 250000,
  taxes: [{ name: "VAT", ratePercent: 7.5, amountMinor: 1125000 }],
  shippingMinor: 0,
  totalMinor: 18375000,
};

const ACCENTS: Record<string, string> = {
  classic: "#2b47c9",
  modern: "#0f766e",
  minimal: "#1e2430",
  bold: "#c2410c",
  elegant: "#6d28d9",
};

const FEATURES = [
  {
    t: "Send it where your client reads",
    d: "Email a secure link or share it on WhatsApp. You see the moment your client opens it.",
  },
  {
    t: "Let clients pay from the invoice",
    d: "A Pay-now button on the invoice takes payment through your own Paystack account. The money goes straight to you.",
  },
  {
    t: "Stop chasing by hand",
    d: "Reminders go out before and after the due date, and a receipt is emailed when a payment lands.",
  },
  {
    t: "Hand your accountant clean numbers",
    d: "Expenses with receipt photos, a profit report by month and CSV exports, in naira or the currency you bill in.",
  },
];

const PLANS = [
  {
    name: "Free",
    price: "₦0",
    note: "For your first clients",
    features: ["10 invoices a month", "3 templates", "Quotes and clients", "Email and WhatsApp sharing", "A small “Made with InvoiceLink” line"],
    cta: "Start free",
    primary: false,
  },
  {
    name: "Pro",
    price: "₦4,000",
    note: "For a busy freelancer",
    features: [
      "Unlimited invoices",
      "All 5 templates",
      "No InvoiceLink branding",
      "Automatic payment reminders",
      "Recurring invoices for retainers",
      "Pay-now links (Paystack)",
    ],
    cta: "Get Pro",
    primary: true,
  },
  {
    name: "Business",
    price: "₦10,000",
    note: "For a small agency",
    features: ["Everything in Pro", "Expenses with receipt photos", "Profit and tax report", "Up to 10 team seats", "Access for your accountant"],
    cta: "Get Business",
    primary: false,
  },
];

function Check() {
  return (
    <svg viewBox="0 0 20 20" className="mt-[3px] h-4 w-4 shrink-0 text-paid" fill="currentColor" aria-hidden="true">
      <path d="M16.7 5.3a1 1 0 0 1 0 1.4l-8 8a1 1 0 0 1-1.4 0l-4-4a1 1 0 1 1 1.4-1.4L8 12.6l7.3-7.3a1 1 0 0 1 1.4 0z" />
    </svg>
  );
}

const sectionTitle = "font-display text-[1.75rem] leading-tight font-[560] tracking-[-0.01em] text-graphite sm:text-[2.5rem]";

export default function Home() {
  return (
    <div className="flex min-h-screen flex-col bg-desk">
      <SiteHeader />
      <main className="flex-1">
        {/* Hero: the generator itself. The document fills in on the right as you type. */}
        <section id="create" className="scroll-mt-16" aria-labelledby="hero-title">
          <div className="mx-auto max-w-6xl px-4 pt-8 pb-14 sm:px-6 sm:pt-12 lg:pb-20">
            <Generator
              intro={
                <div className="pb-2 lg:pb-4">
                  <h1
                    id="hero-title"
                    className="font-display text-[2.5rem] leading-[1.04] font-[600] tracking-[-0.02em] text-graphite sm:text-[3.5rem]"
                  >
                    An invoice your client takes seriously.
                  </h1>
                  <p className="mt-4 max-w-md text-lg text-slate-600">
                    Fill in the form and the invoice draws itself beside it. Download the PDF free, with no account. Sign up when you want to
                    send it, get paid online and see who still owes you.
                  </p>
                </div>
              }
            />
          </div>
        </section>

        <section id="templates" className="scroll-mt-16 border-t border-rule bg-desk-2/60 py-14 sm:py-20" aria-labelledby="templates-title">
          <div className="mx-auto max-w-6xl px-4 sm:px-6">
            <div className="max-w-xl">
              <h2 id="templates-title" className={sectionTitle}>
                Five templates, all in your colours
              </h2>
              <p className="mt-3 text-slate-600">Three are free and two come with Pro. Each one carries your logo and brand colour onto the PDF.</p>
            </div>
            <div className="no-scrollbar -mx-4 mt-10 flex snap-x gap-5 overflow-x-auto px-4 pt-2 pb-6 sm:-mx-6 sm:px-6 lg:mx-0 lg:grid lg:grid-cols-5 lg:overflow-visible lg:px-0">
              {TEMPLATES.map((t, i) => (
                <figure key={t.key} className="w-52 shrink-0 snap-start lg:w-auto">
                  <div
                    className={cx(
                      "pointer-events-none aspect-[210/297] overflow-hidden rounded-doc bg-paper shadow-paper",
                      i % 2 === 0 ? "lg:-rotate-1" : "lg:rotate-1",
                    )}
                    aria-hidden="true"
                  >
                    <div className="w-[200%] origin-top-left scale-50">
                      <InvoicePreview doc={{ ...SAMPLE, template: t.key, brandColor: ACCENTS[t.key] }} className="shadow-none" />
                    </div>
                  </div>
                  <figcaption className="mt-4 flex items-center justify-between text-sm font-semibold text-graphite">
                    {t.name}
                    <span
                      className={cx(
                        "rounded-full px-2 py-0.5 text-xs font-medium",
                        t.isPremium ? "bg-amber-wash text-amber-text" : "bg-paid-wash text-[#0e5c45]",
                      )}
                    >
                      {t.isPremium ? "Pro" : "Free"}
                    </span>
                  </figcaption>
                </figure>
              ))}
            </div>
          </div>
        </section>

        <section className="border-t border-rule bg-paper py-14 sm:py-20" aria-labelledby="features-title">
          <div className="mx-auto max-w-6xl px-4 sm:px-6">
            <h2 id="features-title" className={cx(sectionTitle, "max-w-xl")}>
              From sent to paid without the chasing
            </h2>
            <dl className="mt-10 grid border-t border-rule sm:grid-cols-2">
              {FEATURES.map((f, i) => (
                <div
                  key={f.t}
                  className={cx("border-b border-rule py-6 sm:py-8", i % 2 === 0 ? "sm:pr-10" : "sm:border-l sm:pl-10")}
                >
                  <dt className="text-lg font-semibold text-graphite">{f.t}</dt>
                  <dd className="mt-2 max-w-md text-slate-600">{f.d}</dd>
                </div>
              ))}
            </dl>
          </div>
        </section>

        <section id="pricing" className="scroll-mt-16 border-t border-rule py-14 sm:py-20" aria-labelledby="pricing-title">
          <div className="mx-auto max-w-6xl px-4 sm:px-6">
            <div className="max-w-xl">
              <h2 id="pricing-title" className={sectionTitle}>
                Pricing in naira, billed monthly
              </h2>
              <p className="mt-3 text-slate-600">Start free. Move up when the invoices pile up or the team grows.</p>
            </div>
            <div className="mt-10 grid overflow-hidden rounded-panel border border-rule bg-paper md:grid-cols-3">
              {PLANS.map((p, i) => (
                <div
                  key={p.name}
                  className={cx(
                    "relative flex flex-col p-6 sm:p-8",
                    i > 0 && "border-t border-rule md:border-t-0 md:border-l",
                    p.primary && "bg-ink-wash/40",
                  )}
                >
                  {p.primary && <span className="absolute inset-x-0 top-0 h-1 bg-ink" aria-hidden="true" />}
                  <h3 className="text-lg font-semibold text-graphite">{p.name}</h3>
                  <p className="text-sm text-muted">{p.note}</p>
                  <p className="mt-5 flex items-baseline gap-1.5">
                    <span className="font-figure amount text-[2.5rem] leading-none text-graphite">{p.price}</span>
                    {p.price !== "₦0" && <span className="text-sm text-muted">a month</span>}
                  </p>
                  <ul className="mt-6 flex-1 space-y-2.5 text-sm text-slate-700">
                    {p.features.map((f) => (
                      <li key={f} className="flex gap-2.5">
                        <Check /> {f}
                      </li>
                    ))}
                  </ul>
                  <Link href="/signup" className={buttonClass(p.primary ? "primary" : "secondary", "mt-8 w-full")}>
                    {p.cta}
                  </Link>
                </div>
              ))}
            </div>
          </div>
        </section>
      </main>
      <SiteFooter />
    </div>
  );
}
