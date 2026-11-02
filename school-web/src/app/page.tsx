import Link from "next/link";
import { SiteFooter, SiteHeader } from "@/components/site-chrome";
import { buttonClass } from "@/components/styles";

const CARD = [
  { subject: "English Language", ca1: 17, ca2: 16, exam: 48, grade: "A1" },
  { subject: "Mathematics", ca1: 15, ca2: 18, exam: 41, grade: "B2" },
  { subject: "Basic Science", ca1: 14, ca2: 13, exam: 39, grade: "B3" },
  { subject: "Civic Education", ca1: 12, ca2: 15, exam: 35, grade: "C4" },
  { subject: "Yoruba", ca1: 13, ca2: 11, exam: 32, grade: "C5" },
];

const JOBS = [
  {
    title: "Fees collected without chasing",
    text: "Invoices per term, instalments, part-payments online to your school's own Paystack account, and reminders that go out on their own.",
  },
  {
    title: "Results without the calculator",
    text: "Teachers enter CA1, CA2 and exam scores on their phones. Totals, grades, positions and printed report cards follow.",
  },
  {
    title: "Parents kept informed",
    text: "Parents get a link by email or WhatsApp, sign in with a code, and see results, attendance and fees for every child.",
  },
];

/** The product's real output: a report card fragment on a ruled sheet. */
function ReportCard() {
  return (
    <figure className="ruled-sheet w-full max-w-md justify-self-center md:justify-self-end" aria-label="Sample report card">
      <div className="ruled-pad border-b border-rule py-3">
        <p className="slab text-lg font-bold text-ink">Adaeze Okafor</p>
        <p className="text-xs text-muted">JSS 2A, first term 2026/2027, position 4 of 32</p>
      </div>
      <table className="w-full text-sm">
        <thead>
          <tr className="text-left text-xs text-muted">
            <th scope="col" className="py-2 pr-2 pl-6 font-bold sm:pl-8">
              Subject
            </th>
            <th scope="col" className="px-1 py-2 text-right font-bold">
              CA1
            </th>
            <th scope="col" className="px-1 py-2 text-right font-bold">
              CA2
            </th>
            <th scope="col" className="px-1 py-2 text-right font-bold">
              Exam
            </th>
            <th scope="col" className="px-1 py-2 text-right font-bold">
              Total
            </th>
            <th scope="col" className="py-2 pr-4 pl-2 text-right font-bold sm:pr-5">
              Grade
            </th>
          </tr>
        </thead>
        <tbody>
          {CARD.map((r) => (
            <tr key={r.subject} className="border-t border-rule">
              <th scope="row" className="py-2 pr-2 pl-6 text-left font-normal text-ink sm:pl-8">
                {r.subject}
              </th>
              <td className="tnum px-1 py-2 text-right">{r.ca1}</td>
              <td className="tnum px-1 py-2 text-right">{r.ca2}</td>
              <td className="tnum px-1 py-2 text-right">{r.exam}</td>
              <td className="tnum px-1 py-2 text-right font-bold">{r.ca1 + r.ca2 + r.exam}</td>
              <td className="slab py-2 pr-4 pl-2 text-right text-base font-bold text-blazer sm:pr-5">{r.grade}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <figcaption className="ruled-pad border-t border-rule py-2.5 text-xs text-muted">
        Computed from the scores teachers enter. Printed with your logo and the principal&apos;s signature.
      </figcaption>
    </figure>
  );
}

export default function HomePage() {
  return (
    <div className="flex min-h-screen flex-col">
      <SiteHeader />
      <main>
        <section className="mx-auto grid max-w-6xl items-center gap-10 px-4 pt-10 pb-14 sm:px-6 md:grid-cols-[minmax(0,1fr)_minmax(0,28rem)] md:pt-16 md:pb-20">
          <div className="max-w-xl">
            <h1 className="slab text-3xl font-bold text-ink sm:text-4xl">Run your school&apos;s term from one place.</h1>
            <p className="mt-4 max-w-prose text-lg text-slate-700">
              Import your students from Excel, collect fees into your own account, print report cards, and keep every parent informed on their
              phone. Made for nursery, primary and secondary schools in Nigeria.
            </p>
            <div className="mt-6 flex flex-wrap gap-2">
              <Link href="/signup" className={buttonClass("primary", "px-5")}>
                Set up your school free
              </Link>
              <Link href="/demo" className={buttonClass("secondary", "px-5")}>
                Book a demo
              </Link>
            </div>
            <p className="mt-3 text-sm text-muted">Free for up to 50 students. No card needed.</p>
          </div>
          <ReportCard />
        </section>

        <section className="border-t border-rule bg-surface" aria-labelledby="jobs">
          <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6">
            <h2 id="jobs" className="slab text-2xl font-bold text-ink">
              What schools pay for
            </h2>
            <ul className="mt-6 grid gap-x-8 gap-y-6 md:grid-cols-3">
              {JOBS.map((j) => (
                <li key={j.title} className="border-l-2 border-margin pl-4">
                  <h3 className="font-bold text-ink">{j.title}</h3>
                  <p className="mt-1 text-sm text-slate-700">{j.text}</p>
                </li>
              ))}
            </ul>
          </div>
        </section>

        <section className="mx-auto max-w-6xl px-4 py-12 sm:px-6" aria-labelledby="setup">
          <h2 id="setup" className="slab text-2xl font-bold text-ink">
            Set up in an afternoon
          </h2>
          <p className="mt-2 max-w-prose text-slate-700">
            Start from the Nigerian template: three terms, Creche to SSS 3, CA1 20, CA2 20 and exam 60, and WAEC-style grades from A1 to F9. Change
            any of it to match your school.
          </p>
          <div className="mt-6 flex flex-wrap gap-2">
            <Link href="/signup" className={buttonClass("primary", "px-5")}>
              Set up your school free
            </Link>
            <Link href="/pricing" className={buttonClass("secondary", "px-5")}>
              See pricing
            </Link>
          </div>
        </section>
      </main>
      <SiteFooter />
    </div>
  );
}
