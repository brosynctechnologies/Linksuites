import type { CSSProperties, ReactNode } from "react";
import { PhoneFrame } from "@/components/suite/phone";
import { cx } from "@/components/suite/ui";

/**
 * SchoolLink's real screens, redrawn in HTML for the public pages, in the app's own faces and colours (globals.css). Each mirrors
 * the page it names:
 * - the report card: the ruled sheet with the margin line (results/students/[studentId], the printed card);
 * - the parent's Fees page, with an invoice and Pay now (app/parent/fees/page.tsx inside parent-shell.tsx);
 * - the bursar's invoice (app/app/fees/invoices/[id]/page.tsx);
 * - the score sheet (app/app/scores/page.tsx);
 * - the class register, offline (app/app/attendance/[classId]/[date]/page.tsx);
 * - the parent's home (app/parent/page.tsx).
 * Names and naira amounts are sample school data (a school's own fees), never LinkSuite prices.
 */

const tiny = "text-[0.6875rem] leading-snug";
const small = "text-[0.75rem] leading-snug";

/** One sample pupil, used across the screens so they read as one school's term. */
const PUPIL = { name: "Adaeze Okafor", className: "JSS 2A", school: "Greenfield College, Ibadan" };

const CARD = [
  { subject: "English Language", ca1: 17, ca2: 16, exam: 48, grade: "A1" },
  { subject: "Mathematics", ca1: 15, ca2: 18, exam: 41, grade: "B2" },
  { subject: "Basic Science", ca1: 14, ca2: 13, exam: 39, grade: "B3" },
  { subject: "Civic Education", ca1: 12, ca2: 15, exam: 35, grade: "C4" },
  { subject: "Yoruba", ca1: 13, ca2: 11, exam: 32, grade: "C5" },
];

/** A drawing with a text alternative: the label is read, the drawing is hidden from assistive tech. */
function Drawing({ label, className, style, children }: { label: string; className?: string; style?: CSSProperties; children: ReactNode }) {
  return (
    <div role="img" aria-label={label} className={className} style={style}>
      <div aria-hidden="true" className="h-full font-sans text-ink">
        {children}
      </div>
    </div>
  );
}

function Badge({ tone, children }: { tone: "crest" | "pass" | "brick"; children: ReactNode }) {
  const tones = { crest: "bg-crest-wash text-crest", pass: "bg-pass-wash text-pass", brick: "bg-brick-wash text-brick-dark" };
  return <span className={cx("inline-flex rounded-[4px] px-1.5 py-0.5 text-[0.6875rem] leading-none font-bold whitespace-nowrap", tones[tone])}>{children}</span>;
}

/** The app bar of the parent portal on a phone: the school, the parent, and the child switcher. */
function ParentBar({ notch = true }: { notch?: boolean }) {
  return (
    <div className={cx("bg-blazer-deep px-3.5 pb-2.5 text-white", notch ? "pt-8" : "pt-3")}>
      <p className="slab truncate text-[0.9375rem] leading-tight font-bold">Greenfield College</p>
      <p className={cx(tiny, "text-on-blazer")}>Ngozi</p>
      <p className={cx("mt-2 flex items-center justify-between rounded-[6px] border border-white/20 bg-blazer-deep-2 px-2.5 py-1.5 font-semibold", small)}>
        {PUPIL.name} · {PUPIL.className}
        <svg viewBox="0 0 12 12" width="10" height="10" fill="none" stroke="currentColor" strokeWidth="1.8">
          <path d="m3 4.5 3 3 3-3" />
        </svg>
      </p>
    </div>
  );
}

// ---------- Hero ----------

