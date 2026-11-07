import type { Metadata } from "next";
import Link from "next/link";
import type { CSSProperties, ReactNode } from "react";
import {
  AccessLogScreen,
  BedBoardScreen,
  BillCrop,
  ConsultationScreen,
  DosesCrop,
  DuplicateCrop,
  PortalScreen,
  QueueScreen,
  ReceiptScreen,
} from "@/components/care-screens";
import { PhoneFrame } from "@/components/suite/phone";
import { Faq, FeatureList, FinalCta, JsonLd, PlansBlock, Section, StepList } from "@/components/suite/sections";
import { SuiteFooter } from "@/components/suite/suite-footer";
import { SuiteHeader } from "@/components/suite/suite-header";
import { lsButton, lsLink } from "@/components/suite/ui";
import { DEFAULT_DESCRIPTION, DEFAULT_TITLE } from "@/lib/site";
import { appJsonLd, productByKey } from "@/lib/suite";

const app = productByKey("care");

export const metadata: Metadata = {
  title: { absolute: DEFAULT_TITLE },
  description: DEFAULT_DESCRIPTION,
  alternates: { canonical: "/" },
};

/** The hero: one patient's wristband, CL-000123, on four real screens, in the order the clinic meets them. */
const HERO = [
  {
    caption: "Front desk: the queue",
    label: "CareLink's queue at the front desk: three patients waiting for the doctor, each with a wristband number, first Chinedu Eze, CL-000123.",
    Screen: QueueScreen,
  },
  {
    caption: "Doctor: the consultation",
    label:
      "The doctor's consultation for Chinedu Eze, CL-000123: a red allergy sticker for penicillin and ibuprofen, and a warning when ibuprofen is prescribed.",
    Screen: ConsultationScreen,
  },
  {
    caption: "Ward: the bed board",
    label: "The ward's bed board: Chinedu Eze, CL-000123, in bed A2, with bed A3 free and A4 being cleaned.",
    Screen: BedBoardScreen,
  },
  {
    caption: "Cashier: the receipt",
    label: "The cashier's receipt RCP-2026-00117 for Chinedu Eze, CL-000123: ₦18,500 received by POS, nothing left on the bill.",
    Screen: ReceiptScreen,
  },
];

const ROLES: [string, string][] = [
  ["Medical director", "Sets up the clinic, invites the staff and chooses the plan. Sees everything, including who opened each patient's record."],
  ["Front desk", "Registers patients, books appointments by doctor, checks patients in and keeps the queue moving. Never sees clinical notes."],
  ["Doctors", "Read the record, write the consultation note, prescribe with an allergy check, request tests and do ward rounds."],
  ["Nurses", "Take vital signs, chart every dose and nursing note, and look after the patients on the ward, on a phone if that's what's to hand."],
  ["Cashiers", "Bill from the price list, take deposits and payments, print receipts and follow up what is owed. Never see clinical notes."],
];

const VISIT = [
  { title: "Check in", text: "The front desk finds the patient by name, number or phone and adds them to the queue, booked or walk-in." },
  { title: "Vital signs", text: "A nurse records temperature, blood pressure and pulse. Readings out of range are flagged for the doctor." },
  { title: "The doctor", text: "The doctor writes the note, prescribes and requests tests. A drug that matches a recorded allergy stops for a check." },
  { title: "Bill and pay", text: "Charges come from your price list. The cashier takes cash, POS or transfer and prints the receipt." },
];

/** A drawing on the page: described once for screen readers, hidden from them inside. */
function Drawn({ label, children, className, style }: { label: string; children: ReactNode; className?: string; style?: CSSProperties }) {
  return (
    <div role="img" aria-label={label} className={className} style={style}>
      <div aria-hidden="true" className="h-full">
        {children}
      </div>
    </div>
  );
}

