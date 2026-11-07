import type { Metadata } from "next";
import Link from "next/link";
import { InvoicePreview, type PreviewDoc } from "@/components/invoice-preview";
import { ActivityCrop, PaperTrail, PayNowCrop, ProfitCrop, QuoteCrop } from "@/components/site/screens";
import { FeatureList, Faq, FinalCta, JsonLd, PlansBlock, Section, StepList } from "@/components/suite/sections";
import { SuiteFooter } from "@/components/suite/suite-footer";
import { SuiteHeader } from "@/components/suite/suite-header";
import { cx, lsButton, lsLink } from "@/components/suite/ui";
import { baseOpenGraph, baseTwitter } from "@/lib/site";
import { appHref, appJsonLd, productByKey } from "@/lib/suite";
import { TEMPLATES } from "@/lib/templates";

const app = productByKey("invoices");
const booklink = productByKey("bookings");

const TITLE = "InvoiceLink: invoice app for Nigerian freelancers and businesses";
const DESCRIPTION =
  "Make invoices and quotes, send them on WhatsApp or by email, and let clients pay by card or transfer. For Nigerian freelancers and agencies. Free to start.";

export const metadata: Metadata = {
  title: { absolute: TITLE },
  description: DESCRIPTION,
  alternates: { canonical: "/" },
  openGraph: { ...baseOpenGraph, url: "/", title: TITLE, description: DESCRIPTION },
  twitter: { ...baseTwitter, title: TITLE, description: DESCRIPTION },
};

const TRADES = [
  "Designers, developers, writers and photographers",
  "Agencies and studios",
  "Consultants, accountants and lawyers",
  "Event planners, decorators and caterers",
  "Builders, technicians and repair workshops",
  "Tutors and trainers who bill each month",
];

const STEPS = [
  {
    title: "Fill in the invoice",
    text: "Add your client and what you did. VAT, discounts and the total add up as you type, in naira or the currency you bill in.",
  },
  {
    title: "Send it on WhatsApp or by email",
    text: "Your client gets a secure link to view, download or pay it. You see when they open it.",
  },
  {
    title: "Let the reminders do the asking",
    text: "If it isn't paid, a polite reminder goes out on the days you choose, before and after the due date.",
  },
  {
    title: "Get paid, and the receipt goes out",
    text: "The invoice is marked paid and your client gets a receipt by email. Your list shows who still owes you.",
  },
];

const FEATURES = [
  {
    title: "A Pay now button on the invoice",
    text: "Connect your own Paystack account and clients pay by card or bank transfer from the invoice. The money goes to you, not through us.",
    visual: <PayNowCrop />,
  },
  {
    title: "Reminders that go out on their own",
    text: "Each reminder carries the invoice link and its Pay now button. You see every send, view, reminder and payment on the invoice.",
    visual: <ActivityCrop />,
  },
  {
    title: "Quotes your client can accept",
    text: "Send a quote the same way. Your client accepts or declines it from the link, you hear straight away, and it becomes an invoice in one step.",
    visual: <QuoteCrop />,
  },
  {
    title: "Know what you made this month",
    text: "Log expenses with a photo of the receipt. The profit report shows income, expenses and profit by month, with a VAT summary for your accountant.",
    visual: <ProfitCrop />,
  },
];

const FAQ = [
  {
    q: "Can I make an invoice without an account?",
    a: (
      <>
        Yes. The{" "}
        <Link href="/create" className={lsLink}>
          free invoice generator
        </Link>{" "}
        makes a PDF invoice in your browser with no sign-up. Create a free account when you want to send it, track it and see who still owes
        you; your draft comes with you.
      </>
    ),
  },
  {
    q: "Is it really free?",
    a: "Yes. The free plan has no time limit and needs no card. It includes 10 invoices a month, unlimited quotes and clients, and 3 templates. Invoices on the free plan carry a small “Made with InvoiceLink” line.",
  },
  {
    q: "How much do paid plans cost?",
    a: "Each plan and its price is shown in naira inside InvoiceLink, under Billing, before you pay. You only pay when you choose to upgrade, and you can stay on the free plan as long as it fits.",
  },
  {
    q: "How do my clients pay me?",
    a: "On a paid plan, connect your own Paystack account and every invoice gets a Pay now button for card and bank transfer. The money goes to your Paystack account. On any plan you can print your bank details on the invoice and record a transfer yourself.",
  },
  {
    q: "Can I bill in dollars or pounds?",
    a: "Yes. Bill in naira, US dollars, pounds, euros, cedis, shillings or rand. Each invoice keeps its own currency, with an exchange rate filled in that you can change, so your reports still add up in your main currency.",
  },
  {
    q: "Can my accountant or team use it?",
    a: "Yes, on the Business plan. Invite staff and admins, and give your accountant read-only access to everything, including the profit report and CSV exports.",
  },
];

/** The sample the template strip is drawn from: Ada Designs' logo job for Bakare Foods. A business's own figures. */
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

const TEMPLATE_COLOURS: Record<string, string> = {
  classic: "#2b47c9",
  modern: "#0f766e",
  minimal: "#1e2430",
  bold: "#c2410c",
  elegant: "#6d28d9",
};

