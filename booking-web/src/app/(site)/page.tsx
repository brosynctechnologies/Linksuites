import type { Metadata } from "next";
import Link from "next/link";
import { HeroStage } from "@/components/site/hero-stage";
import { BookingPanelDrawing, CalendarDrawing, DepositDrawing, Drawing, SlotsDrawing } from "@/components/site/screens";
import { FeatureList, Faq, FinalCta, JsonLd, PlansBlock, Section, StepList } from "@/components/suite/sections";
import { SuiteFooter } from "@/components/suite/suite-footer";
import { SuiteHeader } from "@/components/suite/suite-header";
import { lsButton, lsLink } from "@/components/suite/ui";
import { appHref, appJsonLd, productByKey } from "@/lib/suite";
import { SITE_DESCRIPTION, SITE_TITLE } from "@/lib/site";

const app = productByKey("bookings");
const invoiceLink = productByKey("invoices");

export const metadata: Metadata = {
  title: { absolute: SITE_TITLE },
  description: SITE_DESCRIPTION,
  alternates: { canonical: "/" },
};

const WHO = [
  {
    title: "Hair salons and braiders",
    text: "Long services like knotless braids keep their full three hours, so nobody else is booked into that chair halfway through.",
  },
  {
    title: "Barbers",
    text: "Cuts back to back from your link, with walk-ins and phone bookings added to the same diary.",
  },
  {
    title: "Make-up artists",
    text: "Bridal and event bookings with your policies shown before anyone books, and a deposit that holds the date.",
  },
  {
    title: "Photo studios, spas and nail bars",
    text: "Sessions by the length they really take, with time set aside between bookings to get the room ready.",
  },
];

const STEPS = [
  { title: "Add your services and hours", text: "Name each service, how long it takes and its price. Set who works when, breaks included." },
  {
    title: "Put your link in your bio",
    text: "Your page gets its own link with your business name in it. Add it to Instagram, your WhatsApp status or your website.",
  },
  {
    title: "Customers book themselves",
    text: "They pick a service, a stylist and a free time, and pay a deposit by card or transfer if you ask for one.",
  },
  {
    title: "Run the day from the calendar",
    text: "Every chair in one view. Take walk-ins and phone bookings in the same diary, and mark people completed or no-show.",
  },
];

const FEATURES = [
  {
    title: "Only the times you can really take",
    text: "Your page shows a time only when the right person is free for the whole service, after breaks and time off. If two people reach for 10:30 together, the second is asked to pick another time.",
    visual: (
      <Drawing label="BookLink's time picker for Saturday 14 November: morning and afternoon times, with 10:30 selected." className="w-full">
        <SlotsDrawing />
      </Drawing>
    ),
  },
  {
    title: "Deposits that hold the chair",
    text: "Set a fixed amount or a percentage for each service. The time is held for 10 minutes while the customer pays, and the money goes to your own Paystack account. Deposits come with the paid plans.",
    visual: (
      <Drawing
        label="The last step of booking: Ada Okafor's name and phone number, a note that a ₦5,000 deposit holds her time, and the button to pay it."
        className="w-full"
      >
        <DepositDrawing />
      </Drawing>
    ),
  },
  {
    title: "Reminders, so the chair isn't empty at 10:30",
    text: "Open any booking and tap WhatsApp: a reminder is already written, with the link customers use to move or cancel. On paid plans, customers also get an email 24 hours and 2 hours before.",
    visual: (
      <Drawing label="A booking in the BookLink app, Ada Okafor at 10:30 with Tunde, with buttons to call her or send the WhatsApp reminder." className="w-full">
        <BookingPanelDrawing />
      </Drawing>
    ),
  },
  {
    title: "The whole day in one calendar",
    text: "Each person gets a column in their own colour, and bookings still waiting for a deposit are marked. Every customer record keeps a no-show count, so you know who to ask for a deposit.",
    visual: (
      <Drawing label="BookLink's calendar for one morning: Tunde and Bisi side by side, with three bookings, one still waiting for its deposit." className="w-full">
        <CalendarDrawing />
      </Drawing>
    ),
  },
];

