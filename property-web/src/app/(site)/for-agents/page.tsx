import type { Metadata } from "next";
import type { ReactNode } from "react";
import Link from "next/link";
import { appHref, appJsonLd, productByKey } from "@/lib/suite";
import { shareMetadata } from "@/lib/site";
import {
  Drawing,
  EnquiriesScreen,
  ListingCardScreen,
  ListingEditorScreen,
  StayScreen,
  ViewingPickerScreen,
  ViewingsScreen,
  WhatsAppScreen,
} from "@/components/site-screens";
import { PhoneFrame } from "@/components/suite/phone";
import { Faq, FeatureList, FinalCta, JsonLd, PlansBlock, Section, StepList } from "@/components/suite/sections";
import { SuiteFooter } from "@/components/suite/suite-footer";
import { SuiteHeader } from "@/components/suite/suite-header";
import { lsButton, lsLink } from "@/components/suite/ui";

const TITLE = "PropertyLink for agents and landlords: list homes in Nigeria free";
const DESCRIPTION =
  "List homes to rent, sell or short-let with every cost shown. Take enquiries on WhatsApp, book viewings into your hours and get the Verified seal. Free to start.";

export const metadata: Metadata = {
  title: { absolute: TITLE },
  description: DESCRIPTION,
  alternates: { canonical: "/for-agents" },
  ...shareMetadata({ title: TITLE, description: DESCRIPTION, url: "/for-agents", image: false }),
};

const app = productByKey("property");
const invoices = productByKey("invoices");

const WHO = [
  { title: "Estate agencies", text: "A firm with several agents, each with their own login, posting under the agency's name." },
  { title: "Solo agents", text: "You work on your own, list your clients' homes and answer your own WhatsApp." },
  { title: "Landlords", text: "You let or sell your own property, with nobody in between." },
  { title: "Short-let hosts", text: "You let a furnished flat by the night, and guests pay online before they arrive." },
];

const STEPS = [
  { title: "Create your studio", text: "Sign up as an agency, a solo agent or a landlord with your email. No card needed." },
  { title: "List a home", text: "Add photos, the rent and every fee. Seekers see them added up into one move-in total." },
  { title: "Get verified", text: "Upload your ID and business papers. Once a moderator checks them, you get the green seal." },
  { title: "Answer and show", text: "Reply to enquiries on WhatsApp, and meet seekers at the viewings they book into your hours." },
];

/** A feature's screen, cropped from the top so it rises out of the stage floor. */
function Crop({ children }: { children: ReactNode }) {
  return <div className="flex h-[12.5rem] w-full justify-center overflow-hidden">{children}</div>;
}

const FEATURES = [
  {
    title: "Every cost on the listing",
    text: "Enter the rent and each fee once: agency, legal, caution, service charge. Seekers see one move-in total, so the people who call you already know what it costs.",
    visual: (
      <Crop>
        <Drawing label="A PropertyLink listing card: a 2 bedroom flat in Yaba at ₦3,500,000 a year, listed by Haven Homes with the Verified seal.">
          <ListingCardScreen />
        </Drawing>
      </Crop>
    ),
  },
  {
    title: "Leads on WhatsApp, calls and an inbox",
    text: "Seekers tap Chat on WhatsApp and the message names your listing, so you know which home they mean. Enquiry forms land in your studio inbox and your email.",
    visual: (
      <Crop>
        <Drawing label="A WhatsApp chat: a seeker asks Ngozi of Haven Homes whether the 2 bedroom flat they saw on PropertyLink is still available, and she offers a Saturday viewing.">
          <WhatsAppScreen />
        </Drawing>
      </Crop>
    ),
  },
  {
    title: "Viewings in your own hours",
    text: "Set the weekly windows when you can show homes. Seekers pick a time inside them, you confirm, and nobody pays an inspection fee.",
    visual: (
      <Crop>
        <Drawing label="Booking a viewing on a PropertyLink listing: Saturday 14 November at 10:30 picked from the agent's free times, with a note that viewings are free.">
          <ViewingPickerScreen />
        </Drawing>
      </Crop>
    ),
  },
  {
    title: "Short-lets paid before check-in",
    text: "Turn on bookings for a furnished flat. Guests pick dates and pay online through Paystack, and your earnings can be paid out from the day after check-in.",
    visual: (
      <Crop>
        <Drawing label="A short-let booking on PropertyLink: 3 nights at ₦85,000 plus ₦15,000 cleaning, ₦270,000 in total, paid online.">
          <StayScreen />
        </Drawing>
      </Crop>
    ),
  },
];

const FAQ = [
  {
    q: "Is PropertyLink free for agents?",
    a: "Yes. The free plan gives you 3 live listings, each up for 30 days, with enquiries, WhatsApp leads and the Verified seal once we have checked your papers. No card needed, and no time limit.",
  },
  {
    q: "How much do paid plans cost?",
    a: "Each plan and its price is shown in naira on the Plan and billing page in your studio, before you pay. Paid plans add more live listings that stay up for 60 days, views and lead stats, and seats for your agents.",
  },
  {
    q: "How do I get the Verified seal?",
    a: (
      <>
        In the studio, upload a government ID and, for a business, your registration document or estate surveyor&apos;s licence. A PropertyLink
        moderator checks them by hand. Until then, your listings are reviewed before they go live.{" "}
        <Link href="/how-verification-works" className={lsLink}>
          How verification works
        </Link>
      </>
    ),
  },
  {
    q: "Can I charge an inspection fee?",
    a: "No. Viewings booked on PropertyLink are free for seekers, and a listing can't include an inspection fee. Seekers book into the weekly windows you set, and you confirm each one.",
  },
  {
    q: "What are boosts?",
    a: "A boost gives one live listing more reach for a set number of days. Featured puts it on the home page with a Featured tag; Top of search puts it above others in its area. Boosts work on every plan, and each one's length and price is shown in the studio before you pay.",
  },
  {
    q: "How do short-let payments work?",
    a: "Guests choose their dates and pay online through Paystack when they book. PropertyLink keeps a commission, shown on your studio's earnings page, and the rest is yours to pay out to your bank from the day after check-in.",
  },
  {
    q: "Can my agents work in the same studio?",
    a: "Yes. Invite each agent from the Team page. Everyone gets their own login and a role: owners and admins run the agency, and staff post listings, answer enquiries and run viewings. The free plan has one seat; paid plans add more.",
  },
  {
    q: "Can I send invoices for my fees?",
    a: (
      <>
        PropertyLink doesn&apos;t send invoices.{" "}
        <a href={appHref(invoices, invoices.landingPath)} className={lsLink}>
          InvoiceLink
        </a>{" "}
        does, another LinkSuite app: invoices, receipts and payment links for your agency and legal fees, with the same sign-in.
      </>
    ),
  },
];

