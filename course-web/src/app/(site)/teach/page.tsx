import type { Metadata } from "next";
import Link from "next/link";
import { FeatureList, Faq, FinalCta, JsonLd, PlansBlock, Section, StepList } from "@/components/suite/sections";
import { SuiteFooter } from "@/components/suite/suite-footer";
import { SuiteHeader } from "@/components/suite/suite-header";
import { lsButton, lsLink } from "@/components/suite/ui";
import { AcademyVisual, EarningsVisual, MarketplaceVisual, QuizVisual, TeachHeroVisual } from "@/components/teach-screens";
import { APP_NAME, FREE_PLAN_ACADEMY_FEE_PCT, MARKETPLACE_FEE_PCT, REFUND_WINDOW_DAYS } from "@/lib/config";
import { TEACH_DESCRIPTION, TEACH_TITLE, baseOpenGraph } from "@/lib/site";
import { appHref, appJsonLd, productByKey } from "@/lib/suite";

const app = productByKey("courses");
const school = productByKey("school");

/* The share image comes from opengraph-image.tsx beside this page, so Open Graph here sets no images. */
export const metadata: Metadata = {
  title: { absolute: TEACH_TITLE },
  description: TEACH_DESCRIPTION,
  alternates: { canonical: "/teach" },
  openGraph: { ...baseOpenGraph, url: "/teach", title: TEACH_TITLE, description: TEACH_DESCRIPTION },
};

const TRADES = [
  "WAEC, NECO and JAMB tutors",
  "ICAN, ACCA and other professional exam coaches",
  "Coding, design and data bootcamps",
  "Excel, accounting and business trainers",
  "Make-up, baking, tailoring and other craft classes",
  "Yoruba, Igbo, Hausa and French teachers",
];

const STEPS = [
  {
    title: "Open your academy",
    text: "Sign up free with your email and name your academy. You get your own page at your own link, in your colours.",
  },
  {
    title: "Build your first course",
    text: "Upload video lessons, add readings, files and quizzes, and mark a lesson or two as a free preview.",
  },
  {
    title: "Share it, or list it",
    text: "Post your academy link on WhatsApp, Instagram or Telegram. Ask to list the course on the marketplace too, and our team reviews it.",
  },
  {
    title: "Get paid in naira",
    text: `Learners pay through Paystack. Each sale clears after ${REFUND_WINDOW_DAYS} days, then you request a payout to your bank.`,
  },
];

const FEATURES = [
  {
    title: "Your own academy page",
    text: "A page at your own link with your name, logo and colours, listing your courses. Share it from your bio, your WhatsApp status or your Telegram channel.",
    visual: <AcademyVisual />,
  },
  {
    title: "Quizzes that check the work",
    text: "Add checkpoint quizzes with a pass mark. Learners see their score and the right answers. On a paid plan, those who finish get a certificate anyone can check online.",
    visual: <QuizVisual />,
  },
  {
    title: "New learners from the marketplace",
    text: "Learners search the LearnLink marketplace by subject and level. List a course there and people who have never seen your page can find it.",
    visual: <MarketplaceVisual />,
  },
  {
    title: "Earnings you can follow",
    text: "See every sale, the fee that came off it and the day it clears. Request a payout when you're ready.",
    visual: <EarningsVisual />,
  },
];

const MONEY = [
  {
    term: "Sales from your academy",
    text: `Nothing comes off on a paid plan. On the free plan, LearnLink keeps ${FREE_PLAN_ACADEMY_FEE_PCT}%.`,
  },
  {
    term: "Sales from the marketplace",
    text: `LearnLink keeps ${MARKETPLACE_FEE_PCT}% on every plan, because the marketplace found that learner.`,
  },
  {
    term: "When the money clears",
    text: `${REFUND_WINDOW_DAYS} days after each sale, the time a learner has to ask for a refund. Then it's available to pay out.`,
  },
  {
    term: "How you're paid",
    text: "Request a payout from your earnings page, and LearnLink pays it into your bank account.",
  },
];

const FAQ = [
  {
    q: "Is LearnLink really free to start?",
    a: "Yes. The free plan has no time limit and needs no card. It takes 2 courses and 2 GB of video, with your own academy page and a marketplace listing. When you need more, upgrade inside LearnLink, where each plan's price is shown in naira before you pay.",
  },
  {
    q: "What does LearnLink take from my sales?",
    a: `On the free plan, ${FREE_PLAN_ACADEMY_FEE_PCT}% of sales from your academy. Paid plans take nothing from academy sales. Sales that come through the marketplace carry a ${MARKETPLACE_FEE_PCT}% fee on every plan.`,
  },
  {
    q: "How do learners pay, and how do I get my money?",
    a: `Learners pay in naira by card, bank transfer or USSD through Paystack. LearnLink collects the payment and holds it for ${REFUND_WINDOW_DAYS} days in case of a refund. Then it shows as available on your earnings page, and you request a payout to your bank account.`,
  },
  {
    q: "Do I have to list my courses on the marketplace?",
    a: "No. Listing is optional, course by course, and our team reviews a course before it appears there. A course you don't list still sells from your academy page.",
  },
  {
    q: "Do my learners need to install an app?",
    a: "No. LearnLink works in the browser on any phone or computer. Learners sign up with their email, buy your course and start the first lesson straight away.",
  },
  {
    q: "Can I teach with a team?",
    a: "Yes. Invite tutors and staff to your academy and choose what each person can do. The free plan has one seat; paid plans add more.",
  },
  {
    q: "I already learn on LearnLink. Do I need a new account?",
    a: "No. Sign up to teach with the same email and password, and your studio is added to the account you already have.",
  },
];

