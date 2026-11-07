import type { JSX } from "react";
import type { AppKey } from "@/lib/products";

/**
 * One real screen from each app, redrawn in HTML so the hub needs no image files. Each mirrors the app's own page and
 * palette (hex values from that app's globals.css):
 * - OrderLink: the storefront cart and its WhatsApp button (web/src/app/page.tsx, the [slug] store).
 * - InvoiceLink: a sent invoice with Pay now (invoice-web/src/components/invoice-preview.tsx, /i/[token]).
 * - BookLink: the booking page's service, day and time pickers (booking-web/src/app/b/...).
 * - LearnLink: a course's learning path (course-web/src/components/learning-path.tsx).
 * - PropertyLink: a listing with the cost to move in (property-web/src/components/listing-card.tsx, listing page).
 * - SchoolLink: a report card in the parent portal (school-web/src/app/parent, the ruled sheet).
 * - CareLink: the patient wristband and the visit path (care-web/src/app/app/visits, queue).
 * Sample names and naira amounts are a business's own figures, not LinkSuite prices.
 */

const tiny = "text-[0.6875rem] leading-snug";

function WhatsAppGlyph() {
  return (
    <svg viewBox="0 0 24 24" width="13" height="13" fill="currentColor" aria-hidden="true">
      <path d="M12 2a10 10 0 0 0-8.6 15.1L2 22l5-1.3A10 10 0 1 0 12 2Zm0 18.2a8.2 8.2 0 0 1-4.2-1.2l-.3-.2-3 .8.8-2.9-.2-.3A8.2 8.2 0 1 1 12 20.2Zm4.5-6.1c-.2-.1-1.5-.7-1.7-.8-.2-.1-.4-.1-.6.1l-.8 1c-.1.2-.3.2-.5.1a6.7 6.7 0 0 1-3.3-2.9c-.3-.4.2-.4.7-1.4.1-.2 0-.3 0-.4l-.8-1.8c-.2-.5-.4-.4-.6-.4h-.5a1 1 0 0 0-.7.3 3 3 0 0 0-.9 2.2 5.2 5.2 0 0 0 1.1 2.7 11.8 11.8 0 0 0 4.5 4c1.7.7 2.3.8 3.2.6.5-.1 1.5-.6 1.7-1.2.2-.6.2-1.1.2-1.2-.1-.1-.3-.2-.5-.3Z" />
    </svg>
  );
}

function Check({ color }: { color: string }) {
  return (
    <svg viewBox="0 0 16 16" width="12" height="12" fill="none" stroke={color} strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="m3.5 8.5 3 3 6-7" />
    </svg>
  );
}

