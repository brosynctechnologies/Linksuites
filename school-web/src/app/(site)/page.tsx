import type { Metadata } from "next";
import Link from "next/link";
import { BursarInvoiceCrop, HeroStage, ParentHomePhone, RegisterPhone, ScoreSheetCrop } from "@/components/site/screens";
import { FeatureList, Faq, FinalCta, JsonLd, PlansBlock, Section, StepList } from "@/components/suite/sections";
import { SuiteFooter } from "@/components/suite/suite-footer";
import { SuiteHeader } from "@/components/suite/suite-header";
import { lsButton, lsLink } from "@/components/suite/ui";
import { DEFAULT_DESCRIPTION, DEFAULT_TITLE, baseOpenGraph, baseTwitter } from "@/lib/site";
import { appHref, appJsonLd, productByKey } from "@/lib/suite";

const app = productByKey("school");
const learn = productByKey("courses");

export const metadata: Metadata = {
  title: { absolute: DEFAULT_TITLE },
  description: DEFAULT_DESCRIPTION,
  alternates: { canonical: "/" },
  openGraph: { ...baseOpenGraph, url: "/", title: DEFAULT_TITLE, description: DEFAULT_DESCRIPTION },
  twitter: { ...baseTwitter, title: DEFAULT_TITLE, description: DEFAULT_DESCRIPTION },
};

const WHO = [
  {
    title: "Proprietors",
    text: "See what has been paid and what is still owed this term, class by class, and what the school has spent. From your phone, wherever you are.",
  },
  {
    title: "Head teachers and principals",
    text: "Follow scores as teachers enter them, lock each class's results when they're checked, and print report cards with your logo.",
  },
  {
    title: "Bursars",
    text: "Raise the term's invoices in one go, take part-payments and instalments, print receipts and see the debtors list without adding anything up.",
  },
  {
    title: "Parents",
    text: "Open the link the school sends, sign in with a code, and see results, attendance and fees for every child in the family.",
  },
];

const STEPS = [
  {
    title: "Set up your school",
    text: "Start from the Nigerian template: three terms, creche to SSS 3, CA1, CA2 and exam, grades from A1 to F9. Change what's different.",
  },
  { title: "Bring in your students", text: "Import them from your Excel sheet with their parents' phone numbers, or add them one at a time." },
  { title: "Invite your staff", text: "Your principal, bursar and teachers each get their own sign-in, and see only what their role needs." },
  { title: "Send parents their link", text: "By email or WhatsApp. Parents sign in with a code, so there's no password to remember." },
];

const FEATURES = [
  {
    title: "Fees collected without chasing",
    text: "Invoices for each term, with instalments, part-payments and receipts. On a paid plan, parents pay online into your school's own Paystack account and reminders are emailed for you.",
    visual: <BursarInvoiceCrop />,
  },
  {
    title: "Results without the calculator",
    text: "Teachers type CA and exam scores on their phones. Totals, grades, positions and report cards follow, ready to print.",
    visual: <ScoreSheetCrop />,
  },
  {
    title: "A register that works when the data runs out",
    text: "Teachers take attendance on a phone with or without a connection. It sends when they're back online, and the parents of absent children can be messaged on WhatsApp.",
    visual: <RegisterPhone />,
  },
  {
    title: "Parents see it all on their phone",
    text: "Results, attendance, fees, homework, the timetable and the school's messages, for each child, with nothing to install.",
    visual: <ParentHomePhone />,
  },
];

