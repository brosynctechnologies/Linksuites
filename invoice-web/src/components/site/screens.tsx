import type { CSSProperties, ReactNode } from "react";
import { DocStamp } from "@/components/invoice-preview";
import { cx } from "@/components/styles";
import { PhoneFrame } from "@/components/suite/phone";

/**
 * InvoiceLink's real screens, redrawn for the public site in the app's own faces and tokens (the frame speaks LinkSuite,
 * the screens speak the app). Each one names the component it mirrors. Sample names and naira amounts are a business's
 * own figures, never a LinkSuite price.
 */

const tiny = "text-[0.6875rem] leading-snug";

/** StatusBadge from components/ui.tsx: a dot plus the word, so status never relies on colour. */
function Status({ tone, children }: { tone: "ink" | "green"; children: ReactNode }) {
  return (
    <span
      className={cx(
        "inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-[0.6875rem] font-medium whitespace-nowrap",
        tone === "ink" ? "bg-ink-wash text-ink-dark" : "bg-paid-wash text-[#0e5c45]",
      )}
    >
      <span className={cx("h-1.5 w-1.5 shrink-0 rounded-full", tone === "ink" ? "bg-ink" : "bg-paid")} />
      {children}
    </span>
  );
}

/** The amount card at the top of the client's invoice page (src/app/i/[token]/public-invoice.tsx), with Pay now. */
function PayCard({ className }: { className?: string }) {
  return (
    <div className={cx("rounded-panel border border-rule bg-paper p-3.5", className)}>
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <p className={`${tiny} text-muted`}>
            Invoice <span className="tabular-nums">INV-0042</span>
          </p>
          <p className="font-figure amount mt-1 text-[1.75rem] leading-none text-graphite">₦183,750</p>
          <p className={`mt-1.5 ${tiny} text-slate-600`}>Due 15 Oct 2026</p>
        </div>
        <Status tone="ink">Viewed</Status>
      </div>
      <span className="mt-3.5 flex min-h-10 items-center justify-center rounded-control bg-ink text-xs font-semibold text-white">
        Pay ₦183,750 now
      </span>
      <span className="mt-2 flex min-h-9 items-center justify-center rounded-control border border-rule bg-paper text-xs font-semibold text-graphite">
        Download PDF
      </span>
    </div>
  );
}

/** The client's phone: the sender's header (components/public-doc.tsx), then the amount due and Pay now. */
function ClientInvoiceScreen() {
  return (
    <div className="bg-desk pb-6 font-sans text-graphite">
      <div className="flex items-center gap-2.5 border-t-4 border-b border-t-ink border-b-rule bg-paper px-4 pt-7 pb-3">
        <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-[6px] bg-ink text-[0.6875rem] font-bold text-white">AD</span>
        <span className="min-w-0">
          <span className="block truncate font-display text-[0.9375rem] font-semibold">Ada Designs Ltd</span>
          <span className={`block truncate ${tiny} text-muted`}>hello@adadesigns.ng</span>
        </span>
      </div>
      <PayCard className="mx-3 mt-3" />
      <div className="mx-3 mt-3 rounded-doc bg-paper px-3.5 pt-3.5 pb-6 shadow-paper">
        <div className="flex items-start justify-between border-b-2 border-ink pb-2.5">
          <span className="text-[0.8125rem] font-bold">Ada Designs Ltd</span>
          <span className="font-display text-lg leading-none font-semibold text-ink">Invoice</span>
        </div>
        <p className={`mt-2.5 ${tiny} text-muted`}>Bill to</p>
        <p className="text-xs font-semibold">Tunde Bakare</p>
        <p className={`${tiny} text-slate-600`}>Bakare Foods</p>
      </div>
    </div>
  );
}

/** The reminder email InvoiceLink sends for an unpaid invoice (the API's invoice.reminder message). */
function ReminderEmail({ className, style }: { className?: string; style?: CSSProperties }) {
  return (
    <div className={cx("rounded-doc bg-paper p-3.5 font-sans text-graphite shadow-paper", className)} style={style}>
      <div className="flex items-center gap-2 border-b border-rule-soft pb-2.5">
        <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-ink-wash text-[0.6875rem] font-bold text-ink-dark">AD</span>
        <span className="min-w-0">
          <span className="block truncate text-xs font-semibold">Ada Designs Ltd</span>
          <span className={`block truncate ${tiny} text-muted`}>To Tunde Bakare</span>
        </span>
        <span className={`ml-auto shrink-0 ${tiny} text-muted tabular-nums`}>12 Oct</span>
      </div>
      <p className="mt-2.5 text-xs leading-snug font-semibold">Reminder: invoice INV-0042 from Ada Designs Ltd is due 15 Oct</p>
      <div className="max-sm:hidden">
        <p className={`mt-1.5 ${tiny} text-slate-600`}>
          Hi Tunde, this is a friendly reminder that invoice INV-0042 has <span className="amount">₦183,750</span> outstanding, due on 15 Oct.
        </p>
        <p className={`mt-2 ${tiny} font-semibold text-ink underline underline-offset-2`}>View or pay it here</p>
      </div>
    </div>
  );
}