/** OrderLink: the customer's cart in Mama Tee's store, one tap from WhatsApp. */
function OrderScreen() {
  const cart = [
    { name: "Jollof rice", variant: "Large, qty 2", amount: "₦6,000", tone: "#f3e1cf" },
    { name: "Chapman", variant: "qty 1", amount: "₦1,500", tone: "#f6d9c9" },
  ];
  return (
    <div className="bg-[#f6f7fa] pb-5 text-[#1b1f3b]">
      <div className="flex items-center gap-2 border-b border-[#e3e6ee] bg-white px-4 pt-8 pb-3">
        <span className="flex h-8 w-8 items-center justify-center rounded-[6px] bg-[#8a3b12] text-sm font-bold text-white">M</span>
        <span className="min-w-0">
          <span className="block truncate text-[0.8125rem] font-bold">Mama Tee&apos;s Kitchen</span>
          <span className={`block ${tiny} text-[#5b6079]`}>Your cart</span>
        </span>
      </div>
      <ul className="divide-y divide-[#e3e6ee] bg-white px-4">
        {cart.map((l) => (
          <li key={l.name} className="flex items-center gap-2.5 py-2.5">
            <span className="h-9 w-9 shrink-0 rounded-[6px]" style={{ background: l.tone }} />
            <span className="min-w-0 flex-1">
              <span className="block truncate text-xs font-medium">{l.name}</span>
              <span className={`block ${tiny} text-[#5b6079]`}>{l.variant}</span>
            </span>
            <span className="tnum text-xs font-bold">{l.amount}</span>
          </li>
        ))}
      </ul>
      <dl className={`space-y-1 border-t border-[#e3e6ee] bg-white px-4 py-3 ${tiny}`}>
        <div className="flex justify-between text-[#3b4060]">
          <dt>Delivery (Lekki)</dt>
          <dd className="tnum">₦1,500</dd>
        </div>
        <div className="flex items-baseline justify-between pt-1">
          <dt className="text-xs font-semibold">Total</dt>
          <dd className="tnum text-base font-bold">₦9,000</dd>
        </div>
      </dl>
      <div className="px-3 pt-3">
        <span className="flex min-h-10 items-center justify-center gap-1.5 rounded-[8px] bg-[#1f7a4d] text-xs font-semibold text-white">
          <WhatsAppGlyph />
          Send order on WhatsApp
        </span>
        <p className={`mt-2 text-center ${tiny} text-[#5b6079]`}>Your order is saved before WhatsApp opens.</p>
      </div>
    </div>
  );
}

/** InvoiceLink: the invoice the client opens, with Pay now. */
function InvoiceScreen() {
  return (
    <div className="bg-[#edf0f4] px-3 pt-8 pb-5 text-[#1e2430]">
      <div className="mb-2 flex items-center justify-between px-1">
        <span className="text-xs font-semibold">INV-0042</span>
        <span className={`rounded-full bg-[#e9edfb] px-2 py-0.5 ${tiny} font-semibold text-[#1f37a6]`}>Opened today</span>
      </div>
      <div className="rounded-[6px] bg-white shadow-[0_1px_2px_rgb(30_36_48/0.12)]">
        <div className="h-1.5 rounded-t-[6px] bg-[#2b47c9]" />
        <div className="px-3.5 pt-3 pb-3.5">
          <div className="flex items-start justify-between gap-2">
            <span>
              <span className="block text-[0.8125rem] font-bold">Ada Designs Ltd</span>
              <span className={`block ${tiny} text-[#5b6474]`}>Lekki, Lagos</span>
            </span>
            <span className="text-right">
              <span className="block text-[0.8125rem] font-bold text-[#2b47c9]">Invoice</span>
              <span className={`block ${tiny} text-[#5b6474]`}>Due 15 Oct</span>
            </span>
          </div>
          <p className={`mt-3 ${tiny} text-[#5b6474]`}>Bill to</p>
          <p className="text-xs font-semibold">Tunde Bakare, Bakare Foods</p>
          <table className={`mt-3 w-full ${tiny}`}>
            <tbody>
              <tr className="border-t border-[#d9dee6]">
                <td className="py-1.5">Logo design</td>
                <td className="tnum py-1.5 text-right">₦150,000</td>
              </tr>
              <tr className="border-t border-[#d9dee6]">
                <td className="py-1.5">Business cards × 2</td>
                <td className="tnum py-1.5 text-right">₦22,500</td>
              </tr>
              <tr className="border-t border-[#d9dee6] text-[#5b6474]">
                <td className="py-1.5">VAT 7.5%</td>
                <td className="tnum py-1.5 text-right">₦11,250</td>
              </tr>
              <tr className="border-t-2 border-[#1e2430]">
                <td className="pt-2 text-xs font-bold">Total</td>
                <td className="tnum pt-2 text-right text-sm font-bold">₦183,750</td>
              </tr>
            </tbody>
          </table>
          <span className="mt-4 flex min-h-10 items-center justify-center rounded-[6px] bg-[#2b47c9] text-xs font-semibold text-white">
            Pay now
          </span>
          <p className={`mt-2 text-center ${tiny} text-[#5b6474]`}>Card or bank transfer, through Paystack</p>
        </div>
      </div>
    </div>
  );
}