/** The printed report card on its ruled sheet. */
export function ReportCardSheet({ className, style }: { className?: string; style?: CSSProperties }) {
  return (
    <Drawing
      label={`A SchoolLink report card for ${PUPIL.name}, ${PUPIL.className}, first term: CA1, CA2 and exam scores, totals and grades from A1 to C5, position 4 of 32.`}
      className={className}
      style={style}
    >
      <div className="relative overflow-hidden rounded-[8px] border border-rule bg-surface">
        <span className="absolute inset-y-0 left-3 w-0.5 bg-margin opacity-75" />
        <div className="flex items-center gap-2.5 border-b border-rule py-2.5 pr-3 pl-6">
          <span className="slab flex h-8 w-8 shrink-0 items-center justify-center rounded-[6px] bg-blazer text-[0.8125rem] font-bold text-white">GC</span>
          <span className="min-w-0">
            <span className="slab block truncate text-[0.875rem] leading-tight font-bold">{PUPIL.school}</span>
            <span className={cx("block text-muted", tiny)}>Report card, first term 2026/2027</span>
          </span>
        </div>
        <div className="flex items-baseline justify-between gap-2 border-b border-rule py-2 pr-3 pl-6">
          <span className="slab text-[1rem] font-bold">{PUPIL.name}</span>
          <span className={cx("text-muted", tiny)}>{PUPIL.className}, 4th of 32</span>
        </div>
        <table className={cx("w-full", tiny)}>
          <thead>
            <tr className="text-left text-muted">
              <th className="py-1.5 pl-6 font-bold">Subject</th>
              <th className="px-1 py-1.5 text-right font-bold">CA1</th>
              <th className="px-1 py-1.5 text-right font-bold">CA2</th>
              <th className="px-1 py-1.5 text-right font-bold">Exam</th>
              <th className="px-1 py-1.5 text-right font-bold">Total</th>
              <th className="py-1.5 pr-3 pl-1 text-right font-bold">Grade</th>
            </tr>
          </thead>
          <tbody>
            {CARD.map((r) => (
              <tr key={r.subject} className="border-t border-rule">
                <td className="py-1.5 pl-6">{r.subject}</td>
                <td className="tnum px-1 py-1.5 text-right">{r.ca1}</td>
                <td className="tnum px-1 py-1.5 text-right">{r.ca2}</td>
                <td className="tnum px-1 py-1.5 text-right">{r.exam}</td>
                <td className="tnum px-1 py-1.5 text-right font-bold">{r.ca1 + r.ca2 + r.exam}</td>
                <td className="slab py-1.5 pr-3 pl-1 text-right text-[0.8125rem] font-bold text-blazer">{r.grade}</td>
              </tr>
            ))}
          </tbody>
        </table>
        <div className={cx("border-t border-rule py-2.5 pr-3 pl-6", tiny)}>
          <p className="text-muted">Class teacher</p>
          <p>A careful, hardworking term. Keep reading every evening.</p>
          <div className="mt-2 flex items-end justify-between gap-3">
            <span className="text-muted">Next term begins 5 Jan 2027</span>
            <span className="flex flex-col items-end">
              <svg viewBox="0 0 70 18" width="56" height="15" fill="none" stroke="#1f1b24" strokeWidth="1.4" strokeLinecap="round">
                <path d="M2 13c5-9 9-10 10-5s-2 7 1 4 6-9 9-6-1 7 3 5 5-6 9-4 3 4 7 2 8-3 12-3 8 1 14 0" />
              </svg>
              <span className="text-muted">Principal</span>
            </span>
          </div>
        </div>
      </div>
    </Drawing>
  );
}