export default function ForAgentsPage() {
  return (
    <>
      <JsonLd data={{ "@context": "https://schema.org", ...appJsonLd(app) }} />
      <SuiteHeader
        app={app}
        nav={[
          { href: "/for-agents#plans", label: "Plans", wide: true },
          { href: "/studio/login", label: "Log in" },
        ]}
        action={{ href: app.startPath, label: "Start free" }}
      />
      <main id="main" tabIndex={-1} className="flex-1 outline-none">
        <section aria-labelledby="hero-title" className="ls-container pt-10 sm:pt-14 lg:pt-20">
          <div className="grid gap-8 lg:grid-cols-[minmax(0,7fr)_minmax(0,4fr)] lg:items-end lg:gap-14">
            <div>
              <h1 id="hero-title" className="ls-h1 max-w-[13ch] text-ls-ink">
                Let and sell homes with every cost shown.
              </h1>
              <p className="ls-lead mt-5 max-w-[38rem] text-ls-ink-2">
                For agents, agencies and landlords. List a home once with the rent and every fee added up, take enquiries on WhatsApp, and let seekers
                book viewings into the hours you choose.
              </p>
              <div className="mt-7 flex flex-wrap items-center gap-3">
                <Link href={app.startPath} className={lsButton("app")}>
                  Start {app.name} free
                </Link>
                <Link href={app.signInPath} className={lsButton("secondary")}>
                  Log in to the studio
                </Link>
              </div>
            </div>
            <ul className="ls-small space-y-1.5 text-ls-ink-2 lg:pb-2">
              <li>Free to start, no card needed</li>
              <li>3 live listings on the free plan</li>
              <li>Works on any phone, even on slow data</li>
            </ul>
          </div>

          <div className="mt-10 flex h-[26rem] items-start justify-center gap-6 overflow-hidden rounded-t-ls-panel bg-app-wash px-4 pt-10 md:mt-14">
            <Drawing
              className="ls-paint hidden shrink-0 rounded-[10px] md:block"
              label="The PropertyLink listing editor at the Costs step: agency, legal and caution fees of ₦350,000 each added to ₦3,500,000 rent, a ₦4,550,000 move-in total."
            >
              <ListingEditorScreen />
            </Drawing>
            <PhoneFrame label="The PropertyLink studio on a phone: the enquiries inbox, with a new enquiry from Chiamaka Obi about a 2 bedroom flat in Yaba, ready to answer on WhatsApp.">
              <div className="ls-paint [--ls-i:1]">
                <EnquiriesScreen />
              </div>
            </PhoneFrame>
            <Drawing
              className="ls-paint hidden shrink-0 rounded-[10px] [--ls-i:2] xl:block"
              label="The PropertyLink viewings agenda for Saturday: a 10:00 request from Tunde Bakare waiting to be confirmed and two confirmed viewings, above the agent's weekly viewing windows."
            >
              <ViewingsScreen />
            </Drawing>
          </div>
        </section>

        <Section id="who" title="Who it's for">
          <ul className="mt-8 grid gap-x-10 gap-y-2 sm:grid-cols-2 lg:grid-cols-4">
            {WHO.map((w) => (
              <li key={w.title} className="border-t-2 border-app pt-4 pb-4">
                <h3 className="ls-h4 text-ls-ink">{w.title}</h3>
                <p className="mt-1 max-w-[34ch] text-ls-ink-2">{w.text}</p>
              </li>
            ))}
          </ul>
        </Section>

        <Section id="how" title="How it works">
          <StepList steps={STEPS} />
        </Section>

        <Section id="features" tone="surface" title="What you get in the studio">
          <FeatureList items={FEATURES} />
        </Section>

        <Section
          id="plans"
          tone="concrete"
          title="Free to start. Upgrade inside PropertyLink as you grow."
          intro="Start with three live listings and no card. When you need more listings, longer listing time or seats for your agents, choose a plan in the studio."
        >
          <PlansBlock appName={app.name} free={app.free} paid={app.paid} start={{ href: app.startPath, label: "Start free" }} />
        </Section>

        <Section id="faq" title="Questions agents ask">
          <Faq items={FAQ} />
        </Section>

        <FinalCta
          title="List your first home today."
          text="Free to start, with three live listings and no card needed."
          action={{ href: app.startPath, label: `Start ${app.name} free` }}
        />
      </main>
      <SuiteFooter
        app={app}
        links={[
          { href: "/for-agents#plans", label: "Plans" },
          { href: "/for-agents#faq", label: "Questions" },
          { href: app.signInPath, label: "Log in to the studio" },
          { href: "/how-verification-works", label: "How verification works" },
          { href: "/", label: "Find a home" },
        ]}
      />
    </>
  );
}