/** BookLink: Kemi's booking page, Saturday 10:30 picked. */
function BookingScreen() {
  const days = [
    { d: "Thu", n: 12, free: true },
    { d: "Fri", n: 13, free: false },
    { d: "Sat", n: 14, on: true, free: true },
    { d: "Sun", n: 15, free: false },
    { d: "Mon", n: 16, free: true },
  ];
  const times = ["09:30", "10:30", "11:00"];
  return (
    <div className="bg-[#f5f3f7] text-[#2b1838]">
      <div className="h-12 bg-[#c2255c]" />
      <div className="px-4 pb-3">
        <div className="-mt-6 flex h-12 w-12 items-center justify-center rounded-full border-4 border-[#f5f3f7] bg-white text-sm font-extrabold text-[#c2255c]">
          KH
        </div>
        <p className="mt-1 text-[0.9375rem] leading-tight font-extrabold">Kemi&apos;s Hair Studio</p>
        <p className={`${tiny} text-[#6a5e78]`}>Lekki Phase 1</p>
        <div className="mt-3 rounded-[10px] border border-[#e6e1ec] bg-white px-3 py-2">
          <p className={`${tiny} text-[#6a5e78]`}>Service</p>
          <div className="flex items-baseline justify-between gap-2">
            <p className="text-xs font-semibold">Knotless braids, medium</p>
            <p className="tnum text-xs font-semibold">₦25,000</p>
          </div>
          <p className={`${tiny} text-[#6a5e78]`}>3 h, ₦5,000 deposit, with Tunde</p>
        </div>
        <div className="mt-2 rounded-[10px] border border-[#e6e1ec] bg-white px-3 py-2">
          <p className={`${tiny} text-[#6a5e78]`}>When</p>
          <div className="mt-1.5 grid grid-cols-5 gap-1">
            {days.map((x) => (
              <span
                key={x.n}
                className={`flex flex-col items-center rounded-[8px] border py-1 ${
                  x.on ? "border-[#c2255c] bg-[#c2255c] text-white" : x.free ? "border-[#e6e1ec]" : "border-transparent text-[#9a8fa6]"
                }`}
              >
                <span className="text-[0.625rem]">{x.d}</span>
                <span className="tnum text-sm font-bold">{x.n}</span>
              </span>
            ))}
          </div>
          <div className="mt-2 grid grid-cols-3 gap-1">
            {times.map((t) => (
              <span
                key={t}
                className={`tnum rounded-[8px] border py-1.5 text-center text-xs font-semibold ${
                  t === "10:30" ? "border-[#c2255c] bg-[#c2255c] text-white" : "border-[#e6e1ec]"
                }`}
              >
                {t}
              </span>
            ))}
          </div>
        </div>
      </div>
      <div className="bg-white px-4 pt-2.5 pb-5 shadow-[0_-1px_0_#e6e1ec]">
        <p className="rounded-[10px] bg-[#c2255c] py-2.5 text-center text-xs font-semibold text-white">Pay ₦5,000 deposit and book</p>
      </div>
    </div>
  );
}