const FAQ = [
  {
    q: "Is SchoolLink really free?",
    a: "Yes, for up to 50 students and 3 staff logins, with results, report cards and attendance. There is no time limit and no card needed. If you reach a limit, only new students or staff are stopped: your records are never blocked.",
  },
  {
    q: "How do parents pay school fees?",
    a: "On a paid plan, parents pay by card or bank transfer through Paystack, straight into your school's own Paystack account. SchoolLink never holds the money. The bursar can also record cash and bank payments, and every payment gets a receipt.",
  },
  {
    q: "We have our own terms, grades and report card. Can we keep them?",
    a: "Yes. The Nigerian template is a starting point. Change the terms, levels and classes, the CA and exam weighting, the grading scale and the comments to match how your school already works.",
  },
  {
    q: "Our students are in an Excel sheet. Do we type them again?",
    a: "No. Upload the sheet and SchoolLink checks every row before anything is saved, so you can fix mistakes first. Parents' phone numbers come in with the students.",
  },
  {
    q: "Do parents and teachers need to install an app?",
    a: "No. SchoolLink works in the browser on any phone. Parents sign in with a code sent to their email, and students in primary and secondary sign in with their admission number.",
  },
  {
    q: "Who can see pupils' records?",
    a: "Staff see only what their role needs: the bursar works on fees, and teachers take the register and enter scores for their own classes. Parents see only their own children.",
  },
  {
    q: "Can we sell extra lessons or holiday classes online?",
    a: (
      <>
        That&apos;s what{" "}
        <a href={appHref(learn, learn.landingPath)} className={lsLink}>
          LearnLink
        </a>{" "}
        does: video lessons, quizzes and certificates, paid for online. It&apos;s another LinkSuite app, and the same sign-in works in both.
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
          { href: "/demo", label: "Book a demo", wide: true },
          { href: "/login", label: "Log in" },
        ]}
        action={{ href: app.startPath, label: "Start free" }}
      />

      <main id="main" tabIndex={-1} className="flex-1 outline-none">
        <section aria-labelledby="hero-title" className="ls-container pt-10 pb-14 sm:pt-14 md:pb-20 lg:pt-20">
          <div className="grid gap-10 lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)] lg:items-center lg:gap-14">
            <div>
              <h1 id="hero-title" className="ls-h1 max-w-[12ch] text-ls-ink">
                Run your school&apos;s term from one place.
              </h1>
              <p className="ls-lead mt-5 max-w-[30rem] text-ls-ink-2">
                Fees, results, attendance and a portal for parents, made for nursery, primary and secondary schools in Nigeria.
              </p>
              <div className="mt-8 flex flex-wrap gap-3">
                <Link href={app.startPath} className={lsButton("app", "lg")}>
                  Set up your school free
                </Link>
                <Link href="/demo" className={lsButton("secondary", "lg")}>
                  Book a demo
                </Link>
              </div>
              <ul className="ls-small mt-6 space-y-1.5 text-ls-ink-2">
                <li>Free for up to 50 students, no card needed</li>
                <li>Online fees go straight to your school&apos;s Paystack account</li>
                <li>Works on any phone, for staff and parents</li>
              </ul>
            </div>
            <HeroStage />
          </div>
        </section>

        <Section
          id="who"
          title="Who it's for"
          intro="Nursery, primary and secondary schools, from creche to SSS 3. Everyone signs in to the part of the school they look after."
        >
          <ul className="mt-10 grid gap-x-10 sm:grid-cols-2">
            {WHO.map((w) => (
              <li key={w.title} className="border-t border-ls-line py-6">
                <h3 className="ls-h3 text-ls-ink">{w.title}</h3>
                <p className="mt-2 max-w-[46ch] text-ls-ink-2">{w.text}</p>
              </li>
            ))}
          </ul>
        </Section>

        <Section id="how" title="How it works">
          <StepList steps={STEPS} />
        </Section>

        <Section id="features" tone="surface" title="From the first day of term to the report card">
          <FeatureList items={FEATURES} />
        </Section>

        <Section
          id="plans"
          tone="concrete"
          title="Free to start. Upgrade inside SchoolLink as you grow."
          intro="Paid plans are by the number of students and are paid each term. The fees parents pay go to your school, not to us."
        >
          <PlansBlock appName={app.name} free={app.free} paid={app.paid} start={{ href: app.startPath, label: "Set up your school free" }} />
          <p className="mt-6 max-w-[46rem] text-ls-ink-2">
            More than 1,000 students, or several branches?{" "}
            <Link href="/demo" className={lsLink}>
              Book a demo
            </Link>{" "}
            and we&apos;ll set SchoolLink up with you.
          </p>
        </Section>

        <Section id="faq" title="Questions schools ask">
          <Faq items={FAQ} />
        </Section>

        <FinalCta
          title="Set up your school before next term."
          text={
            <>
              Free for up to 50 students. Want to see it with your own classes first?{" "}
              <Link href="/demo" className="font-semibold underline decoration-1 underline-offset-[0.2em] hover:decoration-2">
                Book a demo
              </Link>
              .
            </>
          }
          action={{ href: app.startPath, label: "Start free" }}
        />
      </main>

      <SuiteFooter
        app={app}
        links={[
          { href: "/#plans", label: "Plans" },
          { href: "/#faq", label: "Questions" },
          { href: "/demo", label: "Book a demo" },
          { href: "/login", label: "Staff log in" },
        ]}
      />
    </>
  );
}