const FEATURES = [
  {
    title: "Registration that catches duplicates",
    text: "Each patient gets a number with your clinic's prefix. If the name and date of birth, or the phone, match someone already registered, the front desk sees them before saving.",
    visual: (
      <Drawn label="Registering a patient: CareLink warns that Chinedu Eze, CL-000123, may already be registered." className="w-full max-w-[20rem]">
        <DuplicateCrop />
      </Drawn>
    ),
  },
  {
    title: "Wards, beds and nursing care",
    text: "Admit to a bed with a deposit, chart every dose and nursing note, and discharge with a signed summary. Wards and beds come with a paid plan.",
    visual: (
      <Drawn label="The medication chart for bed A2: two doses given this morning and two due this afternoon." className="w-full max-w-[20rem]">
        <DosesCrop />
      </Drawn>
    ),
  },
  {
    title: "Bills that fill from your price list",
    text: "Consultations, tests, drugs and bed days come onto the bill at your prices, so nothing is typed twice. Import the price list from Excel.",
    visual: (
      <Drawn label="Bill BILL-2026-00042: a consultation, two tests and a drug, ₦18,500 unpaid." className="w-full max-w-[20rem]">
        <BillCrop />
      </Drawn>
    ),
  },
  {
    title: "A portal for your patients",
    text: "Patients and parents book appointments, read signed notes and results, and pay bills online into your clinic's own Paystack account. The portal comes with a paid plan.",
    visual: (
      <div className="h-[12.5rem] overflow-hidden">
        <PhoneFrame label="The patient portal on a phone: Chinedu's next appointment with Dr Bello, ₦18,500 to pay, and who opened his record.">
          <PortalScreen />
        </PhoneFrame>
      </div>
    ),
  },
];