/** "Payments & receipts" from the client's invoice page, once paid, with the app's Paid stamp (DocStamp). */
function PaidReceipt({ className, style }: { className?: string; style?: CSSProperties }) {
  return (
    <div className={cx("rounded-panel border border-rule bg-paper p-3.5 font-sans text-graphite shadow-paper", className)} style={style}>
      <div className="flex items-start justify-between gap-2">
        <p className="text-[0.8125rem] font-semibold">Payments &amp; receipts</p>
        <Status tone="green">Paid</Status>
      </div>
      <div className="mt-2 flex items-end justify-between gap-2 border-t border-rule-soft pt-2">
        <div className="min-w-0">
          <p className="amount text-sm font-semibold">₦183,750</p>
          <p className={`${tiny} text-muted`}>
            <span className="tabular-nums">14 Oct 2026</span>, receipt RCT-0019
          </p>
        </div>
        <div className="-mr-1 -mb-1 shrink-0">
          <DocStamp kind="Paid" />
        </div>
      </div>
    </div>
  );
}

const paint = (i: number) => ({ "--ls-i": i }) as CSSProperties;

/**
 * The hero: the life of one invoice. Tunde gets the reminder, opens INV-0042 on his phone with its Pay now button, and the
 * payment comes back as a receipt stamped Paid. The wet-paint sheen crosses the three once, in that order.
 */
export function PaperTrail() {
  return (
    <div
      role="img"
      aria-label="InvoiceLink at work: Tunde Bakare gets a reminder for invoice INV-0042 from Ada Designs Ltd, opens it on his phone with a Pay now button for ₦183,750, and the payment shows as a receipt stamped Paid."
      className="relative h-[26rem] overflow-hidden rounded-ls-panel bg-app-wash sm:h-[28rem]"
    >
      <div aria-hidden="true">
        <div className="absolute top-[5.25rem] left-1/2 -translate-x-1/2 sm:top-8 sm:left-6 sm:translate-x-0">
          <div className="ls-paint rounded-[2.1rem]" style={paint(1)}>
            <PhoneFrame label="">
              <ClientInvoiceScreen />
            </PhoneFrame>
          </div>
        </div>
        <div className="absolute top-3 left-1/2 w-[17rem] -translate-x-1/2 sm:top-10 sm:right-5 sm:left-auto sm:w-[15.5rem] sm:translate-x-0 sm:rotate-[1.5deg]">
          <ReminderEmail className="ls-paint" style={paint(0)} />
        </div>
        <div className="absolute right-2 bottom-3 w-[13.5rem] -rotate-2 sm:right-8 sm:bottom-9 sm:w-[16rem]">
          <PaidReceipt className="ls-paint" style={paint(2)} />
        </div>
      </div>
    </div>
  );
}

/**
 * A crop of a real screen for a feature: it fills the feature's wash stage down to the floor, top first, so anything
 * taller is cut at the floor rather than at the top.
 */
function Crop({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div
      className={cx(
        "h-full w-full max-w-[19rem] overflow-hidden rounded-t-panel border border-b-0 border-rule bg-desk p-3 font-sans text-graphite",
        className,
      )}
    >
      {children}
    </div>
  );
}

/** Feature: the client's invoice page with Pay now (public-invoice.tsx). */
export function PayNowCrop() {
  return (
    <div role="img" aria-label="The invoice your client opens: ₦183,750 due on 15 October, with a Pay now button." className="flex h-full w-full justify-center">
      <Crop className="pb-0">
        <div aria-hidden="true" className="h-full">
          <PayCard className="h-full rounded-b-none border-b-0" />
        </div>
      </Crop>
    </div>
  );
}