/** The parent's Fees page: what's owed, the term's invoice, Pay now, and the online payment already made. */
function ParentFeesScreen() {
  const lines = [
    ["Tuition", "₦150,000"],
    ["Books and stationery", "₦20,000"],
    ["Sports and uniform", "₦10,000"],
    ["PTA levy", "₦5,000"],
  ];
  return (
    <div className="bg-ruled pb-6 font-sans text-ink">
      <ParentBar />
      <div className="space-y-2.5 px-3 pt-3">
        <div>
          <p className="text-[1.0625rem] leading-tight font-semibold">Fees</p>
          <p className={cx("text-muted", tiny)}>
            {PUPIL.name} · {PUPIL.className}
          </p>
        </div>
        <div className="relative overflow-hidden rounded-[8px] border border-rule bg-surface">
          <span className="absolute inset-y-0 left-3 w-0.5 bg-margin opacity-75" />
          <div className="py-2.5 pr-3 pl-6">
            <p className={cx("flex items-center justify-between gap-2 font-bold", small)}>
              Invoice INV-0142
              <Badge tone="crest">Part paid</Badge>
            </p>
            <p className={cx("tnum text-muted", tiny)}>First term · Due 14 Sep 2026</p>
            <div className="mt-2 flex items-center justify-between gap-2">
              <span className="min-w-0">
                <span className="slab tnum block text-[1.25rem] leading-tight font-bold">₦92,500</span>
                <span className={cx("tnum block text-muted", tiny)}>to pay, of ₦185,000</span>
              </span>
              <span className="flex min-h-9 items-center rounded-[6px] bg-blazer px-3 text-[0.8125rem] font-semibold text-white">Pay now</span>
            </div>
            <ul className={cx("mt-2.5 space-y-0.5 text-ink-2", tiny)}>
              {lines.map(([d, a]) => (
                <li key={d} className="flex justify-between gap-2">
                  <span>{d}</span>
                  <span className="tnum">{a}</span>
                </li>
              ))}
            </ul>
            <p className={cx("mt-2 border-t border-rule-soft pt-2 text-ink-2", tiny)}>
              <span className="tnum font-bold text-ink">₦92,500</span> · Online · <span className="tnum">2 Sep 2026</span>
            </p>
            <p className={cx("font-semibold text-blazer", tiny)}>Receipt RCT-0087</p>
          </div>
        </div>
      </div>
    </div>
  );
}

/**
 * The hero: the report card on paper, and the parent's phone rising from the floor over its corner, open on the term's
 * invoice with Pay now. The paint sheen crosses the card, then the phone, once.
 */
export function HeroStage() {
  return (
    <div className="relative h-[26rem] overflow-hidden rounded-ls-panel bg-app-wash sm:h-[28rem]">
      <div className="absolute top-5 left-4 w-[17.5rem] sm:top-8 sm:left-8 sm:w-[21rem]">
        <ReportCardSheet className="ls-paint rounded-[8px]" style={{ "--ls-i": 0 } as CSSProperties} />
      </div>
      <PhoneFrame
        className="absolute top-24 right-3 sm:top-14 sm:right-10"
        label={`SchoolLink's parent portal on a phone: ${PUPIL.name}'s first term invoice, ₦92,500 still to pay of ₦185,000, with a Pay now button and last week's online payment and receipt.`}
      >
        <div className="ls-paint" style={{ "--ls-i": 1 } as CSSProperties}>
          <ParentFeesScreen />
        </div>
      </PhoneFrame>
    </div>
  );
}

// ---------- Feature crops ----------

/** From 640 to 767px a feature stage has 244px inside its padding, so the 248px phone narrows to 240px there. */
const featurePhone = "sm:max-md:w-[15rem]";

/** A sheet of the app pulled up from the bottom of its stage. */
function Sheet({ label, children }: { label: string; children: ReactNode }) {
  return (
    <Drawing label={label} className="w-full max-w-[21rem] self-end">
      <div className="rounded-t-[8px] border border-b-0 border-rule bg-surface">{children}</div>
    </Drawing>
  );
}

