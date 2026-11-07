import type { Metadata } from "next";
import Link from "next/link";
import { CheckoutScreen, DeliveryCrop, OptionsCrop, ShareCrop, StoreScreen, TicketCrop, WhatsAppScreen } from "@/components/site/order-screens";
import { PhoneFrame } from "@/components/suite/phone";
import { FeatureList, Faq, FinalCta, JsonLd, PlansBlock, Section, StepList } from "@/components/suite/sections";
import { SuiteFooter } from "@/components/suite/suite-footer";
import { SuiteHeader } from "@/components/suite/suite-header";
import { lsButton, lsLink } from "@/components/suite/ui";
import { SITE_DESCRIPTION, SITE_TITLE } from "@/lib/config";
import { appHref, appJsonLd, productByKey } from "@/lib/suite";

const app = productByKey("orders");
const invoiceLink = productByKey("invoices");

export const metadata: Metadata = {
  title: { absolute: SITE_TITLE },
  description: SITE_DESCRIPTION,
  alternates: { canonical: "/" },
};

const TRADES = [
  { title: "Food vendors and home kitchens", text: "Jollof and fried rice, small chops, soups by the litre, cakes and chin chin." },
  { title: "Boutiques and fashion sellers", text: "Ankara and ready-to-wear, thrift, shoes and bags, in every size and colour you stock." },
  { title: "Small shops", text: "Provisions, skincare, hair, phone accessories and gadgets." },
];

const STEPS = [
  { title: "Create your store", text: "Sign up with your email and WhatsApp number. Your store link is ready straight away." },
  { title: "Add products and delivery areas", text: "Snap photos, set prices, sizes and colours. Add the areas you deliver to and what each one costs." },
  { title: "Share your link", text: "Put it in your Instagram bio and WhatsApp status. Orders arrive on WhatsApp, written out and added up." },
];

const FEATURES = [
  {
    title: "Every order saved as a ticket",
    text: "Each order is saved in your dashboard before WhatsApp even opens, so nothing gets lost in your chats. Confirm it, mark it paid and mark it delivered.",
    visual: <TicketCrop />,
  },
  {
    title: "Your delivery areas and fees",
    text: "Set a fee for each area you deliver to, or let customers pick up. The total is worked out before the order reaches you.",
    visual: <DeliveryCrop />,
  },
  {
    title: "Sizes, colours and stock",
    text: "Give a product sizes and colours, each with its own price. Options you've sold out of are crossed out, so nobody orders what you don't have.",
    visual: <OptionsCrop />,
  },
  {
    title: "A link and a QR code to share",
    text: "Copy your store link, share it straight to WhatsApp, or print the QR code for flyers, packaging and your shop counter.",
    visual: <ShareCrop />,
  },
];

const FAQ = [
  {
    q: "Do my customers need to download an app or sign up?",
    a: "No. Your store opens in the browser on any phone. Customers pick what they want, choose delivery or pickup, and tap Send order on WhatsApp. They don't need an account.",
  },
  {
    q: "How do customers pay me?",
    a: "The way you already agree with them on WhatsApp: bank transfer, POS or cash on delivery. OrderLink adds up the total for you, and you mark the order paid in your dashboard when the money comes in.",
  },
  {
    q: "What if WhatsApp doesn't open, or the customer never sends the message?",
    a: "The order is saved in your dashboard before WhatsApp opens, so you see it either way. The customer also gets an order page where they can track it and open WhatsApp again.",
  },
  {
    q: "Can I charge different delivery fees for different areas?",
    a: "Yes. Add each area with its own fee and how long delivery takes, or offer pickup only. The free plan has one delivery zone; paid plans let you add as many as you need.",
  },
  {
    q: "How much do paid plans cost?",
    a: "You see each plan and its price in naira inside OrderLink, under Billing, before you pay. You only upgrade when you choose to, and the free plan has no time limit.",
  },
  {
    q: "Can I send an invoice for a big catering order?",
    a: (
      <>
        Yes, with{" "}
        <a href={appHref(invoiceLink, invoiceLink.landingPath)} className={lsLink}>
          InvoiceLink
        </a>
        , another LinkSuite app. Send a quote or invoice for a wedding or office party, and your client pays online. Your OrderLink email and password
        work there too.
      </>
    ),
  },
];