const FAQ = [
  {
    q: "Is BookLink really free?",
    a: "Yes. The free plan has no time limit and needs no card. It covers one staff member and 50 bookings a month, and your page shows a small “Bookings by BookLink” note.",
  },
  {
    q: "How much do paid plans cost?",
    a: "Each plan and its price is shown in naira inside BookLink, on the billing page, before you pay. You only pay when you choose to upgrade.",
  },
  {
    q: "Where does the deposit money go?",
    a: "Straight to your own Paystack account. BookLink never holds it. Customers pay by card or bank transfer. Deposits come with the paid plans, once you connect Paystack in your settings.",
  },
  {
    q: "What happens when a customer doesn't show up?",
    a: "Mark the booking as a no-show. The count stays on that customer's record, so next time you know to ask for a deposit. Deposits aren't refunded automatically; your booking policy, shown on your page, says what happens.",
  },
  {
    q: "Can customers move or cancel their own booking?",
    a: "Yes, from the link in their confirmation, until the cutoff you set, such as 24 hours before. The time goes back on your page for someone else.",
  },
  {
    q: "Do my customers need to download an app?",
    a: "No. Your booking page opens in the browser on any phone. They don't need an account either: a name and a phone number are enough.",
  },
  {
    q: "Can my whole team use it?",
    a: "Yes. Paid plans add more staff, each with their own calendar and working hours, and logins for the people who run your front desk.",
  },
  {
    q: "A company wants an invoice for a booking. Can BookLink do that?",
    a: (
      <>
        Use{" "}
        <a href={appHref(invoiceLink, invoiceLink.landingPath)} className={lsLink}>
          InvoiceLink
        </a>
        , another LinkSuite app. Sign in with the same email and password and send an invoice the company can pay online.
      </>
    ),
  },
];

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
        <section aria-labelledby="hero-title" className="ls-container pt-10 pb-14 sm:pt-14 md:pb-20 lg:pt-20">
          <div className="grid gap-10 lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)] lg:items-center lg:gap-14">
            <div>
              <h1 id="hero-title" className="ls-h1 max-w-[13ch] text-ls-ink">
                Let customers book the chair without calling you.
              </h1>
              <p className="ls-lead mt-5 max-w-[32rem] text-ls-ink-2">
                A booking page for salons, barbers, braiders, make-up artists and studios. Customers pick a service and a time from your
                Instagram bio, and get a reminder before they come.
              </p>
              <div className="mt-7 flex flex-wrap gap-3">
                <Link href={app.startPath} className={lsButton("app")}>
                  Start BookLink free
                </Link>
                <Link href="/#how" className={lsButton("secondary")}>
                  See how it works
                </Link>
              </div>
              <ul className="ls-small mt-6 space-y-1.5 text-ls-ink-2">
                <li>Free to start, no card needed</li>
                <li>Works in the browser on any phone</li>
                <li>Deposits by card or transfer, through Paystack</li>
              </ul>
            </div>
            <HeroStage />
          </div>
        </section>

        <Section id="who" title="For salons, barbers, braiders, make-up artists and studios">
          <FeatureList items={WHO} />
        </Section>

        <Section id="how" title="How it works">
          <StepList steps={STEPS} />
        </Section>

        <Section
          id="features"
          tone="surface"
          title="What your booking page does for you"
          intro="Your customers see a page with your name, your services and the times you can really take. You see every booking in one place."
        >
          <FeatureList items={FEATURES} />
        </Section>

        <Section
          id="plans"
          tone="concrete"
          title="Free to start. Upgrade inside BookLink as you grow."
          intro="The free plan runs a booking page for one person. Paid plans add deposits, email reminders and room for your whole team. You choose a plan inside BookLink and see its price in naira before you pay."
        >
          <PlansBlock appName={app.name} free={app.free} paid={app.paid} start={{ href: app.startPath, label: "Start BookLink free" }} />
        </Section>

        <Section id="faq" title="Questions owners ask">
          <Faq items={FAQ} />
        </Section>

        <FinalCta
          title="Put your booking page in your bio today."
          text="Add your services and hours, then share your link. Free to start, no card needed."
          action={{ href: app.startPath, label: "Start BookLink free" }}
        />
      </main>
      <SuiteFooter
        app={app}
        links={[
          { href: "/#plans", label: "Plans" },
          { href: "/#faq", label: "Questions" },
          { href: "/login", label: "Log in" },
          { href: app.startPath, label: "Create your booking page" },
        ]}
      />
    </>
  );
}