/** The bursar's view of the same invoice: figures, instalments and the payment, with its receipt. */
export function BursarInvoiceCrop() {
  const figures = [
    ["Total", "₦185,000"],
    ["Paid", "₦92,500"],
    ["Balance", "₦92,500"],
  ];
  return (
    <Sheet label="SchoolLink's invoice page for the bursar: INV-0142, part paid, with the total, what's paid, the balance, two instalments and an online payment with its receipt.">
      <div className="flex items-center justify-between gap-2 border-b border-rule-soft px-3.5 py-2.5">
        <span className="min-w-0">
          <span className="block truncate text-[0.875rem] font-semibold">Invoice INV-0142</span>
          <span className={cx("block text-muted", tiny)}>
            {PUPIL.name}, {PUPIL.className}
          </span>
        </span>
        <Badge tone="crest">Part paid</Badge>
      </div>
      <dl className="grid grid-cols-3 gap-2 px-3.5 pt-3">
        {figures.map(([k, v], i) => (
          <div key={k} className="rounded-[6px] border border-rule px-2 py-1.5">
            <dt className={cx("text-muted", tiny)}>{k}</dt>
            <dd className={cx("tnum", i === 2 ? "slab text-[0.9375rem] font-bold" : "text-[0.8125rem] font-semibold")}>{v}</dd>
          </div>
        ))}
      </dl>
      <p className={cx("px-3.5 pt-2.5 text-muted", tiny)}>Instalments: ₦92,500 due 14 Sep 2026 · ₦92,500 due 12 Oct 2026</p>
      <div className="mx-3.5 mt-2.5 border-t border-rule-soft pt-2 pb-5">
        <p className={cx("font-semibold", small)}>Payments</p>
        <div className={cx("mt-1 flex items-center justify-between gap-2", tiny)}>
          <span className="text-ink-2">
            <span className="tnum font-bold text-ink">₦92,500</span> · Online · <span className="tnum">2 Sep 2026</span>
          </span>
          <span className="font-semibold text-blazer">Receipt RCT-0087</span>
        </div>
      </div>
    </Sheet>
  );
}

