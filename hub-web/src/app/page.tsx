import type { Metadata } from "next";
import { AppBand } from "@/components/hub/app-band";
import { BusinessPicker } from "@/components/hub/business-picker";
import { FeatureList, Faq, FinalCta, JsonLd, PlansBlock, Section, StepList } from "@/components/suite/sections";
import { SuiteFooter } from "@/components/suite/suite-footer";
import { SuiteHeader } from "@/components/suite/suite-header";
import { adminUrl, appStyle, products } from "@/lib/products";
import { CONTACT_EMAIL, SITE_DESCRIPTION, SITE_TITLE, hubJsonLd } from "@/lib/site";

export const metadata: Metadata = {
  title: { absolute: SITE_TITLE },
  description: SITE_DESCRIPTION,
  alternates: { canonical: "/" },
};

const SHARED = [
  {
    title: "One sign-in for every app",
    text: "Use the same email and password in each LinkSuite app. Add a second app when your business needs it, without a new account.",
  },
  {
    title: "Paid in naira, through Paystack",
    text: "Customers pay by card or bank transfer. Apps that take payments for you connect to your own Paystack account, so the money goes to you.",
  },
  {
    title: "Made for the phone in your hand",
    text: "Every app works in the browser on a mid-range Android phone, even on slow data. Your customers don't install anything.",
  },
  {
    title: "Your customers stay yours",
    text: "Your customer list and records belong to your business. We never sell them, and pupils' and patients' records are only open to the staff who need them.",
  },
];

const STEPS = [
  { title: "Pick your kind of business", text: "Each app is built for one kind of business, in that trade's own words." },
  { title: "Start free", text: "Sign up with your email, add your business name, and share your link the same day. No card needed." },
  { title: "Upgrade inside the app as you grow", text: "When you need more, choose a plan inside the app. You see its price in naira before you pay." },
];

const FAQ = [
  {
    q: "Is it really free?",
    a: "Yes. Every app has a free plan with no time limit and no card needed. Free plans have limits, such as 50 bookings a month in BookLink or 50 students in SchoolLink, and show a small note with the app's name to your customers.",
  },
  {
    q: "How much do paid plans cost?",
    a: "Each app shows its plans and prices in naira inside the app, before you pay. You only pay for the app you upgrade, and only when you choose to.",
  },
  {
    q: "Can I use more than one app?",
    a: "Yes. Sign in to each one with the same email and password. A salon might use BookLink for appointments and InvoiceLink for corporate clients. Each app keeps its own plan.",
  },
  {
    q: "How do my customers pay me?",
    a: "Apps that take payments connect to your own Paystack account, so card and transfer payments go to you. LearnLink is the exception: it collects every course sale, from your academy page or the marketplace, and pays you out from your earnings page.",
  },
  {
    q: "Do I need a computer?",
    a: "No. Everything works on a phone, in the browser. A computer helps for big jobs like importing a school's students or a clinic's price list from Excel.",
  },
  {
    q: "Who can see pupils' and patients' records?",
    a: "Only the staff whose role needs them. In CareLink the front desk and the cashier never see clinical notes, and every opening of a record is logged. In SchoolLink, parents see only their own children.",
  },
];

export default function Home() {
  return (
    <>
      <JsonLd data={hubJsonLd()} />
      <SuiteHeader
        nav={[
          { href: "#apps", label: "Apps" },
          { href: "#plans", label: "Plans" },
          { href: "#faq", label: "Questions", wide: true },
          { href: "#sign-in-list", label: "Sign in" },
        ]}
      />

      <main id="main" tabIndex={-1} className="flex-1 outline-none">
        {/* Hero: the headline, then straight into the picker. The picker is the page's main action. */}
        <section aria-labelledby="hero-title" className="ls-container pt-10 pb-14 sm:pt-14 md:pb-20 lg:pt-20">
          <div className="grid gap-10 lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)] lg:gap-14">
            <div className="lg:pt-2">
              <h1 id="hero-title" className="ls-h1 max-w-[11ch] text-ls-ink">
                Run your business on one account.
              </h1>
              <p className="ls-lead mt-5 max-w-[30rem] text-ls-ink-2">
                Seven apps for Nigerian small businesses: orders, invoices, bookings, courses, property, schools and clinics. Tell us what you
                run and start free.
              </p>
              <ul className="ls-small mt-6 space-y-1.5 text-ls-ink-2">
                <li>Free to start, no card needed</li>
                <li>Works on any phone, even on slow data</li>
                <li>Payments in naira, through Paystack</li>
              </ul>
            </div>
            <section id="pick" aria-labelledby="pick-title" className="scroll-mt-4">
              <BusinessPicker />
            </section>
          </div>
        </section>

        <Section
          id="apps"
          title="Seven apps, one for each kind of business"
          intro="Each app has its own site and its own free sign-up. Start with the one that runs your main job; your sign-in works in the others too."
        >
          <div className="mt-6 border-b border-ls-line">
            {products.map((p) => (
              <AppBand key={p.key} product={p} />
            ))}
          </div>
        </Section>

        <Section id="shared" tone="surface" title="What every app gives you">
          <FeatureList items={SHARED} />
        </Section>

        <Section id="how" title="How to start">
          <StepList steps={STEPS} />
        </Section>

        <Section
          id="plans"
          tone="concrete"
          title="Free to start. Upgrade inside the app as you grow."
          intro="Every app has a free plan for getting started. Paid plans raise the limits and add tools for a bigger team. You choose a plan inside the app, and see its price in naira before you pay."
        >
          <PlansBlock
            appName="each app"
            free={["The app's main job, ready the day you sign up", "Your own link or page to share with customers", "No card and no time limit"]}
            paid={["Higher limits: more products, invoices, bookings, students or beds", "More staff logins", "No app branding on what your customers see"]}
          />
          <h3 className="ls-h3 mt-14 text-ls-ink">What&apos;s free in each app</h3>
          <ul className="mt-4 divide-y divide-ls-line border-y border-ls-line">
            {products.map((p) => (
              <li key={p.key} style={appStyle(p)} className="grid gap-3 py-6 md:grid-cols-[11rem_minmax(0,1fr)_minmax(0,1fr)] md:gap-8">
                <p className="flex items-center gap-2.5">
                  <span aria-hidden="true" className="h-3 w-3 rounded-[2px] bg-app" />
                  <a href={`#${p.key}`} className="font-ls-display text-xl font-extrabold text-ls-ink hover:underline">
                    {p.name}
                  </a>
                </p>
                <div>
                  <p className="ls-label text-ls-muted">Free plan</p>
                  <p className="mt-1 text-ls-ink">{p.free.join(". ")}.</p>
                </div>
                <div>
                  <p className="ls-label text-ls-muted">Paid plans add</p>
                  <p className="mt-1 text-ls-ink">{p.paid.join(". ")}.</p>
                </div>
              </li>
            ))}
          </ul>
        </Section>

        <Section id="faq" title="Questions owners ask">
          <Faq items={FAQ} />
        </Section>

        <FinalCta
          title="Tell us what you run. Start free today."
          text="Pick your kind of business and you'll be on the right app's free sign-up in two taps."
          action={{ href: "#pick", label: "Find your app" }}
        />
      </main>

      <SuiteFooter
        links={[
          { href: "#plans", label: "Plans" },
          { href: "#faq", label: "Questions" },
          ...(CONTACT_EMAIL ? [{ href: `mailto:${CONTACT_EMAIL}`, label: "Email us" }] : []),
          { href: `${adminUrl}/login`, label: "Platform team sign in" },
        ]}
      />
    </>
  );
}
