import type { Metadata } from "next";
import Link from "next/link";
import { SiteFooter, SiteHeader } from "@/components/site-chrome";
import { buttonClass, cx } from "@/components/styles";

export const metadata: Metadata = { title: { absolute: "CareLink: patients, wards and billing for your clinic" } };

/** The outpatient visit, in the order it happens. A real sequence, so it is numbered. */
const VISIT = [
  { step: "Check in", text: "Front desk finds the patient by name, number or phone and puts them in the queue." },
  { step: "Vitals", text: "The nurse records temperature, blood pressure and pulse. Out-of-range readings are flagged." },
  { step: "Doctor", text: "The doctor writes the note, prescribes and requests tests, with allergy warnings." },
  { step: "Bill", text: "The visit's charges come from your price list. Nothing is typed twice." },
  { step: "Paid", text: "Cash, POS, transfer or online through your own Paystack. A receipt either way." },
];

const AREAS = [
  {
    title: "Front desk",
    text: "Register patients with a duplicate check, book appointments by doctor, check patients in and watch the queue.",
  },
  {
    title: "Records that stay private",
    text: "Clinical notes, allergies and results are for doctors and nurses only. Every opening of a record is logged for the medical director.",
  },
  {
    title: "Wards and beds",
    text: "Admit to a bed with a deposit, chart nursing care and doses, and discharge with a signed summary. Bed days bill themselves.",
  },
  {
    title: "Billing and the cashier",
    text: "Bills fill from the price list. Take deposits and payments, print receipts and follow up debtors on WhatsApp.",
  },
];

export default function LandingPage() {
  return (
    <div className="flex min-h-screen flex-col">
      <SiteHeader />
      <main>
        <section className="border-b border-rule bg-surface">
          <div className="mx-auto grid max-w-6xl gap-10 px-4 py-12 sm:px-6 md:grid-cols-[minmax(0,1.05fr)_minmax(0,1fr)] md:items-center md:py-20">
            <div>
              <h1 className="display text-3xl font-bold text-ink sm:text-4xl">Run your clinic from the front desk to the ward.</h1>
              <p className="mt-4 max-w-[34rem] text-lg text-slate-700">
                CareLink keeps patients, appointments, consultations, admissions and bills in one place for private clinics and small hospitals
                in Nigeria. Patients book, read their record and pay in their own portal.
              </p>
              <div className="mt-7 flex flex-wrap gap-2">
                <Link href="/signup" className={buttonClass("primary", "px-6")}>
                  Set up your clinic free
                </Link>
                <Link href="/pricing" className={buttonClass("secondary")}>
                  See pricing
                </Link>
              </div>
              <p className="mt-3 text-sm text-muted">Free for outpatients with up to 3 staff. Import your price list and patients from Excel.</p>
            </div>

            <div className="rounded-panel border border-rule bg-gauze p-5 sm:p-7" aria-label="A patient's visit">
              <div className="flex items-center gap-3 rounded-full border-2 border-scrub-line bg-surface py-2 pr-5 pl-2.5 shadow-[0_1px_0_#cbd8d4]">
                <span aria-hidden="true" className="h-6 w-6 shrink-0 rounded-full border-2 border-scrub-line bg-gauze" />
                <span className="min-w-0">
                  <span className="display tnum block text-xl leading-tight font-bold text-scrub-dark">CL-000123</span>
                  <span className="block truncate text-sm text-slate-700">Adaeze Okafor, 34, female</span>
                </span>
              </div>
              <ol className="mt-6 space-y-0">
                {VISIT.map((v, i) => (
                  <li
                    key={v.step}
                    className="relative grid animate-step-in grid-cols-[2rem_minmax(0,1fr)] gap-3 pb-4 last:pb-0"
                    style={{ animationDelay: `${150 + i * 110}ms` }}
                  >
                    {i < VISIT.length - 1 && <span aria-hidden="true" className="absolute top-8 bottom-0 left-[0.97rem] w-0.5 bg-scrub-line" />}
                    <span
                      className={cx(
                        "display tnum flex h-8 w-8 items-center justify-center rounded-full text-sm font-bold",
                        i === VISIT.length - 1 ? "bg-scrub text-white" : "border-2 border-scrub bg-surface text-scrub",
                      )}
                    >
                      {i + 1}
                    </span>
                    <span className="pt-0.5">
                      <span className="block font-bold text-ink">{v.step}</span>
                      <span className="block text-sm text-slate-700">{v.text}</span>
                    </span>
                  </li>
                ))}
              </ol>
            </div>
          </div>
        </section>

        <section className="mx-auto max-w-6xl px-4 py-12 sm:px-6 md:py-16" aria-labelledby="areas">
          <h2 id="areas" className="display text-2xl font-bold text-ink">
            One system for the whole clinic
          </h2>
          <div className="mt-6 grid gap-x-10 gap-y-8 md:grid-cols-2">
            {AREAS.map((a) => (
              <div key={a.title} className="border-t-2 border-scrub pt-4">
                <h3 className="text-lg font-bold text-ink">{a.title}</h3>
                <p className="mt-1.5 max-w-prose text-slate-700">{a.text}</p>
              </div>
            ))}
          </div>
        </section>

        <section className="border-t border-rule bg-scrub-deep text-white" aria-labelledby="roles">
          <div className="on-scrub mx-auto grid max-w-6xl gap-8 px-4 py-12 sm:px-6 md:grid-cols-[minmax(0,1fr)_minmax(0,1.3fr)] md:py-16">
            <div>
              <h2 id="roles" className="display text-2xl font-bold">
                Everyone sees their part
              </h2>
              <p className="mt-2 max-w-md text-on-scrub">
                The front desk and the cashier never see clinical notes. Doctors and nurses don&apos;t see the till. The medical director sees
                everything, including who opened which record.
              </p>
            </div>
            <dl className="grid gap-x-8 gap-y-4 text-sm sm:grid-cols-2">
              {[
                ["Medical director", "Everything, with settings, staff and the plan"],
                ["Admin", "Staff, settings, wards, prices and reports"],
                ["Doctor", "Consultations, prescriptions, tests and ward rounds"],
                ["Nurse", "Vitals, nursing notes and the medication chart"],
                ["Front desk", "Registration, appointments, the queue and beds"],
                ["Cashier", "Bills, deposits, payments and receipts"],
              ].map(([role, text]) => (
                <div key={role} className="border-l-2 border-on-scrub/40 pl-3">
                  <dt className="font-bold text-white">{role}</dt>
                  <dd className="text-on-scrub">{text}</dd>
                </div>
              ))}
            </dl>
          </div>
        </section>
      </main>
      <SiteFooter />
    </div>
  );
}
