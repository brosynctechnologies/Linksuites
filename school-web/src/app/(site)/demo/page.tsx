import type { Metadata } from "next";
import Link from "next/link";
import { SuiteFooter } from "@/components/suite/suite-footer";
import { SuiteHeader } from "@/components/suite/suite-header";
import { lsLink } from "@/components/suite/ui";
import { baseOpenGraph, baseTwitter } from "@/lib/site";
import { productByKey } from "@/lib/suite";
import { DemoForm } from "./demo-form";

const app = productByKey("school");

const TITLE = "Book a SchoolLink demo for your school";
const DESCRIPTION =
  "We'll call you and walk through SchoolLink with your own classes and fee items: importing students, the register, scores, report cards and online fees.";

export const metadata: Metadata = {
  title: "Book a demo",
  description: DESCRIPTION,
  alternates: { canonical: "/demo" },
  openGraph: { ...baseOpenGraph, url: "/demo", title: TITLE, description: DESCRIPTION },
  twitter: { ...baseTwitter, title: TITLE, description: DESCRIPTION },
};

const COVERS = [
  "Importing your students and their parents from Excel",
  "Taking the register, entering scores and printing report cards",
  "Fee invoices, part-payments and how parents pay online",
  "What parents and students see on their phones",
];

export default function DemoPage() {
  return (
    <>
      <SuiteHeader
        app={app}
        nav={[
          { href: "/#plans", label: "Plans", wide: true },
          { href: "/login", label: "Log in" },
        ]}
        action={{ href: app.startPath, label: "Start free" }}
      />
      <main id="main" tabIndex={-1} className="flex-1 outline-none">
        <div className="ls-container ls-section-y grid gap-10 md:grid-cols-[minmax(0,1fr)_minmax(0,26rem)] md:gap-14">
          <div>
            <h1 className="ls-h1 max-w-[12ch] text-ls-ink">See SchoolLink with your own school.</h1>
            <p className="ls-lead mt-5 max-w-[34rem] text-ls-ink-2">
              Tell us about your school and the best number to reach you on. We&apos;ll call within one working day to set a time.
            </p>
            <h2 className="ls-h4 mt-10 text-ls-ink">What we&apos;ll walk through</h2>
            <ul className="mt-3 max-w-[34rem] divide-y divide-ls-line border-y border-ls-line text-ls-ink-2">
              {COVERS.map((c) => (
                <li key={c} className="py-3">
                  {c}
                </li>
              ))}
            </ul>
            <p className="mt-8 max-w-[34rem] text-ls-ink-2">
              Rather try it yourself first? SchoolLink is free for up to 50 students.{" "}
              <Link href={app.startPath} className={lsLink}>
                Set up your school free
              </Link>
              .
            </p>
          </div>
          <section aria-labelledby="demo-form-title" className="self-start rounded-ls-panel border border-ls-line bg-ls-surface p-5 sm:p-7">
            <h2 id="demo-form-title" className="ls-h3 text-ls-ink">
              Book a demo
            </h2>
            <p className="ls-small mt-1 mb-5 text-ls-muted">All fields are needed except the number of students.</p>
            <DemoForm />
          </section>
        </div>
      </main>
      <SuiteFooter
        app={app}
        links={[
          { href: "/#plans", label: "Plans" },
          { href: "/#faq", label: "Questions" },
          { href: "/login", label: "Staff log in" },
        ]}
      />
    </>
  );
}