/** Feature: the invoice's activity (Timeline in components/doc-view-parts.tsx), newest first. */
export function ActivityCrop() {
  const events = [
    { t: "Paid in full", d: "14 Oct 2026, 16:05" },
    { t: "Reminder sent", d: "12 Oct 2026, 09:00" },
    { t: "Viewed by client", d: "2 Oct 2026, 18:40" },
    { t: "Sent", d: "1 Oct 2026, 11:15" },
  ];
  return (
    <div
      role="img"
      aria-label="An invoice's activity: sent, viewed by the client, a reminder sent, then paid in full."
      className="flex h-full w-full justify-center"
    >
      <Crop className="bg-paper px-4 pt-4 pb-3">
        <div aria-hidden="true">
          <p className="mb-3 text-sm font-semibold">Activity</p>
          <ol className="relative space-y-3 before:absolute before:top-2 before:bottom-2 before:left-[3.5px] before:w-px before:bg-rule">
            {events.map((e, i) => (
              <li key={e.t} className="relative flex gap-3 text-[0.8125rem]">
                <span className={cx("relative mt-1.5 h-2 w-2 shrink-0 rounded-full ring-4 ring-paper", i === 0 ? "bg-ink" : "bg-slate-300")} />
                <span className="min-w-0">
                  <span className="block font-medium">{e.t}</span>
                  <span className={`block ${tiny} text-muted tabular-nums`}>{e.d}</span>
                </span>
              </li>
            ))}
          </ol>
        </div>
      </Crop>
    </div>
  );
}

/** Feature: the quote your client opens (src/app/q/[token]/public-quote.tsx), with Accept and Decline. */
export function QuoteCrop() {
  return (
    <div role="img" aria-label="A quote for ₦450,000, valid until 30 October, with Accept quote and Decline buttons." className="flex h-full w-full justify-center">
      <Crop className="pb-0">
        <div aria-hidden="true" className="h-full rounded-t-panel border border-b-0 border-rule bg-paper p-3.5">
          <div className="flex items-start justify-between gap-2">
            <div>
              <p className={`${tiny} text-muted`}>
                Quote <span className="tabular-nums">QT-0007</span>
              </p>
              <p className="font-figure amount mt-1 text-[1.75rem] leading-none">₦450,000</p>
              <p className={`mt-1.5 ${tiny} text-slate-600`}>Valid until 30 Oct 2026</p>
            </div>
            <Status tone="ink">Sent</Status>
          </div>
          <div className="mt-3.5 flex gap-2">
            <span className="flex min-h-10 flex-1 items-center justify-center rounded-control bg-ink text-xs font-semibold text-white">Accept quote</span>
            <span className="flex min-h-10 items-center justify-center rounded-control border border-rule bg-paper px-3 text-xs font-semibold">
              Decline
            </span>
          </div>
        </div>
      </Crop>
    </div>
  );
}

/** Feature: the profit report (src/app/app/reports/page.tsx): period totals and paired monthly bars. */
export function ProfitCrop() {
  const months = [
    { m: "Jul", inc: 62, exp: 20 },
    { m: "Aug", inc: 78, exp: 24 },
    { m: "Sep", inc: 55, exp: 31 },
    { m: "Oct", inc: 92, exp: 22 },
    { m: "Nov", inc: 70, exp: 27 },
  ];
  return (
    <div
      role="img"
      aria-label="The profit report: ₦4,860,000 income, ₦1,315,000 expenses and ₦3,545,000 profit, with income and expenses by month."
      className="flex h-full w-full justify-center"
    >
      <Crop className="bg-paper px-4 pt-4 pb-0">
        <div aria-hidden="true">
          <dl className="grid grid-cols-3 gap-2 border-b border-rule-soft pb-3">
            {[
              ["Income", "₦4,860,000"],
              ["Expenses", "₦1,315,000"],
              ["Profit", "₦3,545,000"],
            ].map(([k, v]) => (
              <div key={k} className="min-w-0">
                <dt className={`${tiny} text-muted`}>{k}</dt>
                <dd className={cx("font-figure amount text-[0.875rem] leading-tight", k === "Profit" && "text-paid")}>{v}</dd>
              </div>
            ))}
          </dl>
          <div className="mt-3 flex h-24 items-end gap-3 border-b border-graphite/60">
            {months.map((x) => (
              <div key={x.m} className="flex h-full flex-1 items-end justify-center gap-0.5">
                <span className="w-1/2 max-w-4 rounded-t-[2px] bg-ink" style={{ height: `${x.inc}%` }} />
                <span className="w-1/2 max-w-4 rounded-t-[2px] bg-amber" style={{ height: `${x.exp}%` }} />
              </div>
            ))}
          </div>
          <div className="flex gap-3 pt-1 pb-3">
            {months.map((x) => (
              <span key={x.m} className={`flex-1 text-center ${tiny} text-muted`}>
                {x.m}
              </span>
            ))}
          </div>
        </div>
      </Crop>
    </div>
  );
}