export default function Landing() {
  return (
    <>
      <JsonLd data={{ "@context": "https://schema.org", ...appJsonLd(app) }} />
      <SuiteHeader
        app={app}
        nav={[
          { href: "/create", label: "Invoice generator", wide: true },
          { href: "/#plans", label: "Plans", wide: true },
          { href: "/login", label: "Log in" },
        ]}
        action={{ href: app.startPath, label: "Start free" }}
      />

      <main id="main" tabIndex={-1} className="flex-1 outline-none">
        <section aria-labelledby="hero-title" className="ls-container pt-10 pb-14 sm:pt-14 md:pb-20 lg:pt-20">
          <div className="grid gap-10 lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)] lg:items-center lg:gap-14">
            <div>
              <h1 id="hero-title" className="ls-h1 max-w-[12ch] text-ls-ink">
                Get paid for every invoice.
              </h1>
              <p className="ls-lead mt-5 max-w-[32rem] text-ls-ink-2">
                Send invoices and quotes your clients take seriously, on WhatsApp or by email. They can pay by card or transfer from the
                invoice, and reminders follow up for you.
              </p>
              <div className="mt-7 flex flex-wrap items-center gap-3">
                <Link href={app.startPath} className={lsButton("app")}>
                  Start InvoiceLink free
                </Link>
                <Link href="/create" className={lsButton("secondary")}>
                  Make an invoice now
                </Link>
              </div>
              <ul className="ls-small mt-6 space-y-1.5 text-ls-ink-2">
                <li>Free to start, no card needed</li>
                <li>Download a PDF invoice without an account</li>
                <li>Payments in naira, through your own Paystack</li>
              </ul>
            </div>
            <PaperTrail />
          </div>
        </section>

        <Section
          id="who"
          title="For anyone who bills for their work"
          intro="If you send a bill after the job, or a quote before it, InvoiceLink is built for you."
        >
          <ul className="mt-8 grid gap-x-10 gap-y-3 text-ls-ink sm:grid-cols-2 lg:grid-cols-3">
            {TRADES.map((t) => (
              <li key={t} className="flex gap-3">
                <span aria-hidden="true" className="mt-[0.6rem] h-2 w-2 shrink-0 rounded-[2px] bg-app" />
                {t}
              </li>
            ))}
          </ul>
          <p className="mt-8 max-w-[38rem] text-ls-ink-2">
            Run a salon, barber shop or studio? Take bookings and deposits with{" "}
            <a href={appHref(booklink, booklink.landingPath)} className={lsLink}>
              BookLink
            </a>
            , and send your corporate clients an InvoiceLink invoice. One sign-in works in both.
          </p>
        </Section>

        <Section id="how" title="How it works">
          <StepList steps={STEPS} />
        </Section>

        <Section id="features" tone="surface" title="From sent to paid, without the chasing">
          <FeatureList items={FEATURES} />
        </Section>

        <Section
          id="templates"
          title="Five invoice templates, in your colours"
          intro={
            <>
              Each one carries your logo and brand colour onto the PDF. Three come with the free plan; try them in the{" "}
              <Link href="/create" className={lsLink}>
                free invoice generator
              </Link>
              .
            </>
          }
        >
          {/* On phones the strip scrolls sideways, so it takes focus and can be scrolled with the arrow keys. */}
          <div
            role="region"
            aria-label="The five templates"
            tabIndex={0}
            className="-mx-4 mt-10 overflow-x-auto px-4 pt-2 pb-6 sm:-mx-6 sm:px-6 lg:mx-0 lg:overflow-visible lg:px-0"
          >
            <ul className="flex snap-x gap-5 lg:grid lg:grid-cols-5">
              {TEMPLATES.map((t) => (
                <li key={t.key} className="w-52 shrink-0 snap-start lg:w-auto">
                  <div aria-hidden="true" className="pointer-events-none aspect-[210/297] overflow-hidden rounded-doc bg-paper shadow-paper">
                    <div className="w-[200%] origin-top-left scale-50 font-sans">
                      <InvoicePreview doc={{ ...SAMPLE, template: t.key, brandColor: TEMPLATE_COLOURS[t.key] }} className="shadow-none" />
                    </div>
                  </div>
                  <p className="mt-4 flex items-center justify-between gap-2">
                    <span className="ls-h4 text-ls-ink">{t.name}</span>
                    <span className={cx("ls-label", t.isPremium ? "text-ls-muted" : "text-app-text")}>
                      {t.isPremium ? "Paid plans" : "Free plan"}
                    </span>
                  </p>
                </li>
              ))}
            </ul>
          </div>
        </Section>

        <Section
          id="plans"
          tone="concrete"
          title="Free to start. Upgrade inside InvoiceLink as you grow."
          intro="The free plan covers your first clients. Paid plans add pay-now links, reminders and recurring invoices, then expenses and a team."
        >
          <PlansBlock appName={app.name} free={app.free} paid={app.paid} start={{ href: app.startPath, label: "Start free" }} />
        </Section>

        <Section id="faq" title="Questions">
          <Faq items={FAQ} />
        </Section>

        <FinalCta
          title="Send your next invoice with InvoiceLink."
          text="Free to start, no card needed. Your first invoice can go out today."
          action={{ href: app.startPath, label: "Start InvoiceLink free" }}
        />
      </main>

      <SuiteFooter
        app={app}
        links={[
          { href: "/#plans", label: "Plans" },
          { href: "/create", label: "Free invoice generator" },
          { href: "/login", label: "Log in" },
          { href: "/signup", label: "Create an account" },
        ]}
      />
    </>
  );
}
