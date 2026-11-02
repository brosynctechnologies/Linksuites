import Link from "next/link";
import { formatDay } from "@/lib/dates";
import { formatMoney } from "@/lib/money";
import type { InvoiceSummaryDto, QuoteSummaryDto } from "@/lib/types";
import { DataList, InvoiceStatusBadge, QuoteStatusBadge, cx, dataRowClass } from "./ui";

const INVOICE_COLS = "6.5rem minmax(0,1fr) 6.5rem minmax(8rem,auto) 7.5rem";
const QUOTE_COLS = "6.5rem minmax(0,1fr) 7rem minmax(8rem,auto) 7rem";

/** `baseCurrency`: the business currency; foreign-currency invoices then also show their base amount. */
export function InvoiceList({ invoices, baseCurrency, busy }: { invoices: InvoiceSummaryDto[]; baseCurrency?: string; busy?: boolean }) {
  return (
    <DataList
      template={INVOICE_COLS}
      busy={busy}
      head={[{ label: "Number" }, { label: "Client" }, { label: "Due" }, { label: "Amount", align: "right" }, { label: "Status" }]}
    >
      {invoices.map((inv) => (
        <li key={inv.id}>
          <Link href={`/app/invoices/${inv.id}`} className={cx(dataRowClass, "hover:bg-slate-50")}>
            <span className="hidden text-sm font-semibold tabular-nums md:block">{inv.number}</span>
            <span className="row-span-2 min-w-0 md:row-span-1">
              <span className="block truncate font-medium text-graphite">{inv.clientName || "—"}</span>
              <span className="block truncate text-xs text-muted tabular-nums md:hidden">
                {inv.number}, due {formatDay(inv.dueDate)}
              </span>
            </span>
            <span className="hidden text-sm text-slate-600 tabular-nums md:block">{formatDay(inv.dueDate)}</span>
            <span className="text-right">
              <span className="amount block font-semibold text-graphite">{formatMoney(inv.totalMinor, inv.currency)}</span>
              {baseCurrency && inv.currency !== baseCurrency && typeof inv.totalBaseMinor === "number" && (
                <span className="amount block text-xs text-muted">≈ {formatMoney(inv.totalBaseMinor, baseCurrency)}</span>
              )}
              {inv.balanceMinor > 0 && inv.balanceMinor !== inv.totalMinor && (
                <span className="amount block text-xs text-amber-text">{formatMoney(inv.balanceMinor, inv.currency)} due</span>
              )}
            </span>
            <span className="col-start-2 justify-self-end md:col-start-auto md:justify-self-start">
              <InvoiceStatusBadge status={inv.status} />
            </span>
          </Link>
        </li>
      ))}
    </DataList>
  );
}

export function QuoteList({ quotes, busy }: { quotes: QuoteSummaryDto[]; busy?: boolean }) {
  return (
    <DataList
      template={QUOTE_COLS}
      busy={busy}
      head={[{ label: "Number" }, { label: "Client" }, { label: "Valid until" }, { label: "Amount", align: "right" }, { label: "Status" }]}
    >
      {quotes.map((q) => (
        <li key={q.id}>
          <Link href={`/app/quotes/${q.id}`} className={cx(dataRowClass, "hover:bg-slate-50")}>
            <span className="hidden text-sm font-semibold tabular-nums md:block">{q.number}</span>
            <span className="row-span-2 min-w-0 md:row-span-1">
              <span className="block truncate font-medium text-graphite">{q.clientName || "—"}</span>
              <span className="block truncate text-xs text-muted tabular-nums md:hidden">
                {q.number}, valid until {formatDay(q.expiryDate)}
              </span>
            </span>
            <span className="hidden text-sm text-slate-600 tabular-nums md:block">{formatDay(q.expiryDate)}</span>
            <span className="amount text-right font-semibold text-graphite">{formatMoney(q.totalMinor, q.currency)}</span>
            <span className="col-start-2 justify-self-end md:col-start-auto md:justify-self-start">
              <QuoteStatusBadge status={q.status} />
            </span>
          </Link>
        </li>
      ))}
    </DataList>
  );
}