const FAQ = [
  {
    q: "Is CareLink really free?",
    a: "Yes. The free plan runs an outpatient clinic with 3 staff logins: patient records, visits and bills. There's no time limit and no card needed.",
  },
  {
    q: "How much do paid plans cost?",
    a: "Plans go by the size of your clinic: staff logins and beds. The medical director sees each plan and its price in naira in Settings, before paying. Reaching a limit only stops new staff or beds; your records are never blocked.",
  },
  {
    q: "Who can see a patient's notes?",
    a: "Only doctors and nurses. The front desk and the cashier never see clinical notes. Every opening of a record is logged, and the medical director can read who opened what.",
  },
  {
    q: "Can we bring our patients and price list?",
    a: "Yes. Import your patients and your price list from an Excel file. CareLink shows the rows that need fixing before you confirm the import.",
  },
  {
    q: "How do patients pay?",
    a: "At the desk by cash, POS or bank transfer, with a printed receipt. On a paid plan they can also pay online through your clinic's own Paystack account, so the money goes to the clinic.",
  },
  {
    q: "Do we need new computers?",
    a: "No. CareLink works in the browser. Reception and consulting rooms usually use a computer; nurses can chart on a phone or tablet on the ward.",
  },
  {
    q: "Where do patients sign in?",
    a: (
      <>
        Patients open the invitation their clinic sends, then sign in to the{" "}
        <Link href="/portal/sign-in" className={lsLink}>
          patient portal
        </Link>{" "}
        with an emailed code or a password.
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
        <section aria-labelledby="hero-title" className="ls-container pt-10 pb-14 sm:pt-14 md:pb-20 lg:pt-16">
          <div className="grid gap-5 lg:grid-cols-[minmax(0,7fr)_minmax(0,5fr)] lg:items-end lg:gap-14">
            <h1 id="hero-title" className="ls-h1 max-w-[12ch] text-ls-ink">
              Your clinic, from front desk to ward.
            </h1>
            <div>
              <p className="ls-lead max-w-[34rem] text-ls-ink-2">
                Registration and the queue, consultations, wards and beds, bills and receipts, for private clinics and small hospitals in Nigeria.
              </p>
              <div className="mt-6 flex flex-wrap items-center gap-3">
                <Link href={app.startPath} className={lsButton("app")}>
                  Start CareLink free
                </Link>
                <Link href="#visit" className={lsButton("secondary")}>
                  See how a visit flows
                </Link>
              </div>
              <ul className="ls-small mt-5 space-y-1 text-ls-ink-2">
                <li>Free for an outpatient clinic with 3 staff logins. No card needed.</li>
                <li>One patient number follows each patient from check-in to receipt.</li>
              </ul>
            </div>
          </div>

          <ol aria-label="One patient through CareLink" className="mt-10 grid grid-cols-2 gap-x-3 gap-y-5 rounded-ls-panel bg-app-wash p-3 sm:gap-x-4 sm:p-5 lg:mt-14 lg:grid-cols-4 lg:p-6">
            {HERO.map(({ caption, label, Screen }, i) => (
              <li key={caption} className={i < 2 ? "col-span-2 lg:col-span-1" : undefined}>
                <p className="ls-label text-ls-ink-2">{caption}</p>
                <Drawn
                  label={label}
                  className="ls-paint mt-2 h-[13rem] overflow-hidden rounded-[10px] border border-rule bg-surface font-sans text-ink lg:h-[18.5rem]"
                  style={{ "--ls-i": i } as CSSProperties}
                >
                  <Screen />
                </Drawn>
              </li>
            ))}
          </ol>
        </section>

        <Section
          id="who"
          title="Every person in the clinic, in their own role"
          intro="For private clinics and small hospitals, outpatients only or with wards. Each person signs in with their own role and sees what their job needs."
        >
          <dl className="mt-10 border-b border-ls-line">
            {ROLES.map(([role, text]) => (
              <div key={role} className="grid gap-1 border-t border-ls-line py-5 md:grid-cols-[14rem_minmax(0,1fr)] md:gap-8">
                <dt className="ls-h3 text-ls-ink">{role}</dt>
                <dd className="max-w-[60ch] text-ls-ink-2">{text}</dd>
              </div>
            ))}
          </dl>
          <p className="ls-small mt-5 max-w-[60ch] text-ls-muted">Admins can share the director&apos;s work: staff, settings, wards, prices and reports.</p>
        </Section>

        <Section id="visit" title="How a visit flows" intro="The queue moves each patient from stage to stage, so everyone can see who is next and who is waiting.">
          <StepList steps={VISIT} />
          <p className="mt-10 max-w-[60ch] text-ls-ink-2">
            If the doctor admits the patient, the front desk gives them a bed with a deposit, and each day in the bed is added to the bill.
          </p>
        </Section>

        <Section id="features" tone="surface" title="Everything a clinic does in a day">
          <FeatureList items={FEATURES} />
        </Section>

        <Section id="privacy" title="Records stay with the people treating the patient">
          <div className="mt-10 grid gap-10 md:grid-cols-[minmax(0,1fr)_minmax(0,24rem)] md:gap-12 lg:grid-cols-[minmax(0,1fr)_minmax(0,28rem)]">
            <ul className="space-y-6">
              <li>
                <h3 className="ls-h4 text-ls-ink">Only doctors and nurses see clinical notes</h3>
                <p className="mt-1 max-w-[52ch] text-ls-ink-2">
                  Allergies, notes, prescriptions and results are for clinical staff. The front desk and the cashier work with names, numbers,
                  appointments and bills, never the notes.
                </p>
              </li>
              <li>
                <h3 className="ls-h4 text-ls-ink">Every opening is written down</h3>
                <p className="mt-1 max-w-[52ch] text-ls-ink-2">
                  The access log keeps who opened a record, when, and which part. The medical director reads it on the patient&apos;s page, and
                  patients see the same log in their portal.
                </p>
              </li>
              <li>
                <h3 className="ls-h4 text-ls-ink">Emergencies need a reason</h3>
                <p className="mt-1 max-w-[52ch] text-ls-ink-2">
                  A doctor or nurse who isn&apos;t caring for the patient gives a reason before the record opens, and the medical director is told.
                </p>
              </li>
              <li>
                <h3 className="ls-h4 text-ls-ink">The records are your clinic&apos;s</h3>
                <p className="mt-1 max-w-[52ch] text-ls-ink-2">Patients&apos; records belong to your clinic. LinkSuite never sells them.</p>
              </li>
            </ul>
            <div className="flex h-[24rem] justify-center overflow-hidden rounded-t-ls-panel bg-app-wash px-4 pt-6 md:self-end">
              <Drawn
                label="The access log for Chinedu Eze, CL-000123: a doctor and a nurse caring for him, an emergency opening at night with its reason, and his own read through the portal."
                className="h-full w-full max-w-[24rem]"
              >
                <div className="h-full overflow-hidden rounded-t-[10px] border border-b-0 border-rule bg-gauze">
                  <AccessLogScreen />
                </div>
              </Drawn>
            </div>
          </div>
        </Section>

        <Section
          id="plans"
          tone="concrete"
          title="Free to start. Upgrade inside CareLink as you grow."
          intro="Start on the free plan with your outpatients. When you open wards, take payments online or turn on the patient portal, the medical director chooses a plan in Settings and sees its price in naira before paying."
        >
          <PlansBlock appName={app.name} free={app.free} paid={app.paid} start={{ href: app.startPath, label: "Start CareLink free" }} />
        </Section>

        <Section id="faq" title="Questions clinics ask">
          <Faq items={FAQ} />
        </Section>

        <FinalCta
          title="Set up your clinic today, free."
          text="Add your price list, invite your staff and register your first patient."
          action={{ href: app.startPath, label: "Start CareLink free" }}
        />
      </main>

      <SuiteFooter
        app={app}
        links={[
          { href: "/#plans", label: "Plans" },
          { href: "/#faq", label: "Questions" },
          { href: "/login", label: "Staff sign in" },
          { href: "/portal/sign-in", label: "Patient portal" },
        ]}
      />
    </>
  );
}