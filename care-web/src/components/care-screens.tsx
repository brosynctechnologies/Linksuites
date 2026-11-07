import type { ReactNode } from "react";
import { cx } from "./styles";

/**
 * CareLink's real screens, redrawn in HTML for the public pages so they need no image files. Each mirrors a staff or portal page in the
 * app's own palette and Red Hat faces, and follows one sample patient's wristband, CL-000123, the way staff check a real band:
 * - the queue (src/app/app/queue/page.tsx), the consultation's patient banner and allergy warning (components/clinical-parts.tsx),
 *   the bed board (src/app/app/beds/page.tsx) and the desk receipt (the API's receipt PDF);
 * - the duplicate check (src/app/app/patients/new/page.tsx), the dose chart (components/ward-care.tsx), a bill (src/app/app/bills/[id]),
 *   the access log (src/app/app/patients/[id]/access-log) and the portal home (src/app/portal/(app)/page.tsx).
 * The drawings are decorative: callers wrap each in role="img" with a label and mark it aria-hidden. Naira amounts are a clinic's own
 * charges, not LinkSuite prices.
 */

const tiny = "text-[0.6875rem] leading-snug";
const micro = "text-[0.625rem] leading-tight";

const CLINIC = "Lifeline Clinic, Surulere";

/** The wristband, at drawing scale (the app's `Wristband`). */
function Band({ n, large }: { n: string; large?: boolean }) {
  return (
    <span
      className={cx(
        "tnum inline-flex items-center gap-1 rounded-full border border-scrub-line bg-scrub-wash font-bold text-scrub-dark",
        large ? "py-0.5 pr-2.5 pl-1 text-[0.8125rem]" : "py-px pr-1.5 pl-0.5 text-[0.6875rem]",
      )}
    >
      <span className={cx("rounded-full border border-scrub-line bg-surface", large ? "h-2.5 w-2.5" : "h-2 w-2")} />
      {n}
    </span>
  );
}

const tones = {
  scrub: "bg-scrub-wash text-scrub-dark",
  pass: "bg-pass-wash text-pass",
  iodine: "bg-iodine-wash text-iodine",
  arterial: "bg-arterial-wash text-arterial-dark",
  neutral: "bg-gauze-2 text-ink-2",
};

/** A status word, as the app's `Badge` draws it. */
function Tag({ tone, children }: { tone: keyof typeof tones; children: ReactNode }) {
  return <span className={cx("inline-flex rounded-[4px] px-1.5 py-px font-bold whitespace-nowrap", micro, tones[tone])}>{children}</span>;
}

/** The Theatre bar at the top of every staff screen: the band mark and the clinic's name. */
function TheatreBar() {
  return (
    <div className="flex items-center gap-1.5 bg-scrub-deep px-3 py-2 text-white">
      <svg viewBox="0 0 22 14" width="16" height="10">
        <rect x="1" y="1" width="20" height="12" rx="6" fill="#9fe0d5" />
        <circle cx="7" cy="7" r="2.2" fill="#0a3a36" />
      </svg>
      <span className={cx("truncate font-bold", tiny)}>{CLINIC}</span>
    </div>
  );
}

function Title({ children, sub }: { children: ReactNode; sub?: ReactNode }) {
  return (
    <div className="px-3 pt-2.5 pb-2">
      <p className="display text-[0.9375rem] leading-tight font-bold text-ink">{children}</p>
      {sub && <p className={cx("mt-0.5 text-muted", micro)}>{sub}</p>}
    </div>
  );
}

// ---------- The hero: one patient through the clinic ----------

