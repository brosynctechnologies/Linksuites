import Link from "next/link";
import type { ReactNode } from "react";
import { cx } from "./badges";

/**
 * The order ticket: a market docket with a perforated top edge, the order
 * number set large, item lines, and the total at the foot. Server-safe.
 */

export interface TicketLine {
  key: string;
  quantity: number;
  name: string;
  variant?: string | null;
  detail?: ReactNode;
  amount: string;
}

export interface TicketRow {
  label: ReactNode;
  value: ReactNode;
  tone?: "default" | "discount" | "muted";
}

export function OrderTicket({
  number,
  kicker = "Order",
  status,
  meta,
  lines,
  rows,
  total,
  totalLabel = "Total",
  footer,
  href,
  compact = false,
  className,
}: {
  number: number | string;
  kicker?: ReactNode;
  status?: ReactNode;
  meta?: ReactNode;
  lines?: TicketLine[];
  rows?: TicketRow[];
  total: string;
  totalLabel?: ReactNode;
  footer?: ReactNode;
  href?: string;
  compact?: boolean;
  className?: string;
}) {
  const body = (
    <article className={cx("ticket text-ink", compact ? "px-4 pb-4" : "px-5 pb-5 sm:px-6 sm:pb-6")}>
      <header className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate text-xs font-medium text-muted">{kicker}</p>
          <p
            className={cx(
              "font-display leading-none font-extrabold num",
              compact ? "mt-1 text-3xl" : "mt-1.5 text-4xl",
            )}
          >
            <span className="text-subtle">#</span>
            {number}
          </p>
        </div>
        {status && <div className="flex shrink-0 flex-wrap justify-end gap-1.5 pt-0.5">{status}</div>}
      </header>

      {meta && <div className={cx("text-sm text-muted", compact ? "mt-2" : "mt-3")}>{meta}</div>}

      {lines && lines.length > 0 && (
        <ul className={cx("space-y-2.5", compact ? "mt-3" : "mt-5")}>
          {lines.map((l) => (
            <li key={l.key} className="grid grid-cols-[2.25rem_minmax(0,1fr)_auto] items-baseline gap-x-2 text-sm">
              <span className="font-semibold num">{l.quantity}×</span>
              <span className="min-w-0">
                <span className="font-medium break-words">{l.name}</span>
                {l.variant && <span className="block text-xs text-muted">{l.variant}</span>}
                {l.detail && <span className="block text-xs text-subtle">{l.detail}</span>}
              </span>
              <span className="font-medium num">{l.amount}</span>
            </li>
          ))}
        </ul>
      )}

      {rows && rows.length > 0 && (
        <dl className={cx("space-y-1.5 border-t border-line pt-3 text-sm", compact ? "mt-3" : "mt-5")}>
          {rows.map((r, i) => (
            <div
              key={i}
              className={cx(
                "flex justify-between gap-3",
                r.tone === "discount" ? "text-ink" : r.tone === "muted" ? "text-subtle" : "text-ink-2",
              )}
            >
              <dt className="min-w-0">{r.label}</dt>
              <dd className="shrink-0 num">{r.value}</dd>
            </div>
          ))}
        </dl>
      )}

      <div className={cx("ticket-tear flex items-baseline justify-between gap-3", compact ? "mt-3 pt-3" : "mt-4 pt-4")}>
        <span className="text-sm font-semibold">{totalLabel}</span>
        <span className={cx("font-display font-bold num", compact ? "text-xl" : "text-2xl")}>{total}</span>
      </div>

      {footer && <div className="mt-4">{footer}</div>}
    </article>
  );

  return (
    <div className={cx("ticket-shadow", className)}>
      {href ? (
        <Link href={href} className="group block rounded-b-panel focus-visible:outline-offset-4">
          {body}
        </Link>
      ) : (
        body
      )}
    </div>
  );
}