/** LearnLink: a learner's path through a JAMB physics course. */
function CourseScreen() {
  const sections = [
    {
      title: "Mechanics",
      lessons: [
        { t: "Scalars, vectors and how JAMB asks", m: "14 min", done: true },
        { t: "Equations of motion, past questions", m: "21 min", done: true },
        { t: "Mechanics checkpoint", m: "Quiz, passed", done: true },
      ],
    },
    {
      title: "Waves and optics",
      lessons: [
        { t: "Reflection and refraction", m: "16 min", now: true },
        { t: "Formula sheet", m: "File" },
        { t: "Waves checkpoint", m: "Quiz" },
      ],
    },
  ];
  return (
    <div className="bg-[#f3f6f6] pb-5 text-[#10202b]">
      <div className="bg-white px-4 pt-8 pb-3 shadow-[0_1px_0_#dce3e3]">
        <p className={`${tiny} text-[#4e626c]`}>Tolu&apos;s Physics Academy</p>
        <p className="text-[0.875rem] leading-tight font-semibold">JAMB Physics, the full syllabus</p>
        <div className="mt-2.5 h-1.5 overflow-hidden rounded-full bg-[#e7eded]">
          <div className="h-full w-1/2 rounded-full bg-[#0b6e6e]" />
        </div>
        <p className={`mt-1 ${tiny} text-[#4e626c]`}>3 of 6 done</p>
      </div>
      <div className="space-y-3 px-3 pt-3">
        {sections.map((s) => (
          <div key={s.title} className="rounded-[10px] border border-[#dce3e3] bg-white">
            <p className="border-b border-[#e8eeee] px-3 py-2 text-xs font-semibold">{s.title}</p>
            <ul>
              {s.lessons.map((l) => {
                const now = "now" in l && l.now;
                const done = "done" in l && l.done;
                return (
                  <li key={l.t} className={`flex items-center gap-2 px-3 py-1.5 ${now ? "bg-[rgba(11,110,110,0.09)]" : ""}`}>
                    <span
                      className={`flex h-4 w-4 shrink-0 items-center justify-center rounded-full ${
                        done ? "bg-[#5e9e2f]" : now ? "border-2 border-[#0b6e6e]" : "border border-[#c3cfd1]"
                      }`}
                    >
                      {done && <Check color="#ffffff" />}
                    </span>
                    <span className="min-w-0 flex-1">
                      <span className={`block truncate ${tiny} ${now ? "font-semibold text-[#0b6e6e]" : ""}`}>{l.t}</span>
                      <span className="block text-[0.625rem] text-[#82949b]">{l.m}</span>
                    </span>
                  </li>
                );
              })}
            </ul>
          </div>
        ))}
        <div className="flex items-center gap-2 rounded-[10px] border border-dashed border-[#c3cfd1] px-3 py-2.5">
          <span className="h-4 w-4 shrink-0 rounded-[3px] border border-[#c3cfd1]" />
          <span className={`${tiny} text-[#4e626c]`}>Certificate of completion</span>
        </div>
      </div>
    </div>
  );
}

/** PropertyLink: a listing from a verified agent, with what moving in really costs. */
function PropertyScreen() {
  const costs = [
    ["Rent, 1 year", "₦3,500,000"],
    ["Agency fee", "₦350,000"],
    ["Legal fee", "₦350,000"],
    ["Caution", "₦350,000"],
  ];
  return (
    <div className="bg-[#eef1ec] pb-5 text-[#17201c]">
      <svg viewBox="0 0 248 150" className="block w-full" aria-hidden="true">
        <rect width="248" height="150" fill="#cfe0e6" />
        <rect y="118" width="248" height="32" fill="#9fae9a" />
        <rect x="38" y="34" width="172" height="96" fill="#efe6d6" />
        <rect x="30" y="26" width="188" height="10" fill="#8c5a3c" />
        {[0, 1, 2, 3].map((c) =>
          [0, 1].map((r) => <rect key={`${c}-${r}`} x={52 + c * 40} y={48 + r * 30} width="24" height="18" fill="#5f7d8c" />),
        )}
        <rect x="112" y="104" width="24" height="26" fill="#6b4a33" />
        <rect x="0" y="128" width="248" height="4" fill="#d9d2c3" />
      </svg>
      <div className="bg-white px-4 pt-3 pb-3.5">
        <p className="text-[#33309a]">
          <span className="tnum text-[1.0625rem] font-bold">₦3,500,000</span>
          <span className={`${tiny} text-[#4d5a53]`}> a year</span>
        </p>
        <p className="text-xs font-semibold">2 bedroom flat in a gated close</p>
        <p className={`${tiny} text-[#4d5a53]`}>2 beds, 2 baths. Yaba, Lagos</p>
        <p className={`mt-1.5 flex items-center gap-1.5 ${tiny} text-[#4d5a53]`}>
          Haven Homes
          <span className="inline-flex items-center gap-0.5 rounded-[4px] bg-[#0e7c4a] px-1.5 py-0.5 text-[0.625rem] leading-none font-semibold text-white">
            <Check color="#ffffff" />
            Verified
          </span>
        </p>
      </div>
      <div className="mx-3 mt-3 rounded-[8px] border border-[#d6dcd4] bg-white px-3 py-2.5">
        <p className="text-xs font-semibold">To move in</p>
        <dl className={`mt-1 ${tiny}`}>
          {costs.map(([k, v]) => (
            <div key={k} className="flex justify-between py-0.5 text-[#36423c]">
              <dt>{k}</dt>
              <dd className="tnum">{v}</dd>
            </div>
          ))}
          <div className="mt-1 flex justify-between border-t border-[#d6dcd4] pt-1.5 text-xs font-bold">
            <dt>Total</dt>
            <dd className="tnum">₦4,550,000</dd>
          </div>
        </dl>
      </div>
      <div className="px-3 pt-3">
        <span className="flex min-h-10 items-center justify-center rounded-[8px] bg-[#33309a] text-xs font-semibold text-white">Book an inspection</span>
      </div>
    </div>
  );
}