/** The score sheet a subject teacher fills in: CA1, CA2 and exam, with the total worked out. */
export function ScoreSheetCrop() {
  const rows = [
    { name: "Adaeze Okafor", no: "GC/2025/017", s: [15, 18, 41] },
    { name: "Ibrahim Sani", no: "GC/2025/021", s: [15, 14, 39] },
    { name: "Funmi Adeyemi", no: "GC/2025/008", s: [18, 17, 52] },
    { name: "Emeka Obi", no: "GC/2025/030", s: [12, 13, 33] },
  ];
  return (
    <Sheet label="SchoolLink's score sheet for JSS 2A Mathematics: each student's CA1, CA2 and exam scores, with totals out of 100 added up as they're typed.">
      <p className={cx("px-3 pt-2.5 pb-2 text-muted", tiny)}>JSS 2A, Mathematics. All scores saved.</p>
      <table className={cx("w-full", tiny)}>
        <thead>
          <tr className="border-y border-rule bg-slate-50 text-left font-bold text-muted">
            <th className="px-3 py-1.5">Student</th>
            <th className="px-1 py-1.5 text-right">CA1</th>
            <th className="px-1 py-1.5 text-right">CA2</th>
            <th className="px-1 py-1.5 text-right">Exam</th>
            <th className="px-3 py-1.5 text-right">Total</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => (
            <tr key={r.no} className="border-b border-rule-soft">
              <td className="px-3 py-1.5">
                <span className="block font-bold">{r.name}</span>
                <span className="tnum block text-[0.625rem] text-muted">{r.no}</span>
              </td>
              {r.s.map((n, i) => (
                <td key={i} className="px-1 py-1.5 text-right">
                  <span className="tnum ml-auto block w-9 rounded-[5px] border border-field px-1.5 py-1 text-right">{n}</span>
                </td>
              ))}
              <td className="slab tnum px-3 py-1.5 text-right text-[0.875rem] font-bold">{r.s[0] + r.s[1] + r.s[2]}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </Sheet>
  );
}

/** The class register on a teacher's phone, taken with no connection and waiting to send. */
export function RegisterPhone() {
  const marks = [
    { name: "Adaeze Okafor", no: "GC/2025/017", mark: "Present" },
    { name: "Tunde Bello", no: "GC/2025/012", mark: "Absent" },
    { name: "Funmi Adeyemi", no: "GC/2025/008", mark: "Present" },
  ];
  const tone: Record<string, string> = { Present: "border-pass bg-pass text-white", Absent: "border-brick bg-brick text-white" };
  return (
    <PhoneFrame
      className={featurePhone}
      label="SchoolLink's class register on a teacher's phone with no connection: JSS 2A marked present and absent, saved on the phone and waiting to send."
    >
      <div className="bg-ruled pb-6 font-sans text-ink">
        <div className="bg-blazer-deep px-3.5 pt-8 pb-2.5 text-white">
          <p className="slab text-[0.9375rem] leading-tight font-bold">JSS 2A register</p>
          <p className={cx("tnum text-on-blazer", tiny)}>Tue 15 Sep 2026</p>
        </div>
        <div className="space-y-2 px-3 pt-2.5">
          <p className={cx("rounded-[6px] border-l-[3px] border-crest bg-crest-wash px-2.5 py-1.5", tiny)}>Saved on this phone. It will send when you&apos;re back online.</p>
          <div className="relative overflow-hidden rounded-[8px] border border-rule bg-surface">
            <span className="absolute inset-y-0 left-3 w-0.5 bg-margin opacity-75" />
            <ul>
              {marks.map((m, i) => (
                <li key={m.no} className={cx("py-2 pr-2.5 pl-6", i > 0 && "border-t border-rule")}>
                  <p className={cx("font-bold", small)}>{m.name}</p>
                  <p className="tnum text-[0.625rem] text-muted">{m.no}</p>
                  <div className="mt-1.5 grid grid-cols-3 gap-1">
                    {["Present", "Absent", "Late"].map((s) => (
                      <span
                        key={s}
                        className={cx(
                          "rounded-[6px] border py-1 text-center text-[0.6875rem] font-semibold",
                          m.mark === s ? tone[s] : "border-rule bg-surface text-slate-700",
                        )}
                      >
                        {s}
                      </span>
                    ))}
                  </div>
                </li>
              ))}
            </ul>
          </div>
        </div>
      </div>
    </PhoneFrame>
  );
}

/** The parent's home: this term's result and attendance for the child. */
export function ParentHomePhone() {
  return (
    <PhoneFrame
      className={featurePhone}
      label={`SchoolLink's parent portal on a phone: ${PUPIL.name}'s first term result, 4th of 32 in ${PUPIL.className}, and attendance, present 58 of 61 days.`}
    >
      <div className="bg-ruled pb-6 font-sans text-ink">
        <ParentBar />
        <div className="space-y-2 px-3 pt-3">
          <div className="rounded-[8px] border border-rule bg-surface px-3 py-2.5">
            <p className={cx("flex justify-between font-semibold", small)}>
              Results <span className="text-blazer">See all</span>
            </p>
            <p className={cx("mt-0.5", small)}>First term, 2026/2027</p>
            <p className={cx("mt-1", small)}>
              <span className="font-bold">{PUPIL.className}</span> <span className="text-muted">· average 71.6, 4th of 32</span>
            </p>
          </div>
          <div className="rounded-[8px] border border-rule bg-surface px-3 py-2.5">
            <p className={cx("font-semibold", small)}>Attendance</p>
            <p className={cx("mt-0.5", small)}>
              Present <span className="tnum font-bold">58</span> of <span className="tnum">61</span> days in {PUPIL.className}, First term.
            </p>
          </div>
          <div className="grid grid-cols-2 gap-1.5">
            {["Results", "Fees", "Homework", "Messages"].map((t) => (
              <span key={t} className={cx("rounded-[8px] border border-rule bg-surface px-2.5 py-2 font-bold", small)}>
                {t}
              </span>
            ))}
          </div>
        </div>
      </div>
    </PhoneFrame>
  );
}