/** Front desk: the day's queue, the patients waiting for the doctor. */
export function QueueScreen() {
  const rows = [
    { n: "CL-000123", name: "Chinedu Eze", meta: "Male, 41. Arrived 09:12, walk-in", wait: "12 min" },
    { n: "CL-000140", name: "Halima Musa", meta: "Female, 29. Arrived 09:20, booked", wait: "8 min", urgent: true },
    { n: "CL-000087", name: "Tobi Adeyemi", meta: "Male, 6. Arrived 09:31, walk-in", wait: "3 min" },
  ];
  return (
    <div className="h-full bg-gauze">
      <TheatreBar />
      <Title sub="Today. 6 waiting. Updates every 30 seconds.">Queue</Title>
      <div className="px-3">
        <p className={cx("mb-1 font-bold text-ink", tiny)}>
          Waiting for the doctor <span className="font-normal text-muted">(3)</span>
        </p>
        <ul className="divide-y divide-rule-soft rounded-[6px] border border-rule bg-surface">
          {rows.map((r) => (
            <li key={r.n} className="px-2.5 py-2">
              <span className="flex items-center gap-1.5">
                <Band n={r.n} />
                <span className={cx("min-w-0 truncate font-bold text-ink", tiny)}>{r.name}</span>
                {r.urgent && <Tag tone="iodine">Urgent</Tag>}
                <span className={cx("tnum ml-auto text-ink-2", micro)}>{r.wait}</span>
              </span>
              <span className={cx("tnum mt-0.5 block truncate text-muted", micro)}>{r.meta}</span>
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}

/** Doctor: the consultation's patient summary, the allergy sticker and the warning when a drug matches it. */
export function ConsultationScreen() {
  return (
    <div className="h-full bg-gauze">
      <TheatreBar />
      <Title>Consultation</Title>
      <div className="space-y-2 px-3">
        <div className="rounded-[6px] border border-rule bg-surface p-2.5">
          <p className="flex flex-wrap items-center gap-x-1.5 gap-y-1">
            <Band n="CL-000123" large />
            <span className="display text-[0.8125rem] font-bold text-ink">Chinedu Eze</span>
            <span className={cx("text-muted", micro)}>Male, 41</span>
          </p>
          <div className="mt-2 rounded-[5px] border-2 border-arterial bg-arterial-wash px-2 py-1.5">
            <p className={cx("font-bold text-arterial-dark", tiny)}>Allergic to</p>
            <p className="mt-1 flex flex-wrap gap-1">
              {["Penicillin", "Ibuprofen"].map((a) => (
                <span key={a} className={cx("rounded-[3px] bg-surface px-1.5 py-px font-bold text-arterial-dark", tiny)}>
                  {a}
                </span>
              ))}
            </p>
          </div>
          <p className={cx("tnum mt-1.5 text-ink-2", micro)}>Latest vitals: BP 128/84, 36.9 °C, pulse 82</p>
        </div>
        <div className="rounded-[6px] border border-rule bg-surface p-2.5">
          <p className={cx("font-bold text-ink", tiny)}>Recorded allergy</p>
          <p className={cx("mt-1 rounded-[5px] border-2 border-arterial bg-arterial-wash px-2 py-1.5 font-bold text-arterial-dark", tiny)}>
            Chinedu Eze is recorded as allergic to Ibuprofen.
          </p>
          <p className={cx("mt-1.5 text-ink-2", micro)}>Ibuprofen 400 mg, three times a day, 5 days</p>
        </div>
      </div>
    </div>
  );
}

/** Ward: the bed board, with the patient in their bed and the beds around them. */
export function BedBoardScreen() {
  return (
    <div className="h-full bg-gauze">
      <TheatreBar />
      <Title sub="3 beds free, 4 occupied, 1 being cleaned">Beds</Title>
      <div className="space-y-1.5 px-3">
        <p className={cx("font-bold text-ink", tiny)}>
          Ward A <span className="font-normal text-muted">Male</span>
        </p>
        <div className="rounded-[6px] border border-scrub-line bg-surface p-2">
          <p className="flex items-center justify-between gap-1">
            <span className="display tnum text-[0.8125rem] font-bold text-ink">A2</span>
            <Tag tone="scrub">Occupied</Tag>
          </p>
          <p className="mt-1">
            <Band n="CL-000123" />
          </p>
          <p className={cx("mt-0.5 truncate font-bold text-ink", tiny)}>Chinedu Eze</p>
          <p className={cx("tnum truncate text-muted", micro)}>Male, 41. Dr Bello</p>
        </div>
        <div className="flex items-center justify-between gap-1 rounded-[6px] border border-pass/40 bg-surface px-2 py-1.5">
          <span className="display tnum text-[0.8125rem] font-bold text-ink">A3</span>
          <Tag tone="pass">Free</Tag>
        </div>
        <div className="flex items-center justify-between gap-1 rounded-[6px] border border-iodine/50 bg-iodine-wash/40 px-2 py-1.5">
          <span className="display tnum text-[0.8125rem] font-bold text-ink">A4</span>
          <Tag tone="iodine">Cleaning</Tag>
        </div>
      </div>
    </div>
  );
}

/** Cashier: the receipt a desk payment prints, as the API's receipt PDF lays it out. */
export function ReceiptScreen() {
  const rows: [string, ReactNode][] = [
    ["Patient", "Chinedu Eze (CL-000123)"],
    ["Bill", "BILL-2026-00042"],
    ["Amount received", "₦18,500"],
    ["Method", "POS"],
    ["Balance on the bill", "₦0"],
  ];
  return (
    <div className="h-full bg-surface px-3 pt-3 text-ink">
      <p className="display text-[0.8125rem] leading-tight font-bold">Lifeline Clinic</p>
      <p className={cx("text-muted", micro)}>Surulere, Lagos</p>
      <div className="mt-2 flex flex-wrap items-baseline justify-between gap-x-2 border-t border-ink/30 pt-2">
        <p className={cx("font-bold [font-variant-numeric:tabular-nums]", tiny)}>Receipt RCP-2026-00117</p>
        <p className={cx("tnum text-muted", micro)}>9 Nov 2026</p>
      </div>
      <dl className={cx("mt-1.5", micro)}>
        {rows.map(([k, v]) => (
          <div key={k} className="border-b border-rule-soft py-1">
            <dt className="font-bold text-ink-2">{k}</dt>
            <dd className="tnum truncate">{v}</dd>
          </div>
        ))}
      </dl>
      <p className={cx("mt-2 text-center text-muted", micro)}>Thank you.</p>
    </div>
  );
}

// ---------- Features ----------

/** The frame for a feature crop: a sheet rising from the floor of its stage. */
function Crop({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div className={cx("h-[12.5rem] w-full max-w-[20rem] overflow-hidden rounded-t-[10px] border border-b-0 border-rule bg-gauze font-sans text-ink", className)}>
      {children}
    </div>
  );
}

/** Registering a patient who may already have a number. */
export function DuplicateCrop() {
  return (
    <Crop>
      <TheatreBar />
      <Title>Register a patient</Title>
      <div className="space-y-2 px-3">
        <div className={cx("rounded-[5px] border-l-[3px] border-iodine bg-iodine-wash px-2 py-1.5 text-ink", tiny)}>
          <p className="font-bold">This patient may already be registered.</p>
          <p className="mt-0.5">A patient has the same name and date of birth, or the same phone.</p>
        </div>
        <div className="rounded-[6px] border border-rule bg-surface px-2.5 py-2">
          <p className="flex flex-wrap items-center gap-1.5">
            <Band n="CL-000123" />
            <span className={cx("font-bold", tiny)}>Chinedu Eze</span>
          </p>
          <p className={cx("tnum mt-0.5 text-muted", micro)}>Male, born 14 Mar 1985 (41), 0803 412 7790</p>
        </div>
      </div>
    </Crop>
  );
}

/** The ward's dose chart for one admission. */
export function DosesCrop() {
  const doses: [string, string, keyof typeof tones, string][] = [
    ["06:00", "Ceftriaxone 1 g, IV", "pass", "Given"],
    ["12:00", "Paracetamol 1 g, oral", "pass", "Given"],
    ["14:00", "Metronidazole 500 mg, IV", "neutral", "Due"],
    ["18:00", "Paracetamol 1 g, oral", "neutral", "Due"],
  ];
  return (
    <Crop>
      <TheatreBar />
      <div className="flex items-center gap-1.5 px-3 pt-2.5 pb-2">
        <Band n="CL-000123" />
        <span className="display text-[0.8125rem] font-bold">Ward A, bed A2</span>
      </div>
      <div className="mx-3 rounded-[6px] border border-rule bg-surface">
        <p className={cx("border-b border-rule bg-gauze px-2.5 py-1.5 font-bold", tiny)}>Medication chart, today</p>
        <ul className="divide-y divide-rule-soft">
          {doses.map(([t, drug, tone, word]) => (
            <li key={t + drug} className={cx("flex items-center gap-2 px-2.5 py-1.5", tiny)}>
              <span className="tnum w-9 shrink-0 text-muted">{t}</span>
              <span className="min-w-0 flex-1 truncate">{drug}</span>
              <Tag tone={tone}>{word}</Tag>
            </li>
          ))}
        </ul>
      </div>
    </Crop>
  );
}

/** A visit's bill, filled from the price list. */
export function BillCrop() {
  const lines: [string, string][] = [
    ["Consultation, general", "₦8,000"],
    ["Full blood count", "₦6,500"],
    ["Malaria parasite test", "₦2,500"],
    ["Artemether-lumefantrine", "₦1,500"],
  ];
  return (
    <Crop>
      <TheatreBar />
      <div className="flex items-center gap-1.5 px-3 pt-2.5 pb-2">
        <span className="display tnum text-[0.8125rem] font-bold">BILL-2026-00042</span>
        <Tag tone="arterial">Unpaid</Tag>
      </div>
      <div className="mx-3 rounded-[6px] border border-rule bg-surface">
        <ul className="divide-y divide-rule-soft">
          {lines.map(([item, amount]) => (
            <li key={item} className={cx("flex justify-between gap-2 px-2.5 py-1.5", tiny)}>
              <span className="truncate">{item}</span>
              <span className="tnum">{amount}</span>
            </li>
          ))}
        </ul>
        <p className={cx("flex justify-between border-t border-ink/40 px-2.5 py-1.5 font-bold", tiny)}>
          <span>Balance</span>
          <span className="tnum">₦18,500</span>
        </p>
      </div>
    </Crop>
  );
}

/** The patient portal's home on a phone (drawn inside the suite's PhoneFrame by the caller). */
export function PortalScreen() {
  return (
    <div className="bg-gauze pb-4 font-sans text-ink">
      <div className="bg-scrub-deep px-4 pt-7 pb-2.5 text-white">
        <p className={cx("font-bold", tiny)}>{CLINIC}</p>
        <p className={cx("text-on-scrub", micro)}>Patient portal</p>
      </div>
      <div className="space-y-2 px-3 pt-3">
        <p className="display text-[0.9375rem] font-bold">Hello, Chinedu</p>
        <div className="rounded-[6px] border border-rule bg-surface px-2.5 py-2">
          <p className={cx("font-bold text-muted", micro)}>Next appointment</p>
          <p className="display text-[0.8125rem] font-bold">Thu 19 Nov</p>
          <p className={cx("tnum text-ink-2", micro)}>10:30 with Dr Bello</p>
        </div>
        <div className="rounded-[6px] border border-rule bg-surface px-2.5 py-2">
          <p className={cx("font-bold text-muted", micro)}>Bills</p>
          <p className="display tnum text-[0.8125rem] font-bold">₦18,500 to pay</p>
          <span className={cx("mt-1.5 flex min-h-7 items-center justify-center rounded-[5px] bg-scrub font-bold text-white", micro)}>See and pay bills</span>
        </div>
        <div className={cx("rounded-[6px] border border-rule bg-surface px-2.5 py-2", micro)}>
          <p className="font-bold">Who opened the record</p>
          <p className="text-muted">Every time clinic staff open it, with their name and role.</p>
        </div>
      </div>
    </div>
  );
}

// ---------- Privacy ----------

/** The medical director's access log for one patient. */
export function AccessLogScreen() {
  const rows: { at: string; who: string; role: string; what: string; reason?: string; emergency?: boolean; portal?: boolean }[] = [
    { at: "9 Nov, 09:41", who: "Tunde Bello", role: "Doctor", what: "Consultation" },
    { at: "9 Nov, 09:18", who: "Ngozi Okafor", role: "Nurse", what: "Medication chart" },
    { at: "8 Nov, 22:05", who: "Amaka Obi", role: "Doctor", what: "Patient record", reason: "Called to the ward at night", emergency: true },
    { at: "8 Nov, 19:30", who: "Chinedu E. (self)", role: "", what: "Test results", portal: true },
  ];
  return (
    <div className="bg-gauze pb-4 font-sans text-ink">
      <TheatreBar />
      <div className="flex flex-wrap items-center gap-1.5 px-3 pt-2.5">
        <Band n="CL-000123" />
        <span className="display text-[0.8125rem] font-bold">Chinedu Eze</span>
      </div>
      <Title sub="Every opening of the patient's clinical record, by staff and through the patient portal, newest first.">Who opened this record</Title>
      <ul className="mx-3 divide-y divide-rule-soft rounded-[6px] border border-rule bg-surface">
        {rows.map((r) => (
          <li key={r.at} className="grid grid-cols-[5.5rem_minmax(0,1fr)] gap-x-2 px-2.5 py-2">
            <span className={cx("tnum text-ink-2", micro)}>{r.at}</span>
            <span className="min-w-0">
              <span className={cx("flex flex-wrap items-center gap-1 font-bold", tiny)}>
                {r.who}
                {r.role && <span className="font-normal text-muted">{r.role}</span>}
                {r.portal && <Tag tone="iodine">Portal</Tag>}
                {r.emergency && <Tag tone="arterial">Emergency</Tag>}
              </span>
              <span className={cx("block text-ink-2", micro)}>
                {r.what}
                {r.reason && `. Reason: ${r.reason}`}
              </span>
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}