export default function TeachPage() {
  return (
    <>
      <JsonLd data={{ "@context": "https://schema.org", ...appJsonLd(app) }} />
      <SuiteHeader
        app={app}
        nav={[
          { href: "/teach#plans", label: "Plans", wide: true },
          { href: "/courses", label: "Browse courses", wide: true },
          { href: "/teach/login", label: "Log in" },
        ]}
        action={{ href: app.startPath, label: "Start free" }}
      />

      <main id="main" tabIndex={-1} className="flex-1 outline-none">
        <section aria-labelledby="hero-title" className="ls-container pt-10 pb-14 sm:pt-14 md:pb-20 lg:pt-20">
          <div className="grid gap-10 lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)] lg:items-center lg:gap-14">
            <div>
              <h1 id="hero-title" className="ls-h1 max-w-[12ch] text-ls-ink">
                Turn your classes into courses people pay for.
              </h1>
              <p className="ls-lead mt-5 max-w-[32rem] text-ls-ink-2">
                Sell your lessons in naira to the people who already follow you, and reach new learners on the {APP_NAME} marketplace.
              </p>
              <div className="mt-7 flex flex-wrap gap-3">
                <Link href={app.startPath} className={lsButton("app", "lg")}>
                  Create your academy free
                </Link>
                <Link href={app.signInPath} className={lsButton("secondary", "lg")}>
                  Log in to the studio
                </Link>
              </div>
              <ul className="ls-small mt-6 space-y-1.5 text-ls-ink-2">
                <li>Free to start, no card needed</li>
                <li>Learners pay by card, transfer or USSD</li>
                <li>Works in the browser on any phone</li>
              </ul>
            </div>
            <TeachHeroVisual />
          </div>
        </section>

        <Section
          id="who"
          title="Who it's for"
          intro="Anyone who already teaches, in a classroom, on YouTube, on Instagram or in a Telegram or WhatsApp group, and wants to be paid for it."
        >
          <ul className="mt-8 grid max-w-[46rem] border-t border-ls-line sm:grid-cols-2 sm:gap-x-10">
            {TRADES.map((t) => (
              <li key={t} className="ls-h4 border-b border-ls-line py-3.5 text-ls-ink">
                {t}
              </li>
            ))}
          </ul>
          <p className="mt-8 max-w-[38rem] text-ls-ink-2">
            Running a whole school, with fees, results and parents?{" "}
            <a href={appHref(school, school.landingPath)} className={lsLink}>
              {school.name}
            </a>{" "}
            runs the school, and {APP_NAME} sells the lessons you teach online.
          </p>
        </Section>

        <Section id="how" title="How it works">
          <StepList steps={STEPS} />
        </Section>

        <Section id="features" tone="surface" title="Everything a course needs, from your phone">
          <FeatureList items={FEATURES} />
        </Section>

        <Section
          id="money"
          title="How you get paid"
          intro="LearnLink collects every payment through Paystack and pays you out. This is everything that comes off a sale."
        >
          <dl className="mt-10 grid gap-x-10 sm:grid-cols-2">
            {MONEY.map((m) => (
              <div key={m.term} className="border-t border-ls-line py-5">
                <dt className="ls-h4 text-ls-ink">{m.term}</dt>
                <dd className="mt-1.5 max-w-[46ch] text-ls-ink-2">{m.text}</dd>
              </div>
            ))}
          </dl>
        </Section>

        <Section
          id="plans"
          tone="concrete"
          title={`Free to start. Upgrade inside ${app.name} as you grow.`}
          intro="The free plan is enough to publish and sell your first courses. Paid plans add room for more, and take no fee from your academy sales."
        >
          <PlansBlock appName={app.name} free={app.free} paid={app.paid} start={{ href: app.startPath, label: "Create your academy free" }} />
        </Section>

        <Section id="faq" title="Questions tutors ask">
          <Faq items={FAQ} />
        </Section>

        <FinalCta
          title="Open your academy and publish your first course."
          text="Free to start, no card needed."
          action={{ href: app.startPath, label: "Create your academy free" }}
        />
      </main>

      <SuiteFooter
        app={app}
        links={[
          { href: "/teach#plans", label: "Plans" },
          { href: "/teach#faq", label: "Questions" },
          { href: "/teach/login", label: "Tutor log in" },
          { href: "/courses", label: "Browse courses" },
        ]}
      />
    </>
  );
}