/** SchoolLink: a child's report card, as a parent sees it. */
function SchoolScreen() {
  const rows = [
    ["English Language", 81, "A1"],
    ["Mathematics", 74, "B2"],
    ["Basic Science", 66, "B3"],
    ["Civic Education", 62, "C4"],
    ["Yoruba", 56, "C5"],
  ] as const;
  return (
    <div className="bg-[#edf1f6] pb-5 text-[#1f1b24]">
      <div className="bg-[#40111f] px-4 pt-8 pb-3 text-white">
        <p className={`${tiny} text-[#e9cfd6]`}>Greenfield College, Ibadan</p>
        <p className="text-[0.875rem] font-bold">Adaeze Okafor, JSS 2A</p>
      </div>
      <div className={`flex gap-3 border-b border-[#c9d5e3] bg-white px-4 ${tiny} font-semibold`}>
        <span className="border-b-2 border-[#6b1d33] py-2 text-[#6b1d33]">Results</span>
        <span className="py-2 text-[#4f5866]">Attendance</span>
        <span className="py-2 text-[#4f5866]">Fees</span>
      </div>
      <div className="relative mx-3 mt-3 overflow-hidden rounded-[6px] border border-[#c9d5e3] bg-white">
        <span className="absolute inset-y-0 left-5 w-px bg-[#d9828f]" />
        <div className="border-b border-[#c9d5e3] py-2 pr-3 pl-7">
          <p className="text-xs font-bold">First term 2026/2027</p>
          <p className={`${tiny} text-[#4f5866]`}>Position 4 of 32</p>
        </div>
        <table className={`w-full ${tiny}`}>
          <thead>
            <tr className="text-left text-[#4f5866]">
              <th className="py-1.5 pl-7 font-semibold">Subject</th>
              <th className="py-1.5 text-right font-semibold">Total</th>
              <th className="py-1.5 pr-3 text-right font-semibold">Grade</th>
            </tr>
          </thead>
          <tbody>
            {rows.map(([s, t, g]) => (
              <tr key={s} className="border-t border-[#dde5ee]">
                <td className="py-1.5 pl-7">{s}</td>
                <td className="tnum py-1.5 text-right font-bold">{t}</td>
                <td className="py-1.5 pr-3 text-right text-xs font-bold text-[#6b1d33]">{g}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className={`mx-4 mt-2.5 ${tiny} text-[#4f5866]`}>Computed from the scores teachers enter.</p>
    </div>
  );
}

/** CareLink: the patient's wristband and the visit, from check-in to paid. */
function CareScreen() {
  const steps = [
    { t: "Check in", d: "09:12, front desk", state: "done" },
    { t: "Vitals", d: "BP 128/84, temp 36.9", state: "done" },
    { t: "Doctor", d: "With Dr Bello now", state: "now" },
    { t: "Bill", d: "From the price list", state: "next" },
    { t: "Paid", d: "Cash, POS or transfer", state: "next" },
  ];
  return (
    <div className="bg-[#eef3f1] pb-5 text-[#14211f]">
      <div className="bg-[#0a3a36] px-4 pt-8 pb-3 text-white">
        <p className="text-[0.8125rem] font-bold">Lifeline Clinic, Surulere</p>
        <p className={`${tiny} text-[#bfe3dc]`}>Queue</p>
      </div>
      <div className="px-3 pt-3">
        <div className="flex items-center gap-2.5 rounded-full border-2 border-[#a7d3cb] bg-white py-1.5 pr-4 pl-2">
          <span className="h-5 w-5 shrink-0 rounded-full border-2 border-[#a7d3cb] bg-[#eef3f1]" />
          <span className="min-w-0">
            <span className="tnum block text-[0.9375rem] leading-tight font-bold text-[#0b5f58]">CL-000123</span>
            <span className={`block truncate ${tiny} text-[#2b3a37]`}>Chinedu Eze, 41, male</span>
          </span>
        </div>
        <ol className="mt-4 rounded-[8px] border border-[#cbd8d4] bg-white px-3 py-3">
          {steps.map((s, i) => (
            <li key={s.t} className="relative grid grid-cols-[1.5rem_minmax(0,1fr)] gap-2.5 pb-3 last:pb-0">
              {i < steps.length - 1 && <span className="absolute top-6 bottom-0 left-[0.7rem] w-0.5 bg-[#a7d3cb]" />}
              <span
                className={`tnum flex h-6 w-6 items-center justify-center rounded-full text-[0.6875rem] font-bold ${
                  s.state === "done"
                    ? "bg-[#0f766e] text-white"
                    : s.state === "now"
                      ? "border-2 border-[#0f766e] bg-[#e3f1ee] text-[#0f766e]"
                      : "border-2 border-[#cbd8d4] bg-white text-[#7b8a86]"
                }`}
              >
                {s.state === "done" ? <Check color="#ffffff" /> : i + 1}
              </span>
              <span>
                <span className="block text-xs font-bold">{s.t}</span>
                <span className={`block ${tiny} text-[#4d5d59]`}>{s.d}</span>
              </span>
            </li>
          ))}
        </ol>
      </div>
    </div>
  );
}

export const screens: Record<AppKey, { label: string; Screen: () => JSX.Element }> = {
  orders: { label: "OrderLink on a phone: a customer's cart from Mama Tee's Kitchen, ready to send as a WhatsApp order.", Screen: OrderScreen },
  invoices: { label: "InvoiceLink on a phone: invoice INV-0042 from Ada Designs, opened by the client, with a Pay now button.", Screen: InvoiceScreen },
  bookings: { label: "BookLink on a phone: booking knotless braids at Kemi's Hair Studio for Saturday at 10:30, with a deposit.", Screen: BookingScreen },
  courses: { label: "LearnLink on a phone: a learner halfway through a JAMB physics course, lessons ticked off.", Screen: CourseScreen },
  property: { label: "PropertyLink on a phone: a verified agent's 2 bedroom flat in Yaba, with the full cost to move in.", Screen: PropertyScreen },
  school: { label: "SchoolLink on a phone: a JSS 2 report card with totals and grades, as a parent sees it.", Screen: SchoolScreen },
  care: { label: "CareLink on a phone: a patient's wristband number and visit, checked in and with the doctor.", Screen: CareScreen },
};