/** The hero: one order's journey on three phones, from the store link to the seller's WhatsApp. */
function OrderJourney() {
  return (
    <div className="mt-10 flex h-[26rem] justify-center gap-6 overflow-hidden rounded-t-ls-panel bg-app-wash px-4 sm:gap-8 lg:mt-14 lg:justify-between lg:px-14">
      <PhoneFrame
        className="ls-paint mt-14 hidden rounded-[2.1rem] [--ls-i:0] lg:block"
        label="Step one, on the customer's phone: Mama Tee's Kitchen's OrderLink store, selling jollof rice, fried rice, Chapman and small chops."
      >
        <StoreScreen />
      </PhoneFrame>
      <PhoneFrame
        className="ls-paint mt-6 hidden rounded-[2.1rem] [--ls-i:1] sm:block"
        label="Step two, the customer's checkout: two large jollof rice and a Chapman, delivered to Lekki, 9,000 naira in total, with a Send order on WhatsApp button."
      >
        <CheckoutScreen />
      </PhoneFrame>
      <PhoneFrame
        className="ls-paint mt-10 rounded-[2.1rem] [--ls-i:2]"
        label="Step three, on the seller's WhatsApp: order 1042 from Ada Okafor arrives written out, with each item, the Lekki delivery fee, the 9,000 naira total and her address."
      >
        <WhatsAppScreen />
      </PhoneFrame>
    </div>
  );
}

export default function Landing() {
  return (
    <>
      <JsonLd data={{ "@context": "https://schema.org", ...appJsonLd(app) }} />
      <SuiteHeader
        app={app}
        nav={[
          { href: "/#plans", label: "Plans", wide: true },
          { href: "/login", label: "Log in" },
        ]}
        action={{ href: app.startPath, label: "Start free" }}
      />

      <main id="main" tabIndex={-1} className="flex-1 outline-none">
        <section aria-labelledby="hero-title" className="ls-container pt-10 sm:pt-14 lg:pt-20">
          <div className="grid gap-6 lg:grid-cols-[minmax(0,7fr)_minmax(0,5fr)] lg:items-end lg:gap-14">
            <h1 id="hero-title" className="ls-h1 max-w-[13ch] text-ls-ink">
              Take orders on WhatsApp from your own store link.
            </h1>
            <div className="lg:pb-2">
              <p className="ls-lead max-w-[34rem] text-ls-ink-2">
                One link for your Instagram bio and WhatsApp status. Customers pick what they want and how to get it, then send you the whole order on
                WhatsApp with the total already added up.
              </p>
              <div className="mt-7 flex flex-wrap gap-3">
                <Link href={app.startPath} className={lsButton("app")}>
                  Create your free store
                </Link>
                <a href="#how" className={lsButton("secondary")}>
                  See how it works
                </a>
              </div>
              <ul className="ls-small mt-5 space-y-1.5 text-ls-ink-2">
                <li>Free to start, no card needed</li>
                <li>Customers don&apos;t need an app or an account</li>
                <li>Works on any phone, even on slow data</li>
              </ul>
            </div>
          </div>
          <OrderJourney />
        </section>

        <Section
          id="who"
          title="Made for people who sell on WhatsApp"
          intro="If you already take orders in your DMs and add up totals on a calculator, OrderLink does the writing and the sums for you."
        >
          <ul className="mt-10 grid gap-x-10 sm:grid-cols-3">
            {TRADES.map((t) => (
              <li key={t.title} className="border-t border-ls-line py-5">
                <h3 className="ls-h4 flex items-center gap-2.5 text-ls-ink">
                  <span aria-hidden="true" className="h-2.5 w-2.5 shrink-0 rounded-[2px] bg-app" />
                  {t.title}
                </h3>
                <p className="mt-1.5 max-w-[34ch] text-ls-ink-2">{t.text}</p>
              </li>
            ))}
          </ul>
        </Section>

        <Section id="how" title="How it works">
          <StepList steps={STEPS} />
        </Section>

        <Section
          id="features"
          tone="surface"
          title="Everything an order needs, before it reaches you"
          intro="Your store runs in your own colours with your logo. Everything else is set up once in your dashboard."
        >
          <FeatureList items={FEATURES} />
        </Section>

        <Section
          id="plans"
          tone="concrete"
          title={`Free to start. Upgrade inside ${app.name} as you grow.`}
          intro="The free plan runs a real store and takes every order that comes in. When you need more products or delivery areas, upgrade inside OrderLink."
        >
          <PlansBlock appName={app.name} free={app.free} paid={app.paid} start={{ href: app.startPath, label: "Create your free store" }} />
        </Section>

        <Section id="faq" title="Questions sellers ask">
          <Faq items={FAQ} />
        </Section>

        <FinalCta
          title="Your next order is one link away."
          text="Create your store, add a few products and share the link today."
          action={{ href: app.startPath, label: "Create your free store" }}
        />
      </main>

      <SuiteFooter
        app={app}
        links={[
          { href: "/#plans", label: "Plans" },
          { href: "/#faq", label: "Questions" },
          { href: "/login", label: "Log in" },
          { href: app.startPath, label: "Create a store" },
        ]}
      />
    </>
  );
}
